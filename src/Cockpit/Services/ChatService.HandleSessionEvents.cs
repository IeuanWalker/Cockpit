using System.Diagnostics;
using Cockpit.Models;
using GitHub.Copilot.SDK;
using Microsoft.Extensions.Logging;

namespace Cockpit.Services;

public partial class ChatService
{
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
}
