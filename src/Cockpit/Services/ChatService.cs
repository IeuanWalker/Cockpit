using System.Diagnostics;
using Cockpit.Models;
using Cockpit.Services.Copilot;
using GitHub.Copilot.SDK;
using Microsoft.Extensions.Logging;

namespace Cockpit.Services;

public class ChatService
{
	readonly CopilotSessionManager _sessionManager;
	readonly ILogger<ChatService> _logger;
	readonly ContextService _contextService;
	readonly CopilotModelService _copilotModelService;

	public event Action? OnSessionsChanged;
	public event Action? OnMessagesChanged;
	public event Action<string>? OnError;

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

	void HandleSessionEvent(string sessionId, SessionEvent evt)
	{
		ChatSession? session = Sessions.FirstOrDefault(s => s.Id == sessionId);
		if(session is null)
		{
			return;
		}

		try
		{
			switch(evt)
			{
				case UserMessageEvent userMsg:
					HandleUserMessage(session, userMsg);
					break;

				case AssistantTurnStartEvent:
					HandleAssistantTurnStart(session);
					break;

				case AssistantMessageDeltaEvent deltaMsg:
					HandleAssistantMessageDelta(session, deltaMsg);
					break;

				case AssistantMessageEvent assistantMsg:
					HandleAssistantMessage(session, assistantMsg);
					break;

				case AssistantReasoningDeltaEvent reasoningDelta:
					HandleReasoningDelta(session, reasoningDelta);
					break;

				case AssistantReasoningEvent reasoning:
					HandleReasoning(session, reasoning);
					break;

				case ToolExecutionStartEvent toolStart:
					HandleToolStart(session, toolStart);
					break;

				case ToolExecutionCompleteEvent toolComplete:
					HandleToolComplete(session, toolComplete);
					break;

				case SessionIdleEvent:
					HandleSessionIdle(session);
					break;

				case SessionErrorEvent error:
					HandleSessionError(session, error);
					break;

				case SessionCompactionStartEvent:
					_logger.LogInformation("Session {SessionId} started context compaction", sessionId);
					break;

				case SessionCompactionCompleteEvent compaction:
					_logger.LogInformation("Session {SessionId} completed compaction: {TokensRemoved} tokens removed",
						sessionId, compaction.Data?.TokensRemoved);
					break;

				default:
					Debug.WriteLine($"UNHANDLED EVENT TYPE: {evt.GetType().Name} - {evt.Type}");
					break;
			}
		}
		catch(Exception ex)
		{
			_logger.LogError(ex, "Error handling session event {EventType} for session {SessionId}",
				evt.Type, sessionId);
		}
	}

	void HandleUserMessage(ChatSession session, UserMessageEvent evt)
	{
		Debug.WriteLine("HandleUserMessage");
		Debug.WriteLine(evt);

		if(evt.Data is null)
		{
			return;
		}

		ChatMessage message = new()
		{
			Id = Guid.NewGuid().ToString(),
			Content = evt.Data.Content ?? string.Empty,
			IsUser = true,
			Timestamp = DateTime.Now,
			Type = MessageType.Text,
			EventType = evt.Type
		};

		session.Messages.Add(message);
		session.LastActivity = DateTime.Now;
		session.Status = SessionStatus.AgentRunning;
		NotifyStateChanged();
	}

	void HandleAssistantMessageDelta(ChatSession session, AssistantMessageDeltaEvent evt)
	{
		Debug.WriteLine("HandleAssistantMessageDelta");
		Debug.WriteLine(evt);

		if(evt.Data is null)
		{
			return;
		}

		string messageId = evt.Data.MessageId ?? "streaming";

		// Don't add to chat if we have an active thinking group
		if(session.ActiveThinkingGroup is not null && session.ActiveThinkingGroup.Status == GroupStatus.Running)
		{
			// Just update the streaming message tracker, don't add to chat
			if(!session.StreamingMessages.TryGetValue(messageId, out ChatMessage? message))
			{
				message = new ChatMessage
				{
					Id = messageId,
					Content = string.Empty,
					IsUser = false,
					Timestamp = DateTime.Now,
					Type = MessageType.Text,
					IsStreaming = true,
					IsComplete = false,
					EventType = evt.Type
				};
				session.StreamingMessages[messageId] = message;
				// DON'T add to session.Messages - it will go in thinking panel
			}

			message.Content += evt.Data.DeltaContent ?? string.Empty;
			session.LastActivity = DateTime.Now;
			NotifyStateChanged();
			return;
		}

		// Not in thinking mode - add to chat normally
		if(!session.StreamingMessages.TryGetValue(messageId, out ChatMessage? msg))
		{
			msg = new ChatMessage
			{
				Id = messageId,
				Content = string.Empty,
				IsUser = false,
				Timestamp = DateTime.Now,
				Type = MessageType.Text,
				IsStreaming = true,
				IsComplete = false,
				EventType = evt.Type
			};
			session.StreamingMessages[messageId] = msg;
			session.Messages.Add(msg);
		}

		msg.Content += evt.Data.DeltaContent ?? string.Empty;
		session.LastActivity = DateTime.Now;
		NotifyStateChanged();
	}

