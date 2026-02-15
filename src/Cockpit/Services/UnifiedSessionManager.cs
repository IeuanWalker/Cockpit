using System.Collections.Concurrent;
using System.Text.Json;
using Blazor.Sonner.Services;
using Cockpit.Models;
using GitHub.Copilot.SDK;
using Microsoft.Extensions.Logging;

namespace Cockpit.Services;

public partial class UnifiedSessionManager
{
	readonly CopilotClientService _clientService;
	readonly ILogger<UnifiedSessionManager> _logger;
	readonly ToastService _toastService;
	readonly ContextService _contextService;
	readonly CopilotModelService _copilotModelService;
	readonly PermissionService _permissionService;
	readonly TerminalService _terminalService;

	// Internal: Maps sessionId -> SDK CopilotSession (for ALL resumed sessions)
	readonly ConcurrentDictionary<string, CopilotSession> _sdkSessions = new();

	public event Action? OnStateChanged;

	// All sessions (persisted + in-memory)
	public List<ChatSession> Sessions { get; private set; } = [];

	// The ONE session currently visible in UI
	public ChatSession? CurrentSession { get; private set; }

	// Activity grouping for working panel (per-session)
	public ActivityGroup? ActiveWorkingGroup => CurrentSession?.ActiveWorkingGroup;
	public bool IsWorking => CurrentSession?.ActiveWorkingGroup is not null && CurrentSession.ActiveWorkingGroup.Status == GroupStatus.Running;

	public UnifiedSessionManager(
		CopilotClientService clientService,
		ILogger<UnifiedSessionManager> logger,
		ToastService toastService,
		ContextService contextService,
		CopilotModelService copilotModelService,
		PermissionService permissionService,
		TerminalService terminalService)
	{
		_clientService = clientService;
		_logger = logger;
		_toastService = toastService;
		_contextService = contextService;
		_copilotModelService = copilotModelService;
		_permissionService = permissionService;
		_terminalService = terminalService;

		// Subscribe to permission events
		_permissionService.OnPermissionRequested += HandlePermissionRequested;
		_permissionService.OnPermissionResolved += HandlePermissionResolved;
	}

	// Deserialize JsonElement arguments to Dictionary<string, object>
	static Dictionary<string, object>? DeserializeArguments(object? arguments)
	{
		if(arguments is null)
		{
			return null;
		}

		try
		{
			// If it's already a dictionary, return it
			if(arguments is Dictionary<string, object> dict)
			{
				return dict;
			}

			// If it's a JsonElement, deserialize it
			if(arguments is JsonElement je)
			{
				if(je.ValueKind == JsonValueKind.Object)
				{
					return JsonSerializer.Deserialize<Dictionary<string, object>>(je.GetRawText());
				}
			}
		}
		catch
		{
			// Fall through to return null
		}

		return null;
	}

	string GenerateActivitySummary(ActivityGroup group)
	{
		List<ToolExecution> tools = [.. group.Tools]; // Create snapshot
		int running = tools.Count(t => t.Status == ToolStatus.Running);
		int complete = tools.Count(t => t.Status == ToolStatus.Success);
		IEnumerable<string> toolNames = tools.Select(t => t.ToolName).Distinct().Take(3);
		int more = tools.Select(t => t.ToolName).Distinct().Count() - 3;
		string preview = string.Join(", ", toolNames) + (more > 0 ? $", +{more}" : "");

		return $"{tools.Count} operations ({preview})";
	}

