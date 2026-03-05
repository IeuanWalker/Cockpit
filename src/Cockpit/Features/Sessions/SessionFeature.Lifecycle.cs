using Cockpit.Features.Agents.Models;
using Cockpit.Features.Git.Models;
using Cockpit.Features.Permissions;
using Cockpit.Features.SessionEvents;
using Cockpit.Features.SessionEvents.Models;
using Cockpit.Features.Sessions.Models;
using GitHub.Copilot.SDK;
using Microsoft.Extensions.Logging;

namespace Cockpit.Features.Sessions;

public sealed partial class SessionFeature
{
	public async Task LoadExistingSessions()
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

			ModelInfo defaultModel = await _modelFeature.GetDefaultModel();
			foreach(SessionMetadata metadata in sessionMetadataList)
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
						CreatedAt = metadata.StartTime,
						LastActivity = metadata.ModifiedTime,
						Status = SessionStatusEnum.Idle,
						Model = defaultModel,
						ReasoningEffort = defaultModel.DefaultReasoningEffort,
						Context = new()
						{
							CurrentWorkingDirectory = metadata.Context?.Cwd ?? string.Empty,
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
			GitContext gitContext = await _gitFeature.GetContext(workingDirectory);

			IReadOnlyList<AgentProfile> globalAgents = _globalAgentFeature.Agents;
			IReadOnlyList<AgentProfile> repoAgents = await _sessionAgentFeature.Load(gitContext.GitRoot);

			List<AgentProfile> agents = [.. globalAgents, .. repoAgents];

			SessionConfig config = new()
			{
				Model = defaultModel.Id,
				ReasoningEffort = defaultModel.DefaultReasoningEffort,
				Streaming = true,
				InfiniteSessions = new InfiniteSessionConfig
				{
					Enabled = true
				},
				WorkingDirectory = workingDirectory,
				OnPermissionRequest = _permissionHandler.HandlePermissionRequest,
				OnUserInputRequest = _userInputHandler.HandleUserInputRequest,
				CustomAgents = agents.Count > 0 ? [.. agents.Select(x => x.Config)] : null
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
				CreatedAt = DateTime.Now,
				LastActivity = DateTime.Now,
				Status = SessionStatusEnum.Idle,
				Context = new()
				{
					CurrentWorkingDirectory = workingDirectory,
					WorkspacePath = sdkSession.WorkspacePath,
					GitRoot = gitContext.GitRoot,
					Repository = gitContext.Repository,
					Branch = gitContext.Branch,
					RepoAgents = agents
				},
				Model = defaultModel,
				ReasoningEffort = defaultModel.DefaultReasoningEffort,
				SdkState = SdkSessionStateEnum.Resumed
			};

			_sessionListFeature.AddSession(chatSession);

			await _modelFeature.SaveSessionModel(chatSession);
			await _agentPersistence.SaveSessionAgent(chatSession);

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
				return true;
			}

			_logger.LogInformation("Loading session {SessionId}", sessionId);

			IReadOnlyList<AgentProfile> globalAgents = _globalAgentFeature.Agents;
			IReadOnlyList<AgentProfile> repoAgents = await _sessionAgentFeature.Load(session.Context.GitRoot);
			session.Context.RepoAgents = repoAgents;

			List<AgentProfile> agents = [.. globalAgents, .. repoAgents];

			ResumeSessionConfig config = new()
			{
				Model = session.Model.Id,
				ReasoningEffort = session.ReasoningEffort,
				Streaming = true,
				DisableResume = true,
				OnPermissionRequest = _permissionHandler.HandlePermissionRequest,
				OnUserInputRequest = _userInputHandler.HandleUserInputRequest,
				CustomAgents = agents.Count > 0 ? [.. agents.Select(x => x.Config)] : null
			};

			session.SdkState = SdkSessionStateEnum.Loading;
			_sessionListFeature.NotifyStateChanged();

			CopilotClient client = await _clientFeature.GetClientAsync();
			CopilotSession sdkSession = await client.ResumeSessionAsync(sessionId, config);

			if(session.Context.SelectedAgent is not null)
			{
				await sdkSession.Rpc.Agent.SelectAsync(session.Context.SelectedAgent.DisplayLabel);
			}