	void HandleAssistantMessage(ChatSession session, AssistantMessageEvent evt)
	{
		Debug.WriteLine("HandleAssistantMessage");
		Debug.WriteLine(evt);

		if(evt.Data is null)
		{
			return;
		}

		string messageId = evt.Data.MessageId ?? Guid.NewGuid().ToString();
		string content = evt.Data.Content ?? string.Empty;

		// Check if this is a streaming message that was already added to chat
		bool isStreamingMessage = session.StreamingMessages.TryGetValue(messageId, out ChatMessage? streamingMsg);
		bool isInChat = session.Messages.Any(m => m.Id == messageId);

		// Check if we have an active thinking group
		if(session.ActiveThinkingGroup is not null && session.ActiveThinkingGroup.Status == GroupStatus.Running)
		{
			// If this message is already in chat, it's the initial message - keep it there
			if(isInChat)
			{
				// This is the initial message - update it in chat and track it
				if(streamingMsg is not null)
				{
					streamingMsg.Content = content;
					streamingMsg.IsStreaming = false;
					streamingMsg.IsComplete = true;
					session.StreamingMessages.Remove(messageId);
				}

				// Track this as the initial message
				if(session.ActiveThinkingGroup.InitialMessageId is null)
				{
					session.ActiveThinkingGroup.InitialMessageId = messageId;
				}

				session.LastActivity = DateTime.Now;
				NotifyStateChanged();
				return;
			}

			// All messages during thinking go to the thinking panel
			// The last one will be extracted as the summary when SessionIdle fires
			if(!string.IsNullOrWhiteSpace(content))
			{
				session.ActiveThinkingGroup.AddEvent(new ThinkingEvent
				{
					Id = messageId,
					Type = ThinkingEventType.Message,
					Message = content,
					Timestamp = DateTime.Now
				});
				Debug.WriteLine("Added intermediate message to thinking group");
			}

			// Clean up streaming tracker
			if(streamingMsg is not null)
			{
				session.StreamingMessages.Remove(messageId);
			}

			session.LastActivity = DateTime.Now;
			NotifyStateChanged();
			return;
		}

		// If this was a streaming message, update it
		if(streamingMsg is not null)
		{
			streamingMsg.Content = content;
			streamingMsg.IsStreaming = false;
			streamingMsg.IsComplete = true;
			session.StreamingMessages.Remove(messageId);
		}
		else if(!string.IsNullOrWhiteSpace(content))
		{
			ChatMessage message = new()
			{
				Id = messageId,
				Content = content,
				IsUser = false,
				Timestamp = DateTime.Now,
				Type = MessageType.Text,
				IsComplete = true,
				EventType = evt.Type
			};
			session.Messages.Add(message);
		}

		session.LastActivity = DateTime.Now;
		NotifyStateChanged();
	}

	void HandleReasoningDelta(ChatSession session, AssistantReasoningDeltaEvent evt)
	{
		Debug.WriteLine("HandleReasoningDelta");
		Debug.WriteLine(evt);

		if(evt.Data is null)
		{
			return;
		}

		// Reasoning is only shown in the thinking panel, not in chat
		// Just update activity timestamp
		session.LastActivity = DateTime.Now;
		NotifyStateChanged();
	}

	void HandleReasoning(ChatSession session, AssistantReasoningEvent evt)
	{
		Debug.WriteLine("HandleReasoning");
		Debug.WriteLine(evt);

		if(evt.Data is null)
		{
			return;
		}

		string messageId = "reasoning";

		if(session.StreamingMessages.TryGetValue(messageId, out ChatMessage? existingMessage))
		{
			existingMessage.ReasoningContent = evt.Data.Content ?? string.Empty;
			existingMessage.IsStreaming = false;
			existingMessage.IsComplete = true;
			session.StreamingMessages.Remove(messageId);
		}

		session.LastActivity = DateTime.Now;
		NotifyStateChanged();
	}

	void HandleAssistantTurnStart(ChatSession session)
	{
		Debug.WriteLine("HandleAssistantTurnStart");

		session.Status = SessionStatus.AgentRunning;
		NotifyStateChanged();
	}

