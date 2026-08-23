using Cockpit.Features.Agents.Models;
using Cockpit.Features.Canvas;
using Cockpit.Features.Sessions.Models;
using GitHub.Copilot;
using Microsoft.Extensions.Logging;

namespace Cockpit.Features.Sessions;

public sealed partial class SessionFeature
{
	/// <summary>
	/// Promotes a loaded session to fully resumed by flipping flags. Call this before sending the first
	/// message on a session that was loaded via <see cref="LoadSession"/>. If the session has not been
	/// loaded yet, delegates to <see cref="LoadSession"/> first.
	/// </summary>
	public async Task<bool> ResumeSession(string sessionId)
	{
		SessionModel? session = _sessionListFeature.Sessions.FirstOrDefault(s => s.Id == sessionId);
		if(session is null)
		{
			_logger.LogWarning("Session {SessionId} not found", sessionId);
			return false;
		}

		if(session.Lifecycle.SdkState == SdkSessionStateEnum.Resumed)
		{
			_logger.LogInformation("Session {SessionId} already resumed", sessionId);
			return true;
		}

		if(session.Lifecycle.SdkState != SdkSessionStateEnum.Loaded)
		{
			bool loaded = await LoadSession(sessionId);
			if(!loaded)
			{
				return false;
			}
		}

		if(!session.Lifecycle.TryTransitionSdkState(SdkSessionStateEnum.Loaded, SdkSessionStateEnum.Resumed))
		{
			return session.Lifecycle.SdkState == SdkSessionStateEnum.Resumed;
		}
		_logger.LogInformation("Session {SessionId} promoted from loaded to resumed", sessionId);
		return true;
	}

	public async Task RestartSession(string sessionId, string newModelId, string? newReasoningEffort = null, ProviderConfig? providerConfig = null, CancellationToken cancellationToken = default)
	{
		SessionModel? session = _sessionListFeature.Sessions.FirstOrDefault(s => s.Id == sessionId);
		if(session is null)
		{
			await RestartSessionCore(sessionId, newModelId, newReasoningEffort, providerConfig, null, null, cancellationToken);
			return;
		}

		await session.Lifecycle.SdkTransitionGate.WaitAsync(cancellationToken);
		try
		{
			SdkLifecycleTransition restartTransition = session.Lifecycle.CaptureSdkTransition();
			await RestartSessionCore(
				sessionId,
				newModelId,
				newReasoningEffort,
				providerConfig,
				session,
				restartTransition,
				cancellationToken);
		}
		finally
		{
			session.Lifecycle.SdkTransitionGate.Release();
		}
	}

