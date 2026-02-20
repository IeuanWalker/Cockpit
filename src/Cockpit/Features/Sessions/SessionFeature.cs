using System.Collections.Concurrent;
using Blazor.Sonner.Services;
using Cockpit.Features.CopilotModels;
using Cockpit.Features.Permissions;
using Cockpit.Features.Sdk;
using Cockpit.Features.SessionEvents;
using Cockpit.Features.SessionEvents.Models;
using Cockpit.Features.Terminal;
using Cockpit.Models;
using GitHub.Copilot.SDK;
using Microsoft.Extensions.Logging;
using SessionContextModel = Cockpit.Models.SessionContext;

namespace Cockpit.Features.Sessions;

public partial class SessionFeature
{
	readonly CopilotClientFeature _clientFeature;
	readonly ILogger<SessionFeature> _logger;
	readonly ToastService _toastService;
	readonly CopilotModelFeature _copilotModelFeature;
	readonly SessionEventProcessor _processor;
	readonly TerminalFeature _terminalFeature;
	readonly SessionListFeature _sessionListFeature;
	readonly IPermissionHandler _permissionHandler;

	readonly ConcurrentDictionary<string, CopilotSession> _sdkSessions = new();

	// Pass-through convenience properties so components only need to inject SessionFeature
	public ChatSession? CurrentSession => _sessionListFeature.CurrentSession;
	public IReadOnlyList<ChatSession> Sessions => _sessionListFeature.Sessions;
	public event Action? OnStateChanged
	{
		add => _sessionListFeature.OnStateChanged += value;
		remove => _sessionListFeature.OnStateChanged -= value;
	}
	public ActivityGroupModel? ActiveWorkingGroup => CurrentSession?.ActiveWorkingGroup;
	public bool IsWorking => CurrentSession?.ActiveWorkingGroup is not null && CurrentSession.ActiveWorkingGroup.Status == GroupStatusEnum.Running;

	public SessionFeature(
		CopilotClientFeature clientFeature,
		ILogger<SessionFeature> logger,
		ToastService toastService,
		CopilotModelFeature copilotModelFeature,
		TerminalFeature terminalFeature,
		SessionEventProcessor processor,
		SessionListFeature sessionListFeature,
		IPermissionHandler permissionHandler)
	{
		_clientFeature = clientFeature;
		_logger = logger;
		_toastService = toastService;
		_copilotModelFeature = copilotModelFeature;
		_terminalFeature = terminalFeature;
		_processor = processor;
		_sessionListFeature = sessionListFeature;
		_permissionHandler = permissionHandler;
	}

	public async Task LoadExistingSessionsAsync()
	{
		try
		{
			_logger.LogInformation("Loading existing sessions from SDK...");

			CopilotClient client = await _clientFeature.GetClientAsync();
			List<SessionMetadata> sessionMetadataList = await client.ListSessionsAsync();

			if(sessionMetadataList.Count == 0)
			{
				_logger.LogInformation("No existing sessions found");
				return;
			}

			_logger.LogInformation("Found {Count} existing sessions", sessionMetadataList.Count);

			ModelInfo defaultModel = await _copilotModelFeature.GetDefaultModel();

			foreach(SessionMetadata metadata in sessionMetadataList)
			{
				if(!_sessionListFeature.Sessions.Any(s => s.Id == metadata.SessionId))
				{
					try
					{
						ChatSession chatSession = new()
						{
							Id = metadata.SessionId,
							Title = metadata.Summary ?? $"Session {metadata.SessionId[..8]}",
							CreatedAt = metadata.StartTime,
							LastActivity = metadata.ModifiedTime,
							Status = SessionStatus.Idle,
							Model = defaultModel,
							ReasoningEffort = defaultModel.DefaultReasoningEffort,
							Context = SessionContextModel.CreateDefault(metadata.Context?.Cwd, metadata.Context?.Branch)
						};

						_sessionListFeature.AddSession(chatSession);
						_logger.LogInformation("Loaded session {SessionId}", chatSession.Id);
					}
					catch(Exception ex)
					{
						_logger.LogWarning(ex, "Failed to load session {SessionId}", metadata.SessionId);
					}
				}
			}

			_sessionListFeature.NotifyStateChanged();
			_logger.LogInformation("Successfully loaded {Count} sessions", _sessionListFeature.Sessions.Count);
		}
		catch(Exception ex)
		{
			_logger.LogError(ex, "Failed to load existing sessions");
		}
	}