	void FinalizeHistoryGroup(ChatSession session, ref ActivityGroup? group, ref ChatMessage? initialMessage)
	{
		if(group is null || !group.Tools.Any())
		{
			group = null;
			return;
		}

		ActivityGroup g = group; // Capture for use in lambdas
		g.Status = GroupStatus.Complete;
		g.EndTime = g.GetEventsSnapshot().LastOrDefault()?.Timestamp ?? g.StartTime;
		g.IsExpanded = false;

		// Mark any still-running tools as stopped (Error status) - handles incomplete resumed sessions
		bool hasStoppedTools = false;
		foreach(ToolExecution tool in g.Tools)
		{
			if(tool.Status == ToolStatus.Running)
			{
				tool.Status = ToolStatus.Error;
				tool.EndTime = g.EndTime;
				tool.IsSuccess = false;
				hasStoppedTools = true;
			}
		}

		// Extract the last message as the summary (same logic as HandleSessionIdle)
		List<ThinkingEvent> events = g.GetEventsSnapshot();
		ThinkingEvent? lastMessage = events.LastOrDefault(e => e.Type == ThinkingEventType.Message);

		bool hasSummary = false;
		if(lastMessage is not null && !string.IsNullOrWhiteSpace(lastMessage.Message))
		{
			g.RemoveEvent(lastMessage);
			session.Messages.Add(new ChatMessage
			{
				Id = lastMessage.Id ?? Guid.NewGuid().ToString(),
				Content = lastMessage.Message,
				IsUser = false,
				Timestamp = lastMessage.Timestamp,
				Type = MessageType.Text,
				IsComplete = true
			});
			hasSummary = true;
		}

		// Add "Session stopped" event for resumed sessions that had stopped tools
		if(hasStoppedTools)
		{
			g.AddEvent(new ThinkingEvent
			{
				Type = ThinkingEventType.Message,
				Message = "Session stopped",
				Timestamp = g.EndTime ?? DateTime.Now
			});
		}

		// Insert activity group between initial and summary
		ChatMessage activityMessage = new()
		{
			IsUser = false,
			Type = MessageType.ActivityGroup,
			ActivityGroup = g,
			Timestamp = g.EndTime ?? DateTime.Now,
			Content = GenerateActivitySummary(g)
		};

		if(!string.IsNullOrEmpty(g.InitialMessageId))
		{
			int initialIndex = session.Messages.FindIndex(m => m.Id == g.InitialMessageId);
			if(initialIndex >= 0)
			{
				session.Messages.Insert(initialIndex + 1, activityMessage);
			}
			else
			{
				// Insert before the summary if it exists, otherwise at the end
				int insertIndex = hasSummary ? Math.Max(0, session.Messages.Count - 1) : session.Messages.Count;
				session.Messages.Insert(insertIndex, activityMessage);
			}
		}
		else
		{
			// No initial assistant message - insert after the last user message
			int lastUserIndex = -1;
			int searchLimit = hasSummary ? session.Messages.Count - 2 : session.Messages.Count - 1;
			for(int i = searchLimit; i >= 0; i--)
			{
				if(session.Messages[i].IsUser)
				{
					lastUserIndex = i;
					break;
				}
			}

			int insertIndex;
			if(lastUserIndex >= 0)
			{
				// Insert right after the user message
				insertIndex = lastUserIndex + 1;
			}
			else
			{
				// Fallback: No user message found - insert before summary if exists, otherwise at end
				insertIndex = hasSummary ? Math.Max(0, session.Messages.Count - 1) : session.Messages.Count;
			}

			// Ensure we never insert at index 0 if there are messages
			if(insertIndex == 0 && session.Messages.Count > 0)
			{
				insertIndex = session.Messages.Count;
			}

			session.Messages.Insert(insertIndex, activityMessage);
		}

		group = null;
		initialMessage = null;
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
				OnPermissionRequest = HandlePermissionRequest
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
				Model = defaultModel,
				ReasoningEffort = defaultModel.DefaultReasoningEffort,
				IsResumed = true
			};

			Sessions.Insert(0, chatSession);
			CurrentSession = chatSession;

