using Cockpit.Features.Agents.Models;
using Cockpit.Features.Git.Models;
using Cockpit.Features.Permissions;
using Cockpit.Features.SessionEvents;
using Cockpit.Features.SessionEvents.Models;
using Cockpit.Features.Sessions.Models;
using GitHub.Copilot;
using GitHub.Copilot.Rpc;
using Microsoft.Extensions.Logging;
using SdkPlugin = GitHub.Copilot.Rpc.Plugin;
using SdkSessionMetadata = GitHub.Copilot.SessionMetadata;

namespace Cockpit.Features.Sessions;

public sealed partial class SessionFeature
{
	Task? _loadExistingSessionsTask;
	readonly Lock _loadGate = new();

	public Task LoadExistingSessions()
	{
		lock(_loadGate)
		{
			if(_loadExistingSessionsTask is null || _loadExistingSessionsTask.IsCanceled || _loadExistingSessionsTask.IsFaulted)
			{
				_loadExistingSessionsTask = RefreshExistingSessions();
			}

			return _loadExistingSessionsTask;
		}
	}

	public async Task RefreshExistingSessions()
	{
		try
		{
			_logger.LogInformation("Loading existing sessions from SDK...");

			CopilotClient client = await _clientFeature.GetClientAsync();
			IList<SdkSessionMetadata> sessionMetadataList = await client.ListSessionsAsync();

			if(sessionMetadataList.Count == 0)
			{
				_logger.LogInformation("No existing sessions found");
				return;
			}

			_logger.LogInformation("Found {Count} existing sessions", sessionMetadataList.Count);

			ModelInfo defaultModel = await _modelFeature.GetDefaultModel();
			foreach(SdkSessionMetadata metadata in sessionMetadataList)
			{
				if(_sessionListFeature.Sessions.Any(s => s.Id == metadata.SessionId))
				{
					continue;
				}

				try
				{
					SessionModel chatSession = new()
					{
						Id = metadata.SessionId,
						Title = metadata.Summary ?? $"Session {metadata.SessionId[..8]}",
						CreatedAt = metadata.StartTime.UtcDateTime,
						LastActivity = metadata.ModifiedTime.UtcDateTime,
						Status = SessionStatusEnum.Idle,
						Model = defaultModel,
						ReasoningEffort = defaultModel.DefaultReasoningEffort,
						Context = new()
						{
							CurrentWorkingDirectory = metadata.Context?.WorkingDirectory ?? string.Empty,
							WorkspacePath = null,
							GitRoot = metadata.Context?.GitRoot,
							Repository = metadata.Context?.Repository,
							Branch = metadata.Context?.Branch
						}
					};

					_sessionListFeature.AddSession(chatSession);
					_logger.LogInformation("Loaded session {SessionId}", chatSession.Id);
				}
				catch(Exception ex)
				{
					_logger.LogWarning(ex, "Failed to load session {SessionId}", metadata.SessionId);
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

	public async Task<SessionModel> CreateSession(string workingDirectory)
	{
		try
		{
			ModelInfo defaultModel = await _modelFeature.GetDefaultModel();
			ProviderConfig? providerConfig = await _modelFeature.GetProviderConfig(defaultModel.Id);
			GitContext gitContext = await _gitFeature.GetContext(workingDirectory);

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

			CopilotClient client = await _clientFeature.GetClientAsync();
			CopilotSession sdkSession = await client.CreateSessionAsync(config);
			_sdkRegistry.Register(sdkSession, evt =>
			{
				_logger.LogDebug("Session {SessionId} event: {EventType}", sdkSession.SessionId, evt.Type);
				HandleSessionEvent(sdkSession.SessionId, evt);
			});

			SessionModel chatSession = new()
			{
				Id = sdkSession.SessionId,
				Title = !string.IsNullOrEmpty(workingDirectory)
					? Path.GetFileName(workingDirectory)
					: "New Session",
				CreatedAt = DateTime.UtcNow,
				LastActivity = DateTime.UtcNow,
				Status = SessionStatusEnum.Idle,
				Context = new()
				{
					CurrentWorkingDirectory = workingDirectory,
					WorkspacePath = sdkSession.WorkspacePath,
					GitRoot = gitContext.GitRoot,
					Repository = gitContext.Repository,
					Branch = gitContext.Branch
				},
				Model = defaultModel,
				ReasoningEffort = defaultModel.DefaultReasoningEffort,
				SdkState = SdkSessionStateEnum.Resumed
			};

			await LoadContextPanelDataAsync(chatSession, sdkSession);

			_sdkSessionByokId[chatSession.Id] = chatSession.ByokConfigId;

			_sessionListFeature.AddSession(chatSession);

			await _modelFeature.SaveSessionModel(chatSession);
			await _agentPersistence.SaveSessionAgent(chatSession);
			await _sessionModePersistence.SaveSessionMode(chatSession);

			await SwitchCurrentSessionAsync(chatSession);

			return chatSession;
		}
		catch(Exception ex)
		{
			_logger.LogError(ex, "Failed to create new session");
			throw;
		}
	}

	/// <summary>
	/// Loads a session by replaying its history into the UI with <c>DisableResume=true</c>, which
	/// suppresses the <c>session.resume</c> event so that merely viewing a session does not update
	/// <c>LastActivity</c>. The SDK session is registered and ready to send messages; calling
	/// <see cref="ResumeSession"/> on first message send simply promotes the flags.
	/// </summary>
	public async Task<bool> LoadSession(string sessionId)
	{
		try
		{
			SessionModel? session = _sessionListFeature.Sessions.FirstOrDefault(s => s.Id == sessionId);
			if(session is null)
			{
				_logger.LogWarning("Session {SessionId} not found", sessionId);
				return false;
			}

			if(session.SdkState != SdkSessionStateEnum.NotLoaded)
			{
				_logger.LogInformation("Session {SessionId} already loaded or loading, switching to it", sessionId);
				await SwitchCurrentSessionAsync(session);

				// Guard: eviction may have cleared the session between the state check and SwitchCurrentSessionAsync.
				// If state is now NotLoaded, fall through to perform a full reload.
				if(session.SdkState != SdkSessionStateEnum.NotLoaded)
				{
					return true;
				}

				_logger.LogInformation("Session {SessionId} was evicted during switch; performing full load", sessionId);
			}

			_logger.LogInformation("Loading session {SessionId}", sessionId);

			if(string.IsNullOrWhiteSpace(session.Context.CurrentWorkingDirectory) || !Directory.Exists(session.Context.CurrentWorkingDirectory))
			{
				session.Context.CurrentWorkingDirectory = null;
			}

			ProviderConfig? providerConfig = await _modelFeature.GetProviderConfig(session.Model.Id);

			// BYOK providers don't support Copilot-specific reasoning effort; always pass null for them.
			// This also guards against stale "medium" values loaded from pre-switch sessions before
			// TryRestoreModelSettings has had a chance to clear the effort.
			string? effectiveReasoningEffort = providerConfig is null ? session.ReasoningEffort : null;

			ResumeSessionConfig config = new()
			{
				ClientName = "Cockpit",
				EnableConfigDiscovery = true,
				Model = session.Model.Id,
				ReasoningEffort = effectiveReasoningEffort,
				Streaming = true,
				SuppressResumeEvent = true,
				WorkingDirectory = session.Context.CurrentWorkingDirectory,
				OnPermissionRequest = _permissionHandler.HandlePermissionRequest,
				OnUserInputRequest = _userInputHandler.HandleUserInputRequest,
				OnElicitationRequest = _elicitationHandler.HandleElicitationRequest,
				Hooks = _hooksFactory.CreateHooks(session.Model.Id, effectiveReasoningEffort, session.Context.CurrentWorkingDirectory, disableResume: true),
				Provider = providerConfig
			};

			session.SdkState = SdkSessionStateEnum.Loading;
			_sessionListFeature.NotifyStateChanged();

			CopilotClient client = await _clientFeature.GetClientAsync();
			CopilotSession sdkSession = await client.ResumeSessionAsync(sessionId, config);

			await LoadContextPanelDataAsync(session, sdkSession);

			bool registered = false;
			try
			{
				IReadOnlyList<SessionEvent> events = await sdkSession.GetEventsAsync(CancellationToken.None);
				_logger.LogInformation("Loading {Count} events for session {SessionId}", events.Count, sessionId);

				SessionModel tempSession = new()
				{
					Id = sessionId,
					Title = session.Title,
					Status = SessionStatusEnum.Idle,
					Model = session.Model,
					ReasoningEffort = session.ReasoningEffort,
					Context = session.Context,
					LastActivity = session.LastActivity,
					CreatedAt = session.CreatedAt,
					SuppressFinishedNotification = true
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
				tempSession.SuppressFinishedNotification = false;

				// Any message still IsPending after replay was sent while the session was
				// mid-turn and never picked up by a subsequent assistant.turn_start (the session
				// was interrupted). Clear the flag so history renders in the correct order and
				// without the "Pending…" indicator.
				foreach(ChatMessageModel msg in tempSession.Messages)
				{
					msg.IsPending = false;
				}

				session.Messages = tempSession.Messages;
				session.ActiveWorkingGroup = null;
				if(session.Title != tempSession.Title)
				{
					session.Title = tempSession.Title;
				}

				session.Status = SessionStatusEnum.Idle;
				session.SdkState = SdkSessionStateEnum.Loaded;
				session.Context.WorkspacePath = sdkSession.WorkspacePath;
				SessionPermissionFeature.TryRestoreSessionCommands(session, _logger);
				await _modelFeature.TryRestoreModelSettings(session);
				await _agentPersistence.TryRestoreSessionAgent(session);
				if(session.Context.SelectedAgent is not null)
				{
					await sdkSession.Rpc.Agent.SelectAsync(session.Context.SelectedAgent.Name);
				}

				await _sessionModePersistence.TryRestoreSessionMode(session);
				if(session.Context.SelectedAgentMode != Models.SessionAgentModeEnum.Interactive)
				{
					await sdkSession.Rpc.Mode.SetAsync(session.Context.SelectedAgentMode.ToSdkSessionMode());
				}

				_sdkRegistry.Register(sdkSession, evt =>
				{
					_logger.LogDebug("Session {SessionId} event: {EventType}", sdkSession.SessionId, evt.Type);
					HandleSessionEvent(sdkSession.SessionId, evt);
				});
				registered = true;
				_sdkSessionByokId[sessionId] = session.ByokConfigId;

				await SwitchCurrentSessionAsync(session);
				_logger.LogInformation("Successfully loaded session {SessionId} with {MessageCount} messages", sessionId, session.Messages.Count);
				return true;
			}
			finally
			{
				if(!registered)
				{
					await sdkSession.DisposeAsync();
					session.SdkState = SdkSessionStateEnum.NotLoaded;
				}
			}
		}
		catch(Exception ex) when(ex.Message.Equals("Communication error with Copilot CLI: Request session.resume failed with message: Session file is corrupted or incompatible"))
		{
			_logger.LogError(ex, "Session {SessionId} is corrupted or incompatible", sessionId);
			SessionModel? failedSession = _sessionListFeature.Sessions.FirstOrDefault(s => s.Id == sessionId);
			failedSession?.SdkState = SdkSessionStateEnum.NotLoaded;
			_sessionListFeature.NotifyStateChanged();
			_toastService.Error("Session Unavailable", opts =>
			{
				opts.Description = "The session file may be corrupted, incompatible, or in use by another instance. You may need to delete or exit the session running else where";
			});
			return false;
		}
		catch(Exception ex)
		{
			_logger.LogError(ex, "Failed to load session {SessionId}", sessionId);
			SessionModel? failedSession = _sessionListFeature.Sessions.FirstOrDefault(s => s.Id == sessionId);
			failedSession?.SdkState = SdkSessionStateEnum.NotLoaded;
			_sessionListFeature.NotifyStateChanged();
			return false;
		}
	}

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

		if(session.SdkState == SdkSessionStateEnum.Resumed)
		{
			_logger.LogInformation("Session {SessionId} already resumed", sessionId);
			return true;
		}

		if(session.SdkState != SdkSessionStateEnum.Loaded)
		{
			bool loaded = await LoadSession(sessionId);
			if(!loaded)
			{
				return false;
			}
		}

		session.SdkState = SdkSessionStateEnum.Resumed;
		_logger.LogInformation("Session {SessionId} promoted from loaded to resumed", sessionId);
		return true;
	}

	public async Task RestartSession(string sessionId, string newModelId, string? newReasoningEffort = null, ProviderConfig? providerConfig = null, CancellationToken cancellationToken = default)
	{
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
			CopilotSession newSdkSession;

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
				newSdkSession = await client.CreateSessionAsync(createConfig, cancellationToken);

				if(chatSession is not null)
				{
					_sdkRegistry.Remove(chatSession.Id);
					_sdkSessionByokId.TryRemove(chatSession.Id, out _);
					chatSession.Id = newSdkSession.SessionId;
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

			_sdkRegistry.Register(newSdkSession, evt =>
			{
				_logger.LogDebug("Session {SessionId} event: {EventType}", newSdkSession.SessionId, evt.Type);
				HandleSessionEvent(newSdkSession.SessionId, evt);
			});
			_sdkSessionByokId[newSdkSession.SessionId] = chatSession?.ByokConfigId;

			_logger.LogInformation("Restarted session {SessionId} with model {Model}", sessionId, newModelId);
		}
		catch(Exception ex)
		{
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
			ProviderConfig? providerConfig = await _modelFeature.GetProviderConfig(session.Model.Id);

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

			// Clear status history so that resolving pending requests restores to Idle, not Running
			SessionModel? session = _sessionListFeature.Sessions.FirstOrDefault(s => s.Id == sessionId);
			if(session is not null)
			{
				lock(session.StatusHistoryLock)
				{
					session.StatusHistory.Clear();
				}
			}

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

	/// <summary>
	/// Debug helper: clears the current session's messages and replays them from the SDK history,
	/// introducing timestamp-proportional delays between events so the replay feels like a live session.
	/// </summary>
	public async Task ReplayCurrentSessionAsync(CancellationToken cancellationToken = default)
	{
		SessionModel? session = _sessionListFeature.CurrentSession;
		if(session is null)
		{
			_logger.LogWarning("ReplayCurrentSession: no current session");
			return;
		}

		if(!_sdkRegistry.TryGet(session.Id, out CopilotSession? sdkSession))
		{
			_logger.LogWarning("ReplayCurrentSession: SDK session not found for {SessionId}", session.Id);
			return;
		}

		try
		{
			IReadOnlyList<SessionEvent> events = await sdkSession.GetEventsAsync(cancellationToken);
			_logger.LogInformation("Replaying {Count} events for session {SessionId}", events.Count, session.Id);

			lock(session.SessionEventLock)
			{
				session.Messages.Clear();
				session.ActiveWorkingGroup = null;
				session.MessagesSnapshot = [];
			}
			_sessionListFeature.NotifyStateChanged();

			// Same parentId-based immediate-mode detection used during live sessions applies
			// here — no pre-processing or reordering needed.
			Task streamCallback(ChatMessageModel msg, string text) => SessionEventHelpers.StreamSummaryTextAsync(msg, text, _sessionListFeature.NotifyStateChanged);

			DateTimeOffset? prevTimestamp = null;
			foreach(SessionEvent evt in events)
			{
				cancellationToken.ThrowIfCancellationRequested();

				if(prevTimestamp.HasValue)
				{
					TimeSpan realGap = evt.Timestamp - prevTimestamp.Value;
					int delayMs = (int)Math.Clamp(realGap.TotalMilliseconds, 50, 3000);
					await Task.Delay(delayMs, cancellationToken);
				}

				lock(session.SessionEventLock)
				{
					_processor.Process(session, evt, streamCallback);
					session.MessagesSnapshot = [.. session.Messages];
				}
				_sessionListFeature.NotifyStateChanged();

				prevTimestamp = evt.Timestamp;
			}

			lock(session.SessionEventLock)
			{
				if(session.ActiveWorkingGroup is not null)
				{
					_processor.FinalizeOpenGroup(session);
				}
				session.MessagesSnapshot = [.. session.Messages];
			}
			_sessionListFeature.NotifyStateChanged();

			_logger.LogInformation("Replay complete for session {SessionId} — {MessageCount} messages", session.Id, session.Messages.Count);
		}
		catch(OperationCanceledException)
		{
			_logger.LogInformation("Replay cancelled for session {SessionId}", session.Id);
		}
		catch(Exception ex)
		{
			_logger.LogError(ex, "Replay failed for session {SessionId}", session.Id);
		}
	}

	async Task LoadContextPanelDataAsync(SessionModel session, CopilotSession sdkSession)
	{
		Task<List<AgentProfile>> agentsTask = _agentFeature.LoadSessionAgentsAsync(sdkSession, session.Context.GitRoot);
		Task<List<InstructionsSources>> instructionsTask = _instructionsFeature.LoadSessionInstructionsAsync(sdkSession);
		Task<List<McpServer>> mcpTask = _mcpFeature.LoadSessionMcpServersAsync(sdkSession);
		Task<List<Skill>> skillsTask = _skillsFeature.LoadSessionSkillsAsync(sdkSession);
		Task<List<SdkPlugin>> pluginsTask = _pluginsFeature.LoadSessionPluginsAsync(sdkSession);

		await Task.WhenAll(agentsTask, instructionsTask, mcpTask, skillsTask, pluginsTask);

		session.Context.Agents = agentsTask.Result;
		session.Context.Instructions = instructionsTask.Result;
		session.Context.McpServers = mcpTask.Result;
		session.Context.Skills = skillsTask.Result;
		session.Context.Plugins = pluginsTask.Result;
	}
}