	void HandleSessionEvent(string sessionId, SessionEvent evt)
	{
		ChatSession? session = _sessionListFeature.Sessions.FirstOrDefault(s => s.Id == sessionId);
		if(session is null)
		{
			return;
		}

		Func<ChatMessageModel, string, Task>? streamCallback = session == _sessionListFeature.CurrentSession
			? (msg, text) => SessionEventHelpers.StreamSummaryTextAsync(msg, text, _sessionListFeature.NotifyStateChanged)
			: null;

		lock(session.SessionEventLock)
		{
			_processor.Process(session, evt, streamCallback);
		}

		if(session == _sessionListFeature.CurrentSession)
		{
			_sessionListFeature.NotifyStateChanged();
		}
	}

	public async Task<ChatSession> CreateNewSessionAsync(string? workingDirectory = null)
	{
		try
		{
			ModelInfo defaultModel = await _copilotModelFeature.GetDefaultModel();

			SessionConfig config = new()
			{
				Model = defaultModel.Id,
				ReasoningEffort = defaultModel.DefaultReasoningEffort,
				Streaming = true,
				InfiniteSessions = new InfiniteSessionConfig { Enabled = true },
				WorkingDirectory = workingDirectory,
				OnPermissionRequest = _permissionHandler.HandlePermissionRequest
			};

			CopilotClient client = await _clientFeature.GetClientAsync();
			CopilotSession sdkSession = await client.CreateSessionAsync(config);

			sdkSession.On(evt =>
			{
				_logger.LogDebug("Session {SessionId} event: {EventType}", sdkSession.SessionId, evt.Type);
				HandleSessionEvent(sdkSession.SessionId, evt);
			});

			_sdkSessions.TryAdd(sdkSession.SessionId, sdkSession);

			ChatSession chatSession = new()
			{
				Id = sdkSession.SessionId,
				Title = !string.IsNullOrEmpty(workingDirectory)
					? Path.GetFileName(workingDirectory)
					: "New Session",
				CreatedAt = DateTime.Now,
				LastActivity = DateTime.Now,
				Status = SessionStatus.Idle,
				WorkspacePath = sdkSession.WorkspacePath,
				WorkingDirectory = workingDirectory,
				Context = SessionContextModel.CreateDefault(workingDirectory ?? sdkSession.WorkspacePath),
				Model = defaultModel,
				ReasoningEffort = defaultModel.DefaultReasoningEffort,
				IsResumed = true
			};

			_sessionListFeature.AddSession(chatSession);
			_sessionListFeature.SetCurrentSession(chatSession);

			return chatSession;
		}
		catch(Exception ex)
		{
			_logger.LogError(ex, "Failed to create new session");
			throw;
		}
	}