	void HandleToolStart(ChatSession session, ToolExecutionStartEvent evt)
	{
		Debug.WriteLine("HandleToolStart");
		Debug.WriteLine(evt);

		if(evt.Data is null)
		{
			return;
		}

		// Ensure we have an active thinking group
		if(session.ActiveThinkingGroup is null)
		{
			session.ActiveThinkingGroup = new ActivityGroup
			{
				StartTime = DateTime.Now,
				Status = GroupStatus.Running,
				IsExpanded = true
			};

			// Find the most recent assistant message AFTER the last user message (current turn only)
			int lastUserIndex = -1;
			for(int i = session.Messages.Count - 1; i >= 0; i--)
			{
				if(session.Messages[i].IsUser)
				{
					lastUserIndex = i;
					break;
				}
			}

			ChatMessage? lastAssistantMessage = null;
			if(lastUserIndex >= 0)
			{
				// Look for assistant messages after the last user message
				lastAssistantMessage = session.Messages
					.Skip(lastUserIndex + 1)
					.Where(m => !m.IsUser && m.Type == MessageType.Text)
					.LastOrDefault();
			}

			if(lastAssistantMessage is not null)
			{
				session.ActiveThinkingGroup.InitialMessageId = lastAssistantMessage.Id;
				Debug.WriteLine($"Tracked initial message: {lastAssistantMessage.Id}");
			}
		}

		// Create tool execution with full details
		ToolExecution toolExec = new()
		{
			ToolName = evt.Data.ToolName ?? "unknown",
			ToolCallId = evt.Data.ToolCallId,
			InputParameters = ActivityGroupingService.DeserializeArguments(evt.Data.Arguments),
			StartTime = DateTime.Now,
			Status = ToolStatus.Running
		};

		// Add as a thinking event (chronologically ordered with messages)
		session.ActiveThinkingGroup.AddEvent(new ThinkingEvent
		{
			Type = ThinkingEventType.Tool,
			Tool = toolExec,
			Timestamp = DateTime.Now
		});

		session.LastActivity = DateTime.Now;
		NotifyStateChanged();
	}

	void HandleToolComplete(ChatSession session, ToolExecutionCompleteEvent evt)
	{
		Debug.WriteLine("HandleToolComplete");
		Debug.WriteLine(evt);

		if(evt.Data is null || session.ActiveThinkingGroup is null)
		{
			return;
		}

		// Find the tool execution in the active group (thread-safe)
		List<ThinkingEvent> events = session.ActiveThinkingGroup.GetEventsSnapshot();
		ToolExecution? toolExec = events
			.Where(e => e.Type == ThinkingEventType.Tool && e.Tool != null)
			.Select(e => e.Tool!)
			.FirstOrDefault(t => t.ToolCallId == evt.Data.ToolCallId);

		if(toolExec != null)
		{
			toolExec.Status = ToolStatus.Success;
			toolExec.IsSuccess = true;
			toolExec.EndTime = DateTime.Now;
			toolExec.Output = evt.Data.Result?.ToString();
		}

		session.LastActivity = DateTime.Now;
		NotifyStateChanged();
	}