	async Task RestartSessionCore(
		string sessionId,
		string newModelId,
		string? newReasoningEffort,
		ProviderConfig? providerConfig,
		SessionModel? expectedSession,
		SdkLifecycleTransition? restartTransition,
		CancellationToken cancellationToken)
	{
		CopilotSession? newSdkSession = null;
		bool registered = false;
		bool restartCompleted = false;

		try
		{
			if(!_sdkRegistry.TryRemove(sessionId, out CopilotSession? existingSession))
			{
				throw new InvalidOperationException($"Session {sessionId} not found");
			}

			SessionModel? chatSession = _sessionListFeature.Sessions.FirstOrDefault(s => s.Id == sessionId);

			await existingSession.DisposeAsync();
			_logger.LogInformation("Destroyed session {SessionId} for restart", sessionId);

			CopilotClient client = await _clientFeature.GetClientAsync(cancellationToken);
			// BYOK providers don't support Copilot-specific reasoning effort (e.g. KV-based "medium").
			// Always pass null when a provider config is present so the SDK doesn't emit reasoning includes.
			string? effectiveReasoningEffort = providerConfig is null ? newReasoningEffort : null;

			bool hasMessages = chatSession?.Messages.Count > 0;
			if(hasMessages)
			{
				ResumeSessionConfig resumeConfig = new()
				{
					Model = newModelId,
					ReasoningEffort = effectiveReasoningEffort,
					Streaming = true,
					EnableConfigDiscovery = true,
					OnPermissionRequest = _permissionHandler.HandlePermissionRequest,
					OnUserInputRequest = _userInputHandler.HandleUserInputRequest,
					OnElicitationRequest = _elicitationHandler.HandleElicitationRequest,
					Hooks = _hooksFactory.CreateHooks(newModelId, effectiveReasoningEffort, chatSession?.Context.CurrentWorkingDirectory),
					Provider = providerConfig
				};

				ApplySystemMessageCustomization(resumeConfig, chatSession?.Model, newModelId);

				if(_appSettingsFeature.CanvasEnabled)
				{
					resumeConfig.RequestCanvasRenderer = true;
					resumeConfig.RequestExtensions = true;
					resumeConfig.ExtensionInfo = new ExtensionInfo { Source = "cockpit", Name = "canvas-provider" };
					resumeConfig.Canvases =
					[
						CreateCockpitCanvasDeclaration()
					];
					resumeConfig.CanvasHandler = new SessionCanvasHandler(_canvasWindowManager);
				}
				newSdkSession = await client.ResumeSessionAsync(sessionId, resumeConfig, cancellationToken);
			}
			else
			{
				SessionConfig createConfig = new()
				{
					Model = newModelId,
					ReasoningEffort = effectiveReasoningEffort,
					Streaming = true,
					EnableConfigDiscovery = true,
					InfiniteSessions = new InfiniteSessionConfig
					{
						Enabled = true
					},
					WorkingDirectory = chatSession?.Context.CurrentWorkingDirectory,
					OnPermissionRequest = _permissionHandler.HandlePermissionRequest,
					OnUserInputRequest = _userInputHandler.HandleUserInputRequest,
					OnElicitationRequest = _elicitationHandler.HandleElicitationRequest,
					Hooks = _hooksFactory.CreateHooks(newModelId, effectiveReasoningEffort, chatSession?.Context.CurrentWorkingDirectory),
					Provider = providerConfig
				};

				ApplySystemMessageCustomization(createConfig, chatSession?.Model, newModelId);

				if(_appSettingsFeature.CanvasEnabled)
				{
					createConfig.RequestCanvasRenderer = true;
					createConfig.RequestExtensions = true;
					createConfig.ExtensionInfo = new ExtensionInfo { Source = "cockpit", Name = "canvas-provider" };
					createConfig.Canvases =
					[
						CreateCockpitCanvasDeclaration()
					];
					createConfig.CanvasHandler = new SessionCanvasHandler(_canvasWindowManager);
				}
				newSdkSession = await client.CreateSessionAsync(createConfig, cancellationToken);

				if(chatSession is not null)
				{
					_sdkRegistry.Remove(chatSession.Id);
					_sdkSessionByokId.TryRemove(chatSession.Id, out _);
					string previousSessionId = chatSession.Id;
					chatSession.Id = newSdkSession.SessionId;
					await _pinnedItemsFeature.ReplaceSessionIdAsync(previousSessionId, chatSession.Id);
					chatSession.Context.WorkspacePath = newSdkSession.WorkspacePath;
				}
			}

			if(chatSession is not null)
			{
				await LoadContextPanelDataAsync(chatSession, newSdkSession);

				AgentProfile? restored = chatSession.Context.SelectedAgent is not null
					? chatSession.Context.Agents.FirstOrDefault(a =>
						string.Equals(a.Name, chatSession.Context.SelectedAgent.Name, StringComparison.OrdinalIgnoreCase) &&
						a.Source == chatSession.Context.SelectedAgent.Source)
						?? chatSession.Context.Agents.FirstOrDefault(a =>
						string.Equals(a.Name, chatSession.Context.SelectedAgent.Name, StringComparison.OrdinalIgnoreCase))
					: null;
				chatSession.Context.SelectedAgent = restored;

				if(chatSession.Context.SelectedAgent is not null)
				{
					await newSdkSession.Rpc.Agent.SelectAsync(chatSession.Context.SelectedAgent.Name, cancellationToken);
				}

				if(chatSession.Context.SelectedAgentMode != Models.SessionAgentModeEnum.Interactive)
				{
					await newSdkSession.Rpc.Mode.SetAsync(chatSession.Context.SelectedAgentMode.ToSdkSessionMode(), cancellationToken);
				}
			}

			void RegisterNewSdkSession() => _sdkRegistry.Register(newSdkSession, evt =>
			{
				_logger.LogDebug("Session {SessionId} event: {EventType}", newSdkSession.SessionId, evt.Type);
				HandleSessionEvent(newSdkSession, evt);
			});

			registered = restartTransition is null
				? RegisterWithoutLifecycleGuard()
				: expectedSession!.Lifecycle.TryRunIfSdkTransitionIsCurrent(
					restartTransition.Value,
					RegisterNewSdkSession);

			if(!registered)
			{
				await newSdkSession.DisposeAsync();
				newSdkSession = null;
				throw new InvalidOperationException($"Session {sessionId} restart was invalidated");
			}

			_sdkSessionByokId[newSdkSession.SessionId] = chatSession?.ByokConfigId;
			restartCompleted = true;

			_logger.LogInformation("Restarted session {SessionId} with model {Model}", sessionId, newModelId);

			bool RegisterWithoutLifecycleGuard()
			{
				RegisterNewSdkSession();
				return true;
			}
		}
		catch(Exception ex)
		{
			if(newSdkSession is not null && !restartCompleted)
			{
				if(registered)
				{
					_sdkRegistry.TryRemove(newSdkSession.SessionId, newSdkSession);
				}

				try
				{
					await newSdkSession.DisposeAsync();
				}
				catch(Exception disposeException)
				{
					_logger.LogWarning(disposeException, "Failed to dispose replacement SDK session {SessionId}", newSdkSession.SessionId);
				}
			}

			if(expectedSession is not null && restartTransition is not null)
			{
				expectedSession.Lifecycle.TryInvalidateSdkTransition(restartTransition.Value);
			}

			_logger.LogError(ex, "Failed to restart session {SessionId}", sessionId);
			throw;
		}
	}