	public async Task<bool> ResumeSessionAsync(string sessionId)
	{
		try
		{
			ChatSession? session = _sessionListFeature.Sessions.FirstOrDefault(s => s.Id == sessionId);
			if(session is null)
			{
				_logger.LogWarning("Session {SessionId} not found", sessionId);
				return false;
			}

			if(session.IsResumed)
			{
				_logger.LogInformation("Session {SessionId} already resumed, switching to it", sessionId);
				_sessionListFeature.SetCurrentSession(session);
				return true;
			}

			_logger.LogInformation("Resuming session {SessionId}", sessionId);

			ResumeSessionConfig config = new()
			{
				Model = session.Model.Id,
				ReasoningEffort = session.ReasoningEffort,
				Streaming = true,
				OnPermissionRequest = _permissionHandler.HandlePermissionRequest
			};

			session.IsResuming = true;
			_sessionListFeature.NotifyStateChanged();

			CopilotClient client = await _clientFeature.GetClientAsync();
			CopilotSession sdkSession = await client.ResumeSessionAsync(sessionId, config);

			IReadOnlyList<SessionEvent> events = await sdkSession.GetMessagesAsync();
			_logger.LogInformation("Loading {Count} events for session {SessionId}", events.Count, sessionId);

			ChatSession tempSession = new()
			{
				Id = sessionId,
				Title = session.Title,
				Status = SessionStatus.Idle,
				Model = session.Model,
				ReasoningEffort = session.ReasoningEffort,
				Context = session.Context,
				LastActivity = session.LastActivity,
				CreatedAt = session.CreatedAt
			};

			await Task.Run(() =>
			{
				foreach(SessionEvent evt in events)
				{
					_processor.Process(tempSession, evt);
				}

				if(tempSession.ActiveWorkingGroup is not null)
				{
					_processor.FinalizeOpenGroup(tempSession);
				}
			});

			session.IsResuming = false;
			session.Messages = tempSession.Messages;
			session.ActiveWorkingGroup = null;
			session.LastActivity = tempSession.LastActivity;
			if(session.Title != tempSession.Title)
			{
				session.Title = tempSession.Title;
			}

			session.Status = SessionStatus.Idle;
			session.IsResumed = true;
			session.WorkspacePath = sdkSession.WorkspacePath;
			SessionPermissionFeature.TryRestoreSessionCommands(session, _logger);

			sdkSession.On(evt =>
			{
				_logger.LogDebug("Session {SessionId} event: {EventType}", sdkSession.SessionId, evt.Type);
				HandleSessionEvent(sdkSession.SessionId, evt);
			});
			_sdkSessions.AddOrUpdate(sdkSession.SessionId, sdkSession, (_, _) => sdkSession);

			// SetCurrentSession handles EnsureSessionContext + CurrentSession assignment + NotifyStateChanged
			_sessionListFeature.SetCurrentSession(session);

			_logger.LogInformation("Successfully resumed session {SessionId} with {MessageCount} messages", sessionId, session.Messages.Count);
			return true;
		}
		catch(Exception ex) when(ex.Message.Equals("Communication error with Copilot CLI: Request session.resume failed with message: Session file is corrupted or incompatible"))
		{
			_logger.LogError(ex, "Session {SessionId} is corrupted or incompatible", sessionId);
			ChatSession? failedSession = _sessionListFeature.Sessions.FirstOrDefault(s => s.Id == sessionId);
			failedSession?.IsResuming = false;
			_sessionListFeature.NotifyStateChanged();
			_toastService.Error("Session Unavailable", opts =>
			{
				opts.Description = "The session file may be corrupted, incompatible, or in use by another instance. You may need to delete or exit the session running else where";
			});
			return false;
		}
		catch(Exception ex)
		{
			_logger.LogError(ex, "Failed to resume session {SessionId}", sessionId);
			ChatSession? failedSession = _sessionListFeature.Sessions.FirstOrDefault(s => s.Id == sessionId);
			failedSession?.IsResuming = false;
			_sessionListFeature.NotifyStateChanged();
			return false;
		}
	}

	public async Task SendMessageAsync(string content, List<UserMessageDataAttachmentsItem>? attachments = null)
	{
		if(CurrentSession is null)
		{
			return;
		}

		try
		{
			if(CurrentSession.RequiresRestart)
			{
				await RestartSessionWithPendingConfigAsync();
			}

			if(!_sdkSessions.TryGetValue(CurrentSession.Id, out CopilotSession? sdkSession))
			{
				throw new InvalidOperationException($"Session {CurrentSession.Id} not found in SDK sessions");
			}

			ChatMessageModel? optimisticMessage = null;
			lock(CurrentSession.SessionEventLock)
			{
				bool agentWasBusy = CurrentSession.ActiveWorkingGroup is not null;
				CurrentSession.Status = SessionStatus.Running;

				optimisticMessage = new ChatMessageModel
				{
					Content = content,
					IsUser = true,
					Timestamp = DateTime.UtcNow,
					Type = MessageTypeEnum.Text,
					IsComplete = false,
					IsPending = agentWasBusy
				};
				CurrentSession.Messages.Add(optimisticMessage);
			}
			_sessionListFeature.NotifyStateChanged();

			string sentMessageId = await sdkSession.SendAsync(new MessageOptions
			{
				Prompt = content,
				Attachments = attachments
			});

			if(!string.IsNullOrWhiteSpace(sentMessageId) && optimisticMessage is not null)
			{
				lock(CurrentSession.SessionEventLock)
				{
					if(CurrentSession.Messages.Contains(optimisticMessage) && !optimisticMessage.IsComplete)
					{
						optimisticMessage.Id = sentMessageId;
					}
				}
			}
		}
		catch(Exception ex)
		{
			_logger.LogError(ex, "Failed to send message");
			CurrentSession.Status = SessionStatus.Error;
			_sessionListFeature.NotifyStateChanged();
		}
	}