	void HandleSessionIdle(ChatSession session)
	{
		Debug.WriteLine("HandleSessionIdle - Finalizing activity group");

		if(session.ActiveThinkingGroup is not null && session.ActiveThinkingGroup.Tools.Any())
		{
			ActivityGroup group = session.ActiveThinkingGroup;
			Debug.WriteLine($"Finalizing thinking group. Has {group.Tools.Count()} tools");

			// Check if activity message already exists for this group
			bool activityMessageExists = session.Messages.Any(m =>
				m.Type == MessageType.ActivityGroup && m.ActivityGroup?.Id == group.Id);

			if(activityMessageExists)
			{
				Debug.WriteLine($"Activity message already exists for group {group.Id}, skipping insertion");
				session.ActiveThinkingGroup = null;
				NotifyStateChanged();
				return;
			}

			// Mark any still-running tools as stopped (Error status)
			bool hasStoppedTools = false;
			foreach(ToolExecution tool in group.Tools)
			{
				if(tool.Status == ToolStatus.Running)
				{
					tool.Status = ToolStatus.Error;
					tool.EndTime = DateTime.Now;
					tool.IsSuccess = false;
					hasStoppedTools = true;
				}
			}

			group.Status = GroupStatus.Complete;
			group.EndTime = DateTime.Now;
			group.IsExpanded = false;

			// Extract the last message event as the summary (but not "Session stopped")
			List<ThinkingEvent> events = group.GetEventsSnapshot();
			ThinkingEvent? lastMessage = events.LastOrDefault(e => e.Type == ThinkingEventType.Message);

			bool hasSummary = false;
			if(lastMessage is not null && !string.IsNullOrWhiteSpace(lastMessage.Message))
			{
				// Remove the summary from thinking events
				group.RemoveEvent(lastMessage);

				// Add summary message as streaming - will be progressively revealed
				ChatMessage summaryMsg = new()
				{
					Id = lastMessage.Id ?? Guid.NewGuid().ToString(),
					Content = string.Empty,
					IsUser = false,
					Timestamp = DateTime.Now,
					Type = MessageType.Text,
					IsStreaming = true,
					IsComplete = false
				};
				session.Messages.Add(summaryMsg);
				Debug.WriteLine($"Added summary message to chat: {summaryMsg.Id}");

				// Stream the summary text progressively
				_ = StreamSummaryTextAsync(session, summaryMsg, lastMessage.Message);
				hasSummary = true;
			}

			// Add "Session stopped" event to operation list AFTER extracting summary
			if(hasStoppedTools)
			{
				group.AddEvent(new ThinkingEvent
				{
					Type = ThinkingEventType.Message,
					Message = "Session stopped",
					Timestamp = DateTime.Now
				});
			}

			// Insert activity group between initial and summary messages
			ChatMessage activityMessage = new()
			{
				IsUser = false,
				Type = MessageType.ActivityGroup,
				ActivityGroup = group,
				Timestamp = group.EndTime ?? DateTime.Now,
				Content = GenerateActivitySummary(group)
			};

			if(!string.IsNullOrEmpty(group.InitialMessageId))
			{
				int initialIndex = session.Messages.FindIndex(m => m.Id == group.InitialMessageId);
				if(initialIndex >= 0)
				{
					session.Messages.Insert(initialIndex + 1, activityMessage);
					Debug.WriteLine($"Inserted activity group at index {initialIndex + 1}");
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
					Debug.WriteLine($"WARNING: Attempted to insert activity group at index 0, moved to end");
				}

				session.Messages.Insert(insertIndex, activityMessage);
				Debug.WriteLine($"Inserted activity group at index {insertIndex} (lastUserIndex={lastUserIndex}, hasSummary={hasSummary}, Count={session.Messages.Count})");
			}

			// Clear the thinking group
			session.ActiveThinkingGroup = null;
		}
		else if(session.ActiveThinkingGroup is not null)
		{
			Debug.WriteLine("Clearing empty thinking group");
			session.ActiveThinkingGroup = null;
		}
		else
		{
			Debug.WriteLine("No active thinking group to finalize");
		}

		session.Status = SessionStatus.Idle;
		NotifyStateChanged();
	}

	async Task StreamSummaryTextAsync(ChatSession _, ChatMessage message, string fullText)
	{
		const int chunkSize = 3; // Characters per tick
		const int delayMs = 8; // Milliseconds between chunks

		for(int i = 0; i < fullText.Length; i += chunkSize)
		{
			int end = Math.Min(i + chunkSize, fullText.Length);
			message.Content = fullText[..end];
			NotifyMessagesChanged();
			await Task.Delay(delayMs);
		}

		message.Content = fullText;
		message.IsStreaming = false;
		message.IsComplete = true;
		NotifyMessagesChanged();
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

	// Finalize an activity group built during history replay and insert it into the message list
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

	void HandleSessionError(ChatSession session, SessionErrorEvent evt)
	{
		Debug.WriteLine("HandleSessionError");
		Debug.WriteLine(evt);

		if(evt.Data is null)
		{
			return;
		}

		ChatMessage message = new()
		{
			Id = Guid.NewGuid().ToString(),
			Content = evt.Data.Message ?? "An error occurred",
			IsUser = false,
			Timestamp = DateTime.Now,
			Type = MessageType.Error,
			EventType = evt.Type
		};

		session.Messages.Add(message);
		session.Status = SessionStatus.Error;
		session.LastActivity = DateTime.Now;

		OnError?.Invoke(evt.Data.Message ?? "Unknown error");
		NotifyStateChanged();
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
			OnError?.Invoke($"Failed to load existing sessions: {ex.Message}");
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
			OnError?.Invoke($"Failed to create session: {ex.Message}");
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
			OnError?.Invoke($"Failed to resume session: {ex.Message}");
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
			OnError?.Invoke($"Failed to send message: {ex.Message}");
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
			OnError?.Invoke($"Failed to delete session: {ex.Message}");
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
			// Don't call HandleSessionIdle here - the SDK will send SessionIdleEvent which will call it
		}
		catch(Exception ex)
		{
			_logger.LogError(ex, "Failed to abort session");
			OnError?.Invoke($"Failed to abort: {ex.Message}");
		}
	}

	void NotifyStateChanged()
	{
		OnSessionsChanged?.Invoke();
		OnMessagesChanged?.Invoke();
	}

	void NotifyMessagesChanged() => OnMessagesChanged?.Invoke();
}

