using System.Collections.Concurrent;
using Blazor.Sonner.Services;
using Cockpit.Features.Permissions;
using Cockpit.Features.SessionEvents;
using Cockpit.Features.SessionEvents.Models;
using Cockpit.Features.Terminal;
using Cockpit.Models;
using GitHub.Copilot.SDK;
using Microsoft.Extensions.Logging;
using SessionContextModel = Cockpit.Models.SessionContext;

namespace Cockpit.Services;

public partial class UnifiedSessionManager : ISessionStateProvider
{
	readonly CopilotClientService _clientService;
	readonly ILogger<UnifiedSessionManager> _logger;
	readonly ToastService _toastService;
	readonly CopilotModelService _copilotModelService;
	readonly SessionEventProcessor _processor;
	PermissionFeature? _permissionFeature;
	readonly TerminalFeature _terminalFeature;

	// Internal: Maps sessionId -> SDK CopilotSession (for ALL resumed sessions)
	readonly ConcurrentDictionary<string, CopilotSession> _sdkSessions = new();

	public event Action? OnStateChanged;

	// All sessions (persisted + in-memory)
	public List<ChatSession> Sessions { get; private set; } = [];

	// The ONE session currently visible in UI
	public ChatSession? CurrentSession { get; private set; }

	// Activity grouping for working panel (per-session)
	public ActivityGroupModel? ActiveWorkingGroup => CurrentSession?.ActiveWorkingGroup;
	public bool IsWorking => CurrentSession?.ActiveWorkingGroup is not null && CurrentSession.ActiveWorkingGroup.Status == GroupStatusEnum.Running;

	public UnifiedSessionManager(
		CopilotClientService clientService,
		ILogger<UnifiedSessionManager> logger,
		ToastService toastService,
		CopilotModelService copilotModelService,
		TerminalFeature terminalFeature,
		SessionEventProcessor processor)
	{
		_clientService = clientService;
		_logger = logger;
		_toastService = toastService;
		_copilotModelService = copilotModelService;
		_terminalFeature = terminalFeature;
		_processor = processor;
	}

	public void SetPermissionFeature(PermissionFeature permissionFeature)
	{
		_permissionFeature = permissionFeature;
	}

	static void EnsureSessionContext(ChatSession session)
	{
		session.Context ??= SessionContextModel.CreateDefault();
	}

	void HandleSessionEvent(string sessionId, SessionEvent evt)
	{
		ChatSession? session = Sessions.FirstOrDefault(s => s.Id == sessionId);
		if(session is null)
		{
			return;
		}

		Func<ChatMessageModel, string, Task>? streamCallback = session == CurrentSession
			? (msg, text) => SessionEventHelpers.StreamSummaryTextAsync(msg, text, NotifyStateChanged)
			: null;

		_processor.Process(session, evt, streamCallback);

		if(session == CurrentSession)
		{
			NotifyStateChanged();
		}
	}


	public async Task LoadExistingSessionsAsync()
	{
		try
		{
			_logger.LogInformation("Loading existing sessions from SDK...");

			CopilotClient client = await _clientService.GetClientAsync();
			List<SessionMetadata> sessionMetadataList = await client.ListSessionsAsync();

			if(sessionMetadataList.Count == 0)
			{
				_logger.LogInformation("No existing sessions found");
				return;
			}

			_logger.LogInformation("Found {Count} existing sessions", sessionMetadataList.Count);

			ModelInfo defaultModel = await _copilotModelService.GetDefaultModel();

			// Load each session that isn't already in our list
			foreach(SessionMetadata metadata in sessionMetadataList)
			{
				if(!Sessions.Any(s => s.Id == metadata.SessionId))
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

						Sessions.Add(chatSession);
						_logger.LogInformation("Loaded session {SessionId}", chatSession.Id);
					}
					catch(Exception ex)
					{
						_logger.LogWarning(ex, "Failed to load session {SessionId}", metadata.SessionId);
					}
				}
			}