	async Task RestartSessionWithPendingConfigAsync()
	{
		if(CurrentSession is null || !CurrentSession.RequiresRestart)
		{
			return;
		}

		try
		{
			_logger.LogInformation(
				"Restarting session {SessionId} with model {Model} and reasoning effort {ReasoningEffort}",
				CurrentSession.Id,
				CurrentSession.Model.Id,
				CurrentSession.ReasoningEffort ?? "default"
			);

			await RestartSessionAsync(
				CurrentSession.Id,
				CurrentSession.Model.Id,
				CurrentSession.ReasoningEffort
			);

			CurrentSession.RequiresRestart = false;

			_logger.LogInformation("Session {SessionId} restarted successfully", CurrentSession.Id);
			_sessionListFeature.NotifyStateChanged();
		}
		catch(Exception ex)
		{
			_logger.LogError(ex, "Failed to restart session {SessionId}", CurrentSession.Id);
			throw;
		}
	}

	public async Task RestartSessionAsync(string sessionId, string newModelId, string? newReasoningEffort = null, CancellationToken cancellationToken = default)
	{
		try
		{
			if(!_sdkSessions.TryRemove(sessionId, out CopilotSession? existingSession))
			{
				throw new InvalidOperationException($"Session {sessionId} not found");
			}

			ChatSession? chatSession = _sessionListFeature.Sessions.FirstOrDefault(s => s.Id == sessionId);

			await existingSession.DisposeAsync();
			_logger.LogInformation("Destroyed session {SessionId} for restart", sessionId);

			CopilotClient client = await _clientFeature.GetClientAsync(cancellationToken);
			CopilotSession newSdkSession;

			bool hasMessages = chatSession?.Messages.Count > 0;
			if(hasMessages)
			{
				ResumeSessionConfig resumeConfig = new()
				{
					Model = newModelId,
					ReasoningEffort = newReasoningEffort,
					Streaming = true,
					OnPermissionRequest = _permissionHandler.HandlePermissionRequest
				};
				newSdkSession = await client.ResumeSessionAsync(sessionId, resumeConfig, cancellationToken);
			}
			else
			{
				SessionConfig createConfig = new()
				{
					Model = newModelId,
					ReasoningEffort = newReasoningEffort,
					Streaming = true,
					InfiniteSessions = new InfiniteSessionConfig { Enabled = true },
					WorkingDirectory = chatSession?.WorkingDirectory,
					OnPermissionRequest = _permissionHandler.HandlePermissionRequest
				};
				newSdkSession = await client.CreateSessionAsync(createConfig, cancellationToken);

				if(chatSession is not null)
				{
					_sdkSessions.TryRemove(chatSession.Id, out _);
					chatSession.Id = newSdkSession.SessionId;
				}
			}

			newSdkSession.On(evt =>
			{
				_logger.LogDebug("Session {SessionId} event: {EventType}", newSdkSession.SessionId, evt.Type);
				HandleSessionEvent(newSdkSession.SessionId, evt);
			});

			_sdkSessions.TryAdd(newSdkSession.SessionId, newSdkSession);

			_logger.LogInformation("Restarted session {SessionId} with model {Model}", sessionId, newModelId);
		}
		catch(Exception ex)
		{
			_logger.LogError(ex, "Failed to restart session {SessionId}", sessionId);
			throw;
		}
	}

	public async Task DeleteSessionAsync(string sessionId)
	{
		try
		{
			if(_sdkSessions.TryRemove(sessionId, out CopilotSession? sdkSession))
			{
				await sdkSession.DisposeAsync();
			}

			_terminalFeature.CloseSession(sessionId);

			CopilotClient client = await _clientFeature.GetClientAsync();
			await client.DeleteSessionAsync(sessionId);

			_sessionListFeature.RemoveSession(sessionId);
			_sessionListFeature.NotifyStateChanged();
		}
		catch(InvalidOperationException ex) when(ex.Message.Contains("Error: Session file not found"))
		{
			_logger.LogWarning(ex, "Session {SessionId} not found during deletion - it may have already been deleted", sessionId);
			_sessionListFeature.RemoveSession(sessionId);
			_sessionListFeature.NotifyStateChanged();
		}
		catch(Exception ex)
		{
			_logger.LogError(ex, "Failed to delete session {SessionId}", sessionId);
		}
	}

	public async Task AbortCurrentSessionAsync()
	{
		if(CurrentSession is null)
		{
			return;
		}

		try
		{
			if(!_sdkSessions.TryGetValue(CurrentSession.Id, out CopilotSession? sdkSession))
			{
				throw new InvalidOperationException($"Session {CurrentSession.Id} not found in SDK sessions");
			}

			await sdkSession.AbortAsync();
		}
		catch(Exception ex)
		{
			_logger.LogError(ex, "Failed to abort session");
		}
	}
}