	public async Task DeleteSession(string sessionId, CancellationToken cancellationToken = default)
	{
		try
		{
			if(_sdkRegistry.TryRemove(sessionId, out CopilotSession? sdkSession))
			{
				await sdkSession.DisposeAsync();
			}

			await _terminalFeature.CloseSessionAsync(sessionId);
			_userInputHandler.CancelPendingRequestsForSession(sessionId);
			_permissionHandler.CancelPendingRequestsForSession(sessionId);
			_elicitationHandler.CancelPendingRequestsForSession(sessionId);
			await _canvasWindowManager.CloseAllForSessionAsync(sessionId, cancellationToken);

			CopilotClient client = await _clientFeature.GetClientAsync(cancellationToken);
			await client.DeleteSessionAsync(sessionId, cancellationToken);

			_sessionListFeature.RemoveSession(sessionId);
			_sdkSessionByokId.TryRemove(sessionId, out _);
		}
		catch(InvalidOperationException ex) when(ex.Message.Contains("Error: Session file not found"))
		{
			_logger.LogWarning(ex, "Session {SessionId} not found during deletion - it may have already been deleted", sessionId);
			_sessionListFeature.RemoveSession(sessionId);
			_sdkSessionByokId.TryRemove(sessionId, out _);
		}
		catch(Exception ex)
		{
			_logger.LogError(ex, "Failed to delete session {SessionId}", sessionId);
		}
	}

	async Task RestartSessionWithPendingConfig(SessionModel session)
	{
		try
		{
			GitHub.Copilot.ProviderConfig? providerConfig = await _modelFeature.GetProviderConfig(session.Model.Id);

			_logger.LogInformation(
				"Restarting session {SessionId} with model {Model} and reasoning effort {ReasoningEffort}",
				session.Id,
				session.Model.Id,
				session.ReasoningEffort ?? "default"
			);

			await RestartSession(
				session.Id,
				session.Model.Id,
				session.ReasoningEffort,
				providerConfig
			);

			_logger.LogInformation("Session {SessionId} restarted successfully", session.Id);
			_sessionListFeature.NotifyStateChanged();
		}
		catch(Exception ex)
		{
			_logger.LogError(ex, "Failed to restart session {SessionId}", session.Id);
			throw;
		}
	}

	public async Task AbortSession(string sessionId)
	{
		try
		{
			if(!_sdkRegistry.TryGet(sessionId, out CopilotSession? sdkSession))
			{
				throw new InvalidOperationException($"Session {sessionId} not found in SDK sessions");
			}

			// Transition the lifecycle state first so resolving pending interactions reveals Idle.
			SessionModel? session = _sessionListFeature.Sessions.FirstOrDefault(s => s.Id == sessionId);
			session?.Lifecycle.SetAgentRunState(AgentRunStateEnum.Idle);

			// Cancel any pending permission/user-input/elicitation requests so they are removed from the UI immediately
			_permissionHandler.CancelPendingRequestsForSession(sessionId);
			_userInputHandler.CancelPendingRequestsForSession(sessionId);
			_elicitationHandler.CancelPendingRequestsForSession(sessionId);

			await sdkSession.AbortAsync();
		}
		catch(Exception ex)
		{
			_logger.LogError(ex, "Failed to abort session");
		}
	}

}
