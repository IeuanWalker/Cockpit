using Cockpit.Features.Canvas;
using Cockpit.Features.Git.Models;
using Cockpit.Features.Sessions.Models;
using GitHub.Copilot;
using Microsoft.Extensions.Logging;

namespace Cockpit.Features.Sessions;

public sealed partial class SessionFeature
{
	public async Task<SessionModel> CreateSession(string? workingDirectory, CancellationToken cancellationToken = default)
	{
		CopilotClient? client = null;
		CopilotSession? sdkSession = null;
		bool sdkSessionRegistered = false;

		try
		{
			cancellationToken.ThrowIfCancellationRequested();

			ModelInfo defaultModel = await _modelFeature.GetDefaultModel(cancellationToken);
			GitHub.Copilot.ProviderConfig? providerConfig = await _modelFeature.GetProviderConfig(defaultModel.Id, cancellationToken);

			// GetContext spawns git subprocesses, and its result isn't needed until the SessionModel
			// is built (further below). Kick it off here so it overlaps the SDK CreateSessionAsync
			// round-trip instead of blocking before it.
			Task<GitContext?> gitContextTask = _gitFeature.GetContext(workingDirectory);

			// BYOK providers don't support Copilot-specific reasoning effort; always pass null for them.
			string? effectiveReasoningEffort = providerConfig is null ? defaultModel.DefaultReasoningEffort : null;

			SessionConfig config = new()
			{
				ClientName = "Cockpit",
				Model = defaultModel.Id,
				ReasoningEffort = effectiveReasoningEffort,
				Streaming = true,
				InfiniteSessions = new InfiniteSessionConfig
				{
					Enabled = true
				},
				WorkingDirectory = workingDirectory,
				OnPermissionRequest = _permissionHandler.HandlePermissionRequest,
				OnUserInputRequest = _userInputHandler.HandleUserInputRequest,
				OnElicitationRequest = _elicitationHandler.HandleElicitationRequest,
				Hooks = _hooksFactory.CreateHooks(defaultModel.Id, effectiveReasoningEffort, workingDirectory),
				EnableConfigDiscovery = true,
				Provider = providerConfig
			};

			if(_appSettingsFeature.CanvasEnabled)
			{
				config.RequestCanvasRenderer = true;
				config.RequestExtensions = true;
				config.ExtensionInfo = new ExtensionInfo { Source = "cockpit", Name = "canvas-provider" };
				config.Canvases =
				[
					CreateCockpitCanvasDeclaration()
				];
				config.CanvasHandler = new SessionCanvasHandler(_canvasWindowManager);
			}

			ApplySystemMessageCustomization(config, defaultModel);

			cancellationToken.ThrowIfCancellationRequested();

			client = await _clientFeature.GetClientAsync(cancellationToken);
			CopilotSession createdSession = await client.CreateSessionAsync(config, cancellationToken);
			sdkSession = createdSession;

			if(cancellationToken.IsCancellationRequested)
			{
				await client.DeleteSessionAsync(createdSession.SessionId, CancellationToken.None);
				await createdSession.DisposeAsync();
				sdkSession = null;
				cancellationToken.ThrowIfCancellationRequested();
			}

			_sdkRegistry.Register(createdSession, evt =>
			{
				_logger.LogDebug("Session {SessionId} event: {EventType}", createdSession.SessionId, evt.Type);
				HandleSessionEvent(createdSession, evt);
			});
			sdkSessionRegistered = true;

			// git context was started before the SDK round-trip; collect it now that it's needed.
			GitContext? gitContext = await gitContextTask;

			SessionModel chatSession = new()
			{
				Id = createdSession.SessionId,
				Title = "New Session",
				CreatedAt = DateTime.UtcNow,
				LastActivity = DateTime.UtcNow,
				AgentRunState = AgentRunStateEnum.Idle,
				Context = new()
				{
					CurrentWorkingDirectory = workingDirectory,
					WorkspacePath = createdSession.WorkspacePath,
					GitRoot = gitContext?.GitRoot,
					Repository = gitContext?.Repository,
					Branch = gitContext?.Branch
				},
				Model = defaultModel,
				ReasoningEffort = defaultModel.DefaultReasoningEffort,
				SdkState = SdkSessionStateEnum.Resumed
			};

			SessionWorkingDirectoryNormalizer.ApplyContextConsistency(chatSession.Context);

			cancellationToken.ThrowIfCancellationRequested();

			await LoadContextPanelDataAsync(chatSession, createdSession);

			cancellationToken.ThrowIfCancellationRequested();

			_sdkSessionByokId[chatSession.Id] = chatSession.ByokConfigId;

			_sessionListFeature.AddSession(chatSession);
			_sessionListFeature.NotifyStateChanged(chatSession.Id, SessionChangeKind.SessionCollection);

			// These three writes are best-effort metadata used only to *resume* the session later
			// (saved model id, agent, agent-mode). They have no bearing on the SessionModel returned
			// to the UI or on SwitchCurrentSessionAsync below, so there's no reason to make the user
			// wait on disk I/O — persist them in the background and just log any failure.
			_ = PersistSessionMetadataInBackground(chatSession);

			await SwitchCurrentSessionAsync(chatSession);

			return chatSession;
		}
		catch(OperationCanceledException)
		{
			if(client is not null && sdkSession is not null)
			{
				string sessionId = sdkSession.SessionId;
				try
				{
					await client.DeleteSessionAsync(sessionId, CancellationToken.None);

					if(sdkSessionRegistered)
					{
						_sdkRegistry.Remove(sessionId);
					}

					await sdkSession.DisposeAsync();
					sdkSession = null;
				}
				catch(Exception cleanupEx)
				{
					_logger.LogWarning(cleanupEx, "Failed to cleanup canceled session {SessionId}", sessionId);
				}
			}

			throw;
		}
		catch(Exception ex)
		{
			_logger.LogError(ex, "Failed to create new session");
			throw;
		}
	}

	/// <summary>
	/// Persists the best-effort resume metadata (model, agent, agent-mode) for a freshly created
	/// session off the critical path. The three writes target independent files, so they run
	/// concurrently; the whole operation is offloaded to the thread pool so session creation never
	/// blocks on disk I/O. Each writer already swallows its own failures, but any unexpected fault
	/// is logged here rather than left unobserved.
	/// </summary>
	Task PersistSessionMetadataInBackground(SessionModel chatSession)
		=> Task.Run(async () =>
		{
			try
			{
				await Task.WhenAll(
					_modelFeature.SaveSessionModel(chatSession),
					_agentPersistence.SaveSessionAgent(chatSession),
					_sessionModePersistence.SaveSessionMode(chatSession, CancellationToken.None));
			}
			catch(Exception ex)
			{
				_logger.LogWarning(ex, "Background persistence failed for new session {SessionId}", chatSession.Id);
			}
		});

}