			bool registered = false;
			try
			{
				IReadOnlyList<SessionEvent> events = await sdkSession.GetMessagesAsync();
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
				session.LastActivity = tempSession.LastActivity;
				if(session.Title != tempSession.Title)
				{
					session.Title = tempSession.Title;
				}

				session.Status = SessionStatusEnum.Idle;
				session.SdkState = SdkSessionStateEnum.Loaded;
				session.Context.WorkspacePath = sdkSession.WorkspacePath;
				SessionPermissionFeature.TryRestoreSessionCommands(session, _logger);
				await _modelFeature.TryRestoreModelSettings(session);
				await _agentPersistence.TryRestoreSessionAgentAsync(session);

				_sdkRegistry.Register(sdkSession, evt =>
				{
					_logger.LogDebug("Session {SessionId} event: {EventType}", sdkSession.SessionId, evt.Type);
					HandleSessionEvent(sdkSession.SessionId, evt);
				});
				registered = true;

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

	public async Task RestartSession(string sessionId, string newModelId, string? newReasoningEffort = null, CancellationToken cancellationToken = default)
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

			IReadOnlyList<AgentProfile> globalAgents = _globalAgentFeature.Agents;
			IReadOnlyList<AgentProfile> repoAgents = chatSession?.Context.RepoAgents ?? [];

			List<AgentProfile> agents = [.. globalAgents, .. repoAgents];

			bool hasMessages = chatSession?.Messages.Count > 0;
			if(hasMessages)
			{
				ResumeSessionConfig resumeConfig = new()
				{
					Model = newModelId,
					ReasoningEffort = newReasoningEffort,
					Streaming = true,
					OnPermissionRequest = _permissionHandler.HandlePermissionRequest,
					OnUserInputRequest = _userInputHandler.HandleUserInputRequest,
					CustomAgents = agents.Count > 0 ? [.. agents.Select(x => x.Config)] : null
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
					InfiniteSessions = new InfiniteSessionConfig
					{
						Enabled = true
					},
					WorkingDirectory = chatSession?.Context.CurrentWorkingDirectory,
					OnPermissionRequest = _permissionHandler.HandlePermissionRequest,
					OnUserInputRequest = _userInputHandler.HandleUserInputRequest,
					CustomAgents = agents.Count > 0 ? [.. agents.Select(x => x.Config)] : null
				};
				newSdkSession = await client.CreateSessionAsync(createConfig, cancellationToken);

				if(chatSession is not null)
				{
					_sdkRegistry.Remove(chatSession.Id);
					chatSession.Id = newSdkSession.SessionId;
				}
			}

			_sdkRegistry.Register(newSdkSession, evt =>
			{
				_logger.LogDebug("Session {SessionId} event: {EventType}", newSdkSession.SessionId, evt.Type);
				HandleSessionEvent(newSdkSession.SessionId, evt);
			});

			_logger.LogInformation("Restarted session {SessionId} with model {Model}", sessionId, newModelId);
		}
		catch(Exception ex)
		{
			_logger.LogError(ex, "Failed to restart session {SessionId}", sessionId);
			throw;
		}
	}

	public async Task DeleteSession(string sessionId)
	{
		try
		{
			if(_sdkRegistry.TryRemove(sessionId, out CopilotSession? sdkSession))
			{
				await sdkSession.DisposeAsync();
			}

			_terminalFeature.CloseSession(sessionId);
			_userInputHandler.CancelPendingRequestsForSession(sessionId);

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

	async Task RestartSessionWithPendingConfig(SessionModel session)
	{
		try
		{
			_logger.LogInformation(
				"Restarting session {SessionId} with model {Model} and reasoning effort {ReasoningEffort}",
				session.Id,
				session.Model.Id,
				session.ReasoningEffort ?? "default"
			);

			await RestartSession(
				session.Id,
				session.Model.Id,
				session.ReasoningEffort
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
			IReadOnlyList<SessionEvent> events = await sdkSession.GetMessagesAsync(cancellationToken);
			_logger.LogInformation("Replaying {Count} events for session {SessionId}", events.Count, session.Id);

			lock(session.SessionEventLock)
			{
				session.Messages.Clear();
				session.ActiveWorkingGroup = null;
			}
			_sessionListFeature.NotifyStateChanged();

			Func<ChatMessageModel, string, Task> streamCallback =
				(msg, text) => SessionEventHelpers.StreamSummaryTextAsync(msg, text, _sessionListFeature.NotifyStateChanged);

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
}