			// Update the context service with the working directory
			if(!string.IsNullOrEmpty(workingDirectory))
			{
				_contextService.SetDirectory(workingDirectory);
			}

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
				OnPermissionRequest = HandlePermissionRequest
			};

			CopilotClient client = await _clientService.GetClientAsync();
			CopilotSession sdkSession = await client.ResumeSessionAsync(sessionId, config);

			// Subscribe to session events (will process in background)
			sdkSession.On(evt =>
			{
				_logger.LogDebug("Session {SessionId} event: {EventType}", sdkSession.SessionId, evt.Type);
				HandleSessionEvent(sdkSession.SessionId, evt);
			});

			// Add to SDK sessions dictionary
			_sdkSessions.AddOrUpdate(sdkSession.SessionId, sdkSession, (_, _) => sdkSession);

			// Load existing messages from SDK
			IReadOnlyList<SessionEvent> events = await sdkSession.GetMessagesAsync();
			session.Messages.Clear();
			session.StreamingMessages.Clear();
			session.ActiveWorkingGroup = null;

			_logger.LogInformation("Loading {Count} events for session {SessionId}", events.Count, sessionId);

			// Reconstruct messages with activity groups from event history
			ActivityGroup? currentGroup = null;
			ChatMessage? initialMessage = null;

			foreach(SessionEvent evt in events)
			{
				if(evt is UserMessageEvent userMsg && userMsg.Data is not null)
				{
					// Finalize any pending activity group before user message
					FinalizeHistoryGroup(session, ref currentGroup, ref initialMessage);

					// Reset initial message tracking for the new turn
					initialMessage = null;

					session.Messages.Add(new ChatMessage
					{
						Id = Guid.NewGuid().ToString(),
						Content = userMsg.Data.Content ?? string.Empty,
						IsUser = true,
						Timestamp = userMsg.Timestamp,
						Type = MessageType.Text
					});
				}
				else if(evt is AssistantMessageEvent assistantMsg && assistantMsg.Data is not null)
				{
					string content = assistantMsg.Data.Content ?? string.Empty;
					if(string.IsNullOrWhiteSpace(content))
					{
						continue;
					}

					if(currentGroup is not null)
					{
						// During an active group, messages go to thinking events
						currentGroup.AddEvent(new ThinkingEvent
						{
							Id = assistantMsg.Data.MessageId,
							Type = ThinkingEventType.Message,
							Message = content,
							Timestamp = assistantMsg.Timestamp.LocalDateTime
						});
					}
					else
					{
						// No active group - this could be the initial message before tools
						ChatMessage msg = new()
						{
							Id = assistantMsg.Data.MessageId ?? Guid.NewGuid().ToString(),
							Content = content,
							IsUser = false,
							Timestamp = assistantMsg.Timestamp,
							Type = MessageType.Text,
						};
						session.Messages.Add(msg);
						initialMessage = msg;
					}
				}
				else if(evt is ToolExecutionStartEvent toolStart && toolStart.Data != null)
				{
					// Create group on first tool
					currentGroup ??= new ActivityGroup
					{
						StartTime = toolStart.Timestamp.LocalDateTime,
						Status = GroupStatus.Complete,
						IsExpanded = false,
						InitialMessageId = initialMessage?.Id,
					};

					ToolExecution toolExec = new()
					{
						ToolName = toolStart.Data.ToolName ?? "unknown",
						ToolCallId = toolStart.Data.ToolCallId,
						StartTime = toolStart.Timestamp.LocalDateTime,
						Status = ToolStatus.Running,
						InputParameters = DeserializeArguments(toolStart.Data.Arguments)
					};

					currentGroup.AddEvent(new ThinkingEvent
					{
						Type = ThinkingEventType.Tool,
						Tool = toolExec,
						Timestamp = toolStart.Timestamp.LocalDateTime
					});
				}
				else if(evt is ToolExecutionCompleteEvent toolComplete && toolComplete.Data != null)
				{
					if(currentGroup != null)
					{
						List<ThinkingEvent> toolEvents = currentGroup.GetEventsSnapshot();
						ToolExecution? tool = toolEvents
							.Where(e => e.Type == ThinkingEventType.Tool && e.Tool != null)
							.Select(e => e.Tool!)
							.FirstOrDefault(t => t.ToolCallId == toolComplete.Data.ToolCallId);
						if(tool != null)
						{
							tool.Status = ToolStatus.Success;
							tool.EndTime = toolComplete.Timestamp.LocalDateTime;
							tool.Output = toolComplete.Data.Result?.ToString();
						}
					}
				}
			}

			// Finalize any remaining group at the end
			FinalizeHistoryGroup(session, ref currentGroup, ref initialMessage);

			session.Status = SessionStatus.Idle;
			session.IsResumed = true;
			session.WorkspacePath = sdkSession.WorkspacePath;
			CurrentSession = session;

			// Update context service with working directory
			if(!string.IsNullOrEmpty(session.WorkingDirectory))
			{
				_contextService.SetDirectory(session.WorkingDirectory);
			}
			else if(!string.IsNullOrEmpty(session.WorkspacePath))
			{
				_contextService.SetDirectory(session.WorkspacePath);
			}

			NotifyStateChanged();
			_logger.LogInformation("Successfully resumed session {SessionId} with {MessageCount} messages", sessionId, session.Messages.Count);
			return true;
		}
		catch(Exception ex) when(ex.Message.Equals("Communication error with Copilot CLI: Request session.resume failed with message: Session file is corrupted or incompatible"))
		{
			_logger.LogError(ex, "Session {SessionId} is corrupted or incompatible", sessionId);
			_toastService.Error("Session Unavailable", opts =>
			{
				opts.Description = "The session file may be corrupted, incompatible, or in use by another instance. You may need to delete or exit the session running else where";
			});
			return false;
		}
		catch(Exception ex)
		{
			_logger.LogError(ex, "Failed to resume session {SessionId}", sessionId);
			return false;
		}
	}

	public void SetCurrentSession(ChatSession session)
	{
		CurrentSession = session;

		// Update context service when switching sessions
		if(!string.IsNullOrEmpty(session.WorkingDirectory))
		{
			_contextService.SetDirectory(session.WorkingDirectory);
		}
		else if(!string.IsNullOrEmpty(session.WorkspacePath))
		{
			// Fallback to workspace path if no working directory is set
			_contextService.SetDirectory(session.WorkspacePath);
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

			// 2. Destroy the in-memory session object
			await existingSession.DisposeAsync();
			_logger.LogInformation("Destroyed session {SessionId} for restart", sessionId);

			// 3. Create ResumeSessionConfig with new model/reasoning
			ResumeSessionConfig resumeConfig = new()
			{
				Model = newModelId,
				ReasoningEffort = newReasoningEffort,
				Streaming = true,
				OnPermissionRequest = HandlePermissionRequest
			};

			// 4. Resume session with same ID but new config
			CopilotClient client = await _clientService.GetClientAsync(cancellationToken);
			CopilotSession resumedSession = await client.ResumeSessionAsync(sessionId, resumeConfig, cancellationToken);

			// 5. Re-subscribe to session events
			resumedSession.On(evt =>
			{
				_logger.LogDebug("Session {SessionId} event: {EventType}", resumedSession.SessionId, evt.Type);
				HandleSessionEvent(resumedSession.SessionId, evt);
			});

			// 6. Update dictionary with resumed session
			_sdkSessions.TryAdd(resumedSession.SessionId, resumedSession);

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
			_terminalService.CloseSession(sessionId);

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

	void NotifyStateChanged() => OnStateChanged?.Invoke();

	// Handle permission requested event from PermissionService
	void HandlePermissionRequested(string sessionId, Models.PermissionRequest request)
	{
		ChatSession? session = Sessions.FirstOrDefault(s => s.Id == sessionId);
		if(session is null)
		{
			return;
		}

		_logger.LogInformation("HandlePermissionRequested - Adding request ID: {RequestId} to session {SessionId}", request.Id, sessionId);

		// Add to pending requests collection
		session.PendingPermissionRequests.Add(request);

		// Set status to NeedsPermission on first request
		if(session.PendingPermissionRequests.Count == 1)
		{
			session.PreviousStatus = session.Status;
			session.Status = SessionStatus.NeedsPermission;
		}

		// Notify UI
		NotifyStateChanged();
	}

	// Handle permission resolved event from PermissionService
	void HandlePermissionResolved(string sessionId, string requestId, PermissionDecision decision)
	{
		ChatSession? session = Sessions.FirstOrDefault(s => s.Id == sessionId);
		if(session is null)
		{
			return;
		}

		_logger.LogInformation("HandlePermissionResolved - Removing request ID: {RequestId} from session {SessionId}", requestId, sessionId);

		// Remove the specific resolved request from the collection
		// ConcurrentBag doesn't support direct removal, so we need to recreate it
		List<Models.PermissionRequest> remaining = session.PendingPermissionRequests
			.Where(r => r.Id != requestId)
			.ToList();
		session.PendingPermissionRequests = new ConcurrentBag<Models.PermissionRequest>(remaining);

		// Restore previous status only when all permissions are resolved
		if(session.PendingPermissionRequests.Count == 0)
		{
			session.Status = session.PreviousStatus ?? SessionStatus.Idle;
		}

		// Notify UI
		NotifyStateChanged();
	}

	// Permission handler for SDK tool execution requests
	async Task<PermissionRequestResult> HandlePermissionRequest(GitHub.Copilot.SDK.PermissionRequest request, PermissionInvocation invocation)
	{
		try
		{
			// Get the session this permission request is for
			if(!_sdkSessions.TryGetValue(invocation.SessionId, out CopilotSession? sdkSession))
			{
				_logger.LogWarning("Permission request for unknown session {SessionId}", invocation.SessionId);
				return new PermissionRequestResult { Kind = "denied" };
			}

			ChatSession? session = Sessions.FirstOrDefault(s => s.Id == invocation.SessionId);
			if(session is null)
			{
				_logger.LogWarning("ChatSession not found for SDK session {SessionId}", invocation.SessionId);
				return new PermissionRequestResult { Kind = "denied" };
			}

			// Extract tool information from the permission request
			// The SDK sends different "kinds" of permission requests (e.g., "write", "execute", etc.)
			string toolName = request.Kind; // e.g., "write", "execute", etc.
			string command = ExtractCommandFromRequest(request);

			// Pass ExtensionData as arguments for detailed message generation
			Dictionary<string, object>? arguments = request.ExtensionData != null
				? new Dictionary<string, object>(request.ExtensionData)
				: null;


			_logger.LogInformation("Permission request: Kind={Kind}, Command={Command}, SessionId={SessionId}",
				toolName, command, session.Id);

			// Check permission through our service
			PermissionDecision decision = await _permissionService.CheckPermissionAsync(
				session.Id,
				toolName,
				command,
				arguments: arguments,
				kind: request.Kind,
				isYolo: session.IsYolo);

			// Convert our decision to SDK format
			string resultKind = decision.IsApproved ? "approved" : "denied-interactively-by-user";

			_logger.LogInformation("Permission decision: {Decision} for {Command}", resultKind, command);

			return new PermissionRequestResult { Kind = resultKind };
		}
		catch(Exception ex)
		{
			_logger.LogError(ex, "Error in permission handler");
			return new PermissionRequestResult { Kind = "denied" };
		}
	}

	// Extract command string from SDK permission request
	static string ExtractCommandFromRequest(GitHub.Copilot.SDK.PermissionRequest request)
	{
		// The SDK puts additional context in ExtensionData
		if(request.ExtensionData is null)
		{
			return request.Kind;
		}

		// Try to extract command from various possible fields
		if(request.ExtensionData.TryGetValue("command", out object? cmdObj))
		{
			string cmdStr = cmdObj?.ToString() ?? "";

			// If it's a JSON object with fullCommandText, extract that
			if(cmdStr.StartsWith('{') && cmdStr.Contains("fullCommandText"))
			{
				try
				{
					using JsonDocument doc = JsonDocument.Parse(cmdStr);
					if(doc.RootElement.TryGetProperty("fullCommandText", out JsonElement fullCmd))
					{
						return fullCmd.GetString() ?? request.Kind;
					}
				}
				catch
				{
					// Fall through to return as-is
				}
			}

			return cmdStr;
		}

		if(request.ExtensionData.TryGetValue("path", out object? pathObj))
		{
			return pathObj?.ToString() ?? request.Kind;
		}

		if(request.ExtensionData.TryGetValue("args", out object? argsObj))
		{
			return argsObj?.ToString() ?? request.Kind;
		}

		// Fallback: return kind
		return request.Kind;
	}
}