			NotifyStateChanged();
			_logger.LogInformation("Successfully loaded {Count} sessions", Sessions.Count);
		}
		catch(Exception ex)
		{
			_logger.LogError(ex, "Failed to load existing sessions");
		}
	}

	public async Task<ChatSession> CreateNewSessionAsync(string? workingDirectory = null)
	{
		try
		{
			ModelInfo defaultModel = await _copilotModelService.GetDefaultModel();

			SessionConfig config = new()
			{
				Model = defaultModel.Id,
				ReasoningEffort = defaultModel.DefaultReasoningEffort,
				Streaming = true,
				InfiniteSessions = new InfiniteSessionConfig { Enabled = true },
				WorkingDirectory = workingDirectory,
				OnPermissionRequest = _permissionFeature!.HandlePermissionRequest
			};

			CopilotClient client = await _clientService.GetClientAsync();
			CopilotSession sdkSession = await client.CreateSessionAsync(config);

			// Subscribe to session events
			sdkSession.On(evt =>
			{
				_logger.LogDebug("Session {SessionId} event: {EventType}", sdkSession.SessionId, evt.Type);
				HandleSessionEvent(sdkSession.SessionId, evt);
			});

			// Add to SDK sessions dictionary
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

			Sessions.Insert(0, chatSession);
			CurrentSession = chatSession;

			NotifyStateChanged();
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
			ChatSession? session = Sessions.FirstOrDefault(s => s.Id == sessionId);
			if(session is null)
			{
				_logger.LogWarning("Session {SessionId} not found", sessionId);
				return false;
			}

			// If session is already resumed with an active SDK connection, just switch to it
			if(session.IsResumed)
			{
				_logger.LogInformation("Session {SessionId} already resumed, switching to it", sessionId);
				SetCurrentSession(session);
				return true;
			}

			_logger.LogInformation("Resuming session {SessionId}", sessionId);

			ResumeSessionConfig config = new()
			{
				Model = session.Model.Id,
				ReasoningEffort = session.ReasoningEffort,
				Streaming = true,
				OnPermissionRequest = _permissionFeature!.HandlePermissionRequest
			};

			// Mark as resuming so the session list shows a loading indicator immediately
			session.IsResuming = true;
			NotifyStateChanged();

			CopilotClient client = await _clientService.GetClientAsync();
			CopilotSession sdkSession = await client.ResumeSessionAsync(sessionId, config);

			// Load existing messages from SDK BEFORE subscribing to live events
			// to avoid a race condition where live events modify session state concurrently with replay
			IReadOnlyList<SessionEvent> events = await sdkSession.GetMessagesAsync();
			_logger.LogInformation("Loading {Count} events for session {SessionId}", events.Count, sessionId);

			// Replay history into a temporary session so the real session is never in an intermediate
			// state — no spurious UI re-renders during replay
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

				// Fallback: finalize any active group not closed by SessionIdleEvent (e.g., abrupt termination)
				if(tempSession.ActiveWorkingGroup is not null)
				{
					_processor.FinalizeOpenGroup(tempSession);
				}
			});

			// Atomically apply replayed state to the real session, then clear loading indicator
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
			EnsureSessionContext(session);
			SessionPermissionFeature.TryRestoreSessionCommands(session, _logger);
			CurrentSession = session;

			// Subscribe to live events and register SDK session only after replay is complete
			sdkSession.On(evt =>
			{
				_logger.LogDebug("Session {SessionId} event: {EventType}", sdkSession.SessionId, evt.Type);
				HandleSessionEvent(sdkSession.SessionId, evt);
			});
			_sdkSessions.AddOrUpdate(sdkSession.SessionId, sdkSession, (_, _) => sdkSession);

			NotifyStateChanged();
			_logger.LogInformation("Successfully resumed session {SessionId} with {MessageCount} messages", sessionId, session.Messages.Count);
			return true;
		}
		catch(Exception ex) when(ex.Message.Equals("Communication error with Copilot CLI: Request session.resume failed with message: Session file is corrupted or incompatible"))
		{
			_logger.LogError(ex, "Session {SessionId} is corrupted or incompatible", sessionId);
			ChatSession? failedSession = Sessions.FirstOrDefault(s => s.Id == sessionId);
			failedSession?.IsResuming = false;
			NotifyStateChanged();
			_toastService.Error("Session Unavailable", opts =>
			{
				opts.Description = "The session file may be corrupted, incompatible, or in use by another instance. You may need to delete or exit the session running else where";
			});
			return false;
		}
		catch(Exception ex)
		{
			_logger.LogError(ex, "Failed to resume session {SessionId}", sessionId);
			ChatSession? failedSession = Sessions.FirstOrDefault(s => s.Id == sessionId);
			failedSession?.IsResuming = false;
			NotifyStateChanged();
			return false;
		}
	}

	public void SetCurrentSession(ChatSession session)
	{
		EnsureSessionContext(session);
		CurrentSession = session;

		NotifyStateChanged();
	}

	public void SetCurrentSessionContextDirectory(string directory)
	{
		if(CurrentSession is null || string.IsNullOrWhiteSpace(directory))
		{
			return;
		}

		CurrentSession.Context.CurrentDirectory = directory;
		NotifyStateChanged();
	}

	public void ToggleCurrentSessionContextSkill(string skill)
	{
		if(CurrentSession is null || string.IsNullOrWhiteSpace(skill))
		{
			return;
		}

		if(!CurrentSession.Context.AgentSkills.Remove(skill))
		{
			CurrentSession.Context.AgentSkills.Add(skill);
		}
		NotifyStateChanged();
	}

	public void ToggleCurrentSessionContextCommand(string command)
	{
		if(CurrentSession is null || string.IsNullOrWhiteSpace(command))
		{
			return;
		}

		if(!CurrentSession.Context.AllowedCommands.Remove(command))
		{
			CurrentSession.Context.AllowedCommands.Add(command);
		}
		NotifyStateChanged();
	}

	public async Task SendMessageAsync(string content, List<UserMessageDataAttachmentsItem>? attachments = null)
	{
		if(CurrentSession is null)
		{
			return;
		}

		try
		{
			// Check if session needs restart before sending message
			if(CurrentSession.RequiresRestart)
			{
				await RestartSessionWithPendingConfigAsync();
			}

			if(!_sdkSessions.TryGetValue(CurrentSession.Id, out CopilotSession? sdkSession))
			{
				throw new InvalidOperationException($"Session {CurrentSession.Id} not found in SDK sessions");
			}

			CurrentSession.Status = SessionStatus.Running;

			// Optimistically add the message immediately so the UI doesn't feel frozen
			ChatMessageModel optimisticMessage = new()
			{
				Content = content,
				IsUser = true,
				Timestamp = DateTime.Now,
				Type = MessageTypeEnum.Text,
				IsComplete = false
			};
			CurrentSession.Messages.Add(optimisticMessage);
			NotifyStateChanged();

			await sdkSession.SendAsync(new MessageOptions
			{
				Prompt = content,
				Attachments = attachments
			});
		}
		catch(Exception ex)
		{
			_logger.LogError(ex, "Failed to send message");
			CurrentSession.Status = SessionStatus.Error;
			NotifyStateChanged();
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

			// Perform restart (destroy + resume with new config)
			await RestartSessionAsync(
				CurrentSession.Id,
				CurrentSession.Model.Id,
				CurrentSession.ReasoningEffort
			);

			// Clear restart flag
			CurrentSession.RequiresRestart = false;

			_logger.LogInformation("Session {SessionId} restarted successfully", CurrentSession.Id);
			NotifyStateChanged();
		}
		catch(Exception ex)
		{
			_logger.LogError(ex, "Failed to restart session {SessionId}", CurrentSession.Id);
			// Keep RequiresRestart = true so user can retry
			throw;
		}
	}

	public async Task RestartSessionAsync(string sessionId, string newModelId, string? newReasoningEffort = null, CancellationToken cancellationToken = default)
	{
		try
		{
			// 1. Get existing session from dictionary
			if(!_sdkSessions.TryRemove(sessionId, out CopilotSession? existingSession))
			{
				throw new InvalidOperationException($"Session {sessionId} not found");
			}

			ChatSession? chatSession = Sessions.FirstOrDefault(s => s.Id == sessionId);

			// 2. Destroy the in-memory session object
			await existingSession.DisposeAsync();
			_logger.LogInformation("Destroyed session {SessionId} for restart", sessionId);

			CopilotClient client = await _clientService.GetClientAsync(cancellationToken);
			CopilotSession newSdkSession;

			// 3. For sessions with no messages the CLI hasn't persisted them yet — create fresh
			bool hasMessages = chatSession?.Messages.Count > 0;
			if(hasMessages)
			{
				ResumeSessionConfig resumeConfig = new()
				{
					Model = newModelId,
					ReasoningEffort = newReasoningEffort,
					Streaming = true,
					OnPermissionRequest = _permissionFeature!.HandlePermissionRequest
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
					OnPermissionRequest = _permissionFeature!.HandlePermissionRequest
				};
				newSdkSession = await client.CreateSessionAsync(createConfig, cancellationToken);

				// Update the ChatSession with the new SDK session ID
				if(chatSession is not null)
				{
					_sdkSessions.TryRemove(chatSession.Id, out _);
					chatSession.Id = newSdkSession.SessionId;
				}
			}

			// 4. Re-subscribe to session events
			newSdkSession.On(evt =>
			{
				_logger.LogDebug("Session {SessionId} event: {EventType}", newSdkSession.SessionId, evt.Type);
				HandleSessionEvent(newSdkSession.SessionId, evt);
			});

			// 5. Update dictionary with new/resumed session
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
			// Remove from SDK sessions and dispose
			if(_sdkSessions.TryRemove(sessionId, out CopilotSession? sdkSession))
			{
				await sdkSession.DisposeAsync();
			}

			// Clean up terminal session
			_terminalFeature.CloseSession(sessionId);

			// Delete from Copilot CLI
			CopilotClient client = await _clientService.GetClientAsync();
			await client.DeleteSessionAsync(sessionId);

			ChatSession? session = Sessions.FirstOrDefault(s => s.Id == sessionId);
			if(session is not null)
			{
				Sessions.Remove(session);
				if(CurrentSession?.Id == sessionId)
				{
					CurrentSession = Sessions.FirstOrDefault();
				}
				NotifyStateChanged();
			}
		}
		catch(InvalidOperationException ex) when(ex.Message.Contains("Error: Session file not found"))
		{
			_logger.LogWarning(ex, "Session {SessionId} not found during deletion - it may have already been deleted", sessionId);
			// Even if the session was not found, we can consider it deleted, so remove from our list and update UI
			ChatSession? session = Sessions.FirstOrDefault(s => s.Id == sessionId);
			if(session is not null)
			{
				Sessions.Remove(session);
				if(CurrentSession?.Id == sessionId)
				{
					CurrentSession = Sessions.FirstOrDefault();
				}
				NotifyStateChanged();
			}
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

	public void NotifyStateChanged() => OnStateChanged?.Invoke();

	// ISessionStateProvider implementation
	IReadOnlyList<ChatSession> ISessionStateProvider.GetSessions() => Sessions;
}
