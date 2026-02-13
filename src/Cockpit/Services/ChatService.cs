using Cockpit.Models;
using Cockpit.Services.Copilot;
using GitHub.Copilot.SDK;
using Microsoft.Extensions.Logging;

namespace Cockpit.Services;

public partial class ChatService
{
	readonly CopilotSessionManager _sessionManager;
	readonly ILogger<ChatService> _logger;
	readonly ContextService _contextService;
	readonly CopilotModelService _copilotModelService;

	public event Action? OnSessionsChanged;
	public event Action? OnMessagesChanged;

	public List<ChatSession> Sessions { get; private set; } = [];
	public ChatSession? CurrentSession { get; private set; }

	// Activity grouping for thinking panel (per-session)
	public ActivityGroup? ActiveThinkingGroup => CurrentSession?.ActiveThinkingGroup;
	public bool IsThinking => CurrentSession?.ActiveThinkingGroup is not null && CurrentSession.ActiveThinkingGroup.Status == GroupStatus.Running;

	public ChatService(
		CopilotSessionManager sessionManager,
		ILogger<ChatService> logger,
		ContextService contextService,
		CopilotModelService copilotModelService)
	{
		_sessionManager = sessionManager;
		_logger = logger;
		_contextService = contextService;
		_copilotModelService = copilotModelService;

		// Subscribe to session events
		_sessionManager.OnSessionEvent += HandleSessionEvent;
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
		if(lastMessage != null && !string.IsNullOrWhiteSpace(lastMessage.Message))
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

			List<SessionMetadata> sessionMetadataList = await _sessionManager.ListSessionsAsync();

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
			// TODO: Dipslay toest to user
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
				WorkingDirectory = workingDirectory
			};

			CopilotSession sdkSession = await _sessionManager.CreateSessionAsync(config);

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
			// TODO: Dipslay toest to user
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
				Streaming = true
			};

			CopilotSession sdkSession = await _sessionManager.ResumeSessionAsync(sessionId, config);

			// Load existing messages from SDK
			IReadOnlyList<SessionEvent> events = await _sessionManager.GetMessagesAsync(sessionId);
			session.Messages.Clear();
			session.StreamingMessages.Clear();
			session.ActiveThinkingGroup = null;

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
						InputParameters = ActivityGroupingService.DeserializeArguments(toolStart.Data.Arguments)
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
		catch(Exception ex)
		{
			_logger.LogError(ex, "Failed to resume session {SessionId}", sessionId);
			// TODO: Dipslay toest to user
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

		NotifyMessagesChanged();
	}

	public async Task SendMessageAsync(string content, List<UserMessageDataAttachmentsItem>? attachments = null)
	{
		if(CurrentSession is null)
		{
			return;
		}

		try
		{
			CurrentSession.Status = SessionStatus.AgentRunning;
			await _sessionManager.SendMessageAsync(CurrentSession.Id, content, attachments);
		}
		catch(Exception ex)
		{
			_logger.LogError(ex, "Failed to send message");
			// TODO: Dipslay toest to user
			CurrentSession.Status = SessionStatus.Error;
			NotifyStateChanged();
		}
	}

	public async Task DeleteSessionAsync(string sessionId)
	{
		try
		{
			await _sessionManager.DeleteSessionAsync(sessionId);

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

			// TODO: Dipslay toest to user
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
			await _sessionManager.AbortSessionAsync(CurrentSession.Id);
		}
		catch(Exception ex)
		{
			_logger.LogError(ex, "Failed to abort session");

			// TODO: Dipslay toest to user
		}
	}

	void NotifyStateChanged()
	{
		OnSessionsChanged?.Invoke();
		OnMessagesChanged?.Invoke();
	}

	void NotifyMessagesChanged() => OnMessagesChanged?.Invoke();
}

