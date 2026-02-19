using System.Diagnostics;
using System.Text.Json;
using Cockpit.Models;
using GitHub.Copilot.SDK;
using Microsoft.Extensions.Logging;

namespace Cockpit.Services;

public partial class UnifiedSessionManager
{
	/// <summary>
	/// Handles session events for ALL resumed sessions (not just CurrentSession).
	/// Events are processed regardless of which session is currently visible.
	/// UI notifications are only triggered for CurrentSession to avoid unnecessary re-renders.
	/// </summary>
	void HandleSessionEvent(string sessionId, SessionEvent evt)
	{
		ChatSession? session = Sessions.FirstOrDefault(s => s.Id == sessionId);
		if(session is null)
		{
			return;
		}
		HandleSessionEventCore(session, evt);
	}

	void HandleSessionEventCore(ChatSession session, SessionEvent evt)
	{

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

				case SessionIdleEvent idleEvt:
					HandleSessionIdle(session, idleEvt.Timestamp);
					break;

				case SessionErrorEvent error:
					HandleSessionError(session, error);
					break;

				case SessionTitleChangedEvent titleChanged:
					HandleSessionTitleChanged(session, titleChanged);
					break;

				case AbortEvent abort:
					HandleAbort(session, abort);
					break;

				case SessionShutdownEvent shutdown:
					HandleSessionShutdown(session, shutdown);
					break;

				case SessionWarningEvent warning:
					HandleSessionWarning(session, warning);
					break;

				case ToolExecutionProgressEvent toolProgress:
					HandleToolProgress(session, toolProgress);
					break;

				case ToolExecutionPartialResultEvent toolPartial:
					HandleToolPartialResult(session, toolPartial);
					break;

				case SubagentStartedEvent subagentStarted:
					HandleSubagentStarted(session, subagentStarted);
					break;

				case SubagentCompletedEvent subagentCompleted:
					HandleSubagentCompleted(session, subagentCompleted);
					break;

				case SubagentFailedEvent subagentFailed:
					HandleSubagentFailed(session, subagentFailed);
					break;

				case SessionCompactionStartEvent:
					_logger.LogInformation("Session {SessionId} started context compaction", session.Id);
					break;

				case SessionCompactionCompleteEvent compaction:
					_logger.LogInformation("Session {SessionId} completed compaction: {TokensRemoved} tokens removed",
					session.Id, compaction.Data?.TokensRemoved);
					break;

				// Tier 2 — informational logging only
				case AssistantIntentEvent intent:
					_logger.LogDebug("Session {SessionId} assistant intent: {Intent}", session.Id, intent.Data?.Intent);
					break;

				case AssistantTurnEndEvent turnEnd:
					_logger.LogDebug("Session {SessionId} assistant turn ended: {TurnId}", session.Id, turnEnd.Data?.TurnId);
					break;

				case AssistantUsageEvent usage:
					_logger.LogDebug("Session {SessionId} usage — model: {Model}, in: {In}, out: {Out}, cost: {Cost}",
					session.Id, usage.Data?.Model, usage.Data?.InputTokens, usage.Data?.OutputTokens, usage.Data?.Cost);
					break;

				case SessionInfoEvent info:
					_logger.LogInformation("Session {SessionId} info [{InfoType}]: {Message}",
					session.Id, info.Data?.InfoType, info.Data?.Message);
					break;

				case SessionStartEvent start:
					_logger.LogInformation("Session {SessionId} started — producer: {Producer}, model: {Model}",
					session.Id, start.Data?.Producer, start.Data?.SelectedModel);
					break;

				case SessionResumeEvent resume:
					_logger.LogInformation("Session {SessionId} resumed at {ResumeTime}, {EventCount} prior events",
					session.Id, resume.Data?.ResumeTime, resume.Data?.EventCount);
					break;

				case SessionContextChangedEvent ctxChanged:
					_logger.LogInformation("Session {SessionId} context changed — cwd: {Cwd}, repo: {Repo}, branch: {Branch}",
					session.Id, ctxChanged.Data?.Cwd, ctxChanged.Data?.Repository, ctxChanged.Data?.Branch);
					break;

				case SessionModeChangedEvent modeChanged:
					_logger.LogInformation("Session {SessionId} mode changed: {Prev} → {New}",
					session.Id, modeChanged.Data?.PreviousMode, modeChanged.Data?.NewMode);
					break;

				case SessionModelChangeEvent modelChange:
					_logger.LogInformation("Session {SessionId} model changed: {Prev} → {New}",
					session.Id, modelChange.Data?.PreviousModel, modelChange.Data?.NewModel);
					break;

				case SessionHandoffEvent handoff:
					_logger.LogInformation("Session {SessionId} handoff — source: {Source}, summary: {Summary}",
					session.Id, handoff.Data?.SourceType, handoff.Data?.Summary);
					break;

				case SessionTruncationEvent truncation:
					_logger.LogInformation("Session {SessionId} truncated — {MessagesRemoved} messages, {TokensRemoved} tokens removed",
					session.Id, truncation.Data?.MessagesRemovedDuringTruncation, truncation.Data?.TokensRemovedDuringTruncation);
					break;

				case SessionUsageInfoEvent usageInfo:
					_logger.LogDebug("Session {SessionId} usage info — {Current}/{Limit} tokens, {Messages} messages",
					session.Id, usageInfo.Data?.CurrentTokens, usageInfo.Data?.TokenLimit, usageInfo.Data?.MessagesLength);
					break;

				case SessionPlanChangedEvent planChanged:
					_logger.LogDebug("Session {SessionId} plan changed: {Operation}", session.Id, planChanged.Data?.Operation);
					break;

				case SessionSnapshotRewindEvent snapshotRewind:
					_logger.LogInformation("Session {SessionId} snapshot rewind — {EventsRemoved} events removed",
					session.Id, snapshotRewind.Data?.EventsRemoved);
					break;

				case SessionWorkspaceFileChangedEvent fileChanged:
					_logger.LogDebug("Session {SessionId} workspace file {Operation}: {Path}",
					session.Id, fileChanged.Data?.Operation, fileChanged.Data?.Path);
					break;

				case HookStartEvent hookStart:
					_logger.LogDebug("Session {SessionId} hook started — type: {HookType}, id: {Id}",
					session.Id, hookStart.Data?.HookType, hookStart.Data?.HookInvocationId);
					break;

				case HookEndEvent hookEnd:
					_logger.LogInformation("Session {SessionId} hook ended — type: {HookType}, success: {Success}",
					session.Id, hookEnd.Data?.HookType, hookEnd.Data?.Success);
					break;

				case SkillInvokedEvent skill:
					_logger.LogInformation("Session {SessionId} skill invoked: {Name}", session.Id, skill.Data?.Name);
					break;

				case SubagentSelectedEvent subagentSelected:
					_logger.LogInformation("Session {SessionId} subagent selected: {AgentName}", session.Id, subagentSelected.Data?.AgentName);
					break;

				case SystemMessageEvent systemMsg:
					_logger.LogDebug("Session {SessionId} system message [{Role}]", session.Id, systemMsg.Data?.Role);
					break;

				case ToolUserRequestedEvent toolUserRequested:
					_logger.LogDebug("Session {SessionId} tool user requested: {ToolName} (handled by permission callback)",
					session.Id, toolUserRequested.Data?.ToolName);
					break;

				case PendingMessagesModifiedEvent:
					_logger.LogDebug("Session {SessionId} pending messages modified", session.Id);
					break;

				default:
_logger.LogDebug("Unhandled event type {EventType} for session {SessionId}", evt.Type, session.Id);
break;
			}
		}
		catch(Exception ex)
		{
			_logger.LogError(ex, "Error handling session event {EventType} for session {SessionId}",
				evt.Type, session.Id);
		}

		// Update LastActivity only for meaningful events (user/assistant messages, tool executions)
		if(evt is UserMessageEvent or AssistantMessageEvent or AssistantTurnEndEvent
			or ToolExecutionStartEvent or ToolExecutionCompleteEvent or SessionIdleEvent
			or SubagentStartedEvent or SubagentCompletedEvent or SessionErrorEvent)
		{
			session.LastActivity = evt.Timestamp.LocalDateTime;
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

		// Safety net: finalize any prior group not yet closed by SessionIdleEvent
		if(session.ActiveWorkingGroup is not null)
		{
			HandleSessionIdle(session);
		}

		ChatMessage message = new()
		{
			Id = Guid.NewGuid().ToString(),
			Content = evt.Data.Content ?? string.Empty,
			IsUser = true,
			Timestamp = evt.Timestamp,
			Type = MessageType.Text,
			EventType = evt.Type
		};

		session.Messages.Add(message);
		session.Status = SessionStatus.Running;

		// Only notify if this is the current visible session
		if(session == CurrentSession)
		{
			NotifyStateChanged();
		}
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
		if(session.ActiveWorkingGroup is not null && session.ActiveWorkingGroup.Status == GroupStatus.Running)
		{
			// Just update the streaming message tracker, don't add to chat
			if(!session.StreamingMessages.TryGetValue(messageId, out ChatMessage? message))
			{
				message = new ChatMessage
				{
					Id = messageId,
					Content = string.Empty,
					IsUser = false,
					Timestamp = evt.Timestamp,
					Type = MessageType.Text,
					IsStreaming = true,
					IsComplete = false,
					EventType = evt.Type
				};
				session.StreamingMessages[messageId] = message;
				// DON'T add to session.Messages - it will go in thinking panel
			}

			message.Content += evt.Data.DeltaContent ?? string.Empty;
	
			// Only notify if this is the current visible session
			if(session == CurrentSession)
			{
				NotifyStateChanged();
			}
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

		// Only notify if this is the current visible session
		if(session == CurrentSession)
		{
			NotifyStateChanged();
		}
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
		if(session.ActiveWorkingGroup is not null && session.ActiveWorkingGroup.Status == GroupStatus.Running)
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
				if(session.ActiveWorkingGroup.InitialMessageId is null)
				{
					session.ActiveWorkingGroup.InitialMessageId = messageId;
				}

		
				// Only notify if this is the current visible session
				if(session == CurrentSession)
				{
					NotifyStateChanged();
				}
				return;
			}

			// All messages during thinking go to the thinking panel
			// The last one will be extracted as the summary when SessionIdle fires
			if(!string.IsNullOrWhiteSpace(content))
			{
				session.ActiveWorkingGroup.AddEvent(new ThinkingEvent
				{
					Id = messageId,
					Type = ThinkingEventType.Message,
					Message = content,
					Timestamp = evt.Timestamp.LocalDateTime
				});
				Debug.WriteLine("Added intermediate message to thinking group");
			}

			// Clean up streaming tracker
			if(streamingMsg is not null)
			{
				session.StreamingMessages.Remove(messageId);
			}

	
			// Only notify if this is the current visible session
			if(session == CurrentSession)
			{
				NotifyStateChanged();
			}
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
				Timestamp = evt.Timestamp,
				Type = MessageType.Text,
				IsComplete = true,
				EventType = evt.Type
			};
			session.Messages.Add(message);
		}


		// Only notify if this is the current visible session
		if(session == CurrentSession)
		{
			NotifyStateChanged();
		}
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

		// Only notify if this is the current visible session
		if(session == CurrentSession)
		{
			NotifyStateChanged();
		}
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


		// Only notify if this is the current visible session
		if(session == CurrentSession)
		{
			NotifyStateChanged();
		}
	}

	void HandleAssistantTurnStart(ChatSession session)
	{
		Debug.WriteLine("HandleAssistantTurnStart");

		session.Status = SessionStatus.Running;

		// Only notify if this is the current visible session
		if(session == CurrentSession)
		{
			NotifyStateChanged();
		}
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
		if(session.ActiveWorkingGroup is null)
		{
			session.ActiveWorkingGroup = new ActivityGroup
			{
				StartTime = evt.Timestamp.LocalDateTime,
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
				session.ActiveWorkingGroup.InitialMessageId = lastAssistantMessage.Id;
				Debug.WriteLine($"Tracked initial message: {lastAssistantMessage.Id}");
			}
		}

		// Create tool execution with full details
		ToolExecution toolExec = new()
		{
			ToolName = evt.Data.ToolName ?? "unknown",
			ToolCallId = evt.Data.ToolCallId,
			InputParameters = DeserializeArguments(evt.Data.Arguments),
			StartTime = evt.Timestamp.LocalDateTime,
			Status = ToolStatus.Running
		};

		toolExec.RawEvents.Add(new Lazy<string>(() => SerializeEvent(evt)));

		// If this is a child call (belongs to a subagent), nest it under the parent
		string? parentCallId = evt.Data.ParentToolCallId;
		if(parentCallId is not null)
		{
			ToolExecution? parent = FindToolExecution(session.ActiveWorkingGroup, parentCallId);
			if(parent is not null)
			{
				parent.AddChild(toolExec);

				if(session == CurrentSession)
				{
					NotifyStateChanged();
				}

				return;
			}
		}

		// Top-level tool — add as a thinking event (chronologically ordered with messages)
		session.ActiveWorkingGroup.AddEvent(new ThinkingEvent
		{
			Type = ThinkingEventType.Tool,
			Tool = toolExec,
			Timestamp = evt.Timestamp.LocalDateTime
		});


		// Only notify if this is the current visible session
		if(session == CurrentSession)
		{
			NotifyStateChanged();
		}
	}

	void HandleToolComplete(ChatSession session, ToolExecutionCompleteEvent evt)
	{
		Debug.WriteLine("HandleToolComplete");
		Debug.WriteLine(evt);

		if(evt.Data is null || session.ActiveWorkingGroup is null)
		{
			return;
		}

		// Find the tool execution in the active group, including children (thread-safe)
		ToolExecution? toolExec = FindToolExecution(session.ActiveWorkingGroup, evt.Data.ToolCallId);

		if(toolExec is not null)
		{
			toolExec.Status = ToolStatus.Success;
			toolExec.IsSuccess = true;
			toolExec.EndTime = evt.Timestamp.LocalDateTime;
			toolExec.Output = evt.Data.Result?.ToString();
			toolExec.RawEvents.Add(new Lazy<string>(() => SerializeEvent(evt)));
		}


		// Only notify if this is the current visible session
		if(session == CurrentSession)
		{
			NotifyStateChanged();
		}
	}

	void HandleSessionIdle(ChatSession session, DateTimeOffset? eventTimestamp = null)
	{
		DateTime now = eventTimestamp?.LocalDateTime ?? DateTime.Now;
		Debug.WriteLine("HandleSessionIdle - Finalizing activity group");

		if(session.ActiveWorkingGroup is not null && session.ActiveWorkingGroup.Tools.Any())
		{
			ActivityGroup group = session.ActiveWorkingGroup;
			Debug.WriteLine($"Finalizing thinking group. Has {group.Tools.Count()} tools");

			// Check if activity message already exists for this group
			bool activityMessageExists = session.Messages.Any(m =>
				m.Type == MessageType.ActivityGroup && m.ActivityGroup?.Id == group.Id);

			if(activityMessageExists)
			{
				Debug.WriteLine($"Activity message already exists for group {group.Id}, skipping insertion");
				session.ActiveWorkingGroup = null;

				// Only notify if this is the current visible session
				if(session == CurrentSession)
				{
					NotifyStateChanged();
				}
				return;
			}

			// Mark any still-running tools as stopped (Error status), including children
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

				foreach(ToolExecution child in tool.GetChildrenSnapshot())
				{
					if(child.Status == ToolStatus.Running)
					{
						child.Status = ToolStatus.Error;
						child.EndTime = DateTime.Now;
						child.IsSuccess = false;
					}
				}
			}

			group.Status = GroupStatus.Complete;
			group.EndTime = now;
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
					Timestamp = now,
					Type = MessageType.Text,
					IsStreaming = true,
					IsComplete = false
				};
				session.Messages.Add(summaryMsg);
				Debug.WriteLine($"Added summary message to chat: {summaryMsg.Id}");

				// Stream the summary text progressively (only for current session)
				if(session == CurrentSession)
				{
					_ = StreamSummaryTextAsync(session, summaryMsg, lastMessage.Message);
				}
				else
				{
					// For background sessions, just set the content immediately
					summaryMsg.Content = lastMessage.Message;
					summaryMsg.IsStreaming = false;
					summaryMsg.IsComplete = true;
				}
				hasSummary = true;
			}

			// Add "Session stopped" event to operation list AFTER extracting summary
			if(hasStoppedTools)
			{
				group.AddEvent(new ThinkingEvent
				{
					Type = ThinkingEventType.Message,
					Message = "Session stopped",
					Timestamp = now
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
			session.ActiveWorkingGroup = null;
		}
		else if(session.ActiveWorkingGroup is not null)
		{
			Debug.WriteLine("Clearing empty thinking group");
			session.ActiveWorkingGroup = null;
		}
		else
		{
			Debug.WriteLine("No active thinking group to finalize");
		}

		session.Status = SessionStatus.Idle;

		// Only notify if this is the current visible session
		if(session == CurrentSession)
		{
			NotifyStateChanged();
		}
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

		// Only notify if this is the current visible session
		if(session == CurrentSession)
		{
			NotifyStateChanged();
		}
	}

	void HandleSessionTitleChanged(ChatSession session, SessionTitleChangedEvent evt)
	{
		session.Title = evt.Data.Title;

		if(session == CurrentSession)
		{
			NotifyStateChanged();
		}
	}

	void HandleAbort(ChatSession session, AbortEvent evt)
	{
		_logger.LogWarning("Session {SessionId} aborted: {Reason}", session.Id, evt.Data.Reason);

		if(session.ActiveWorkingGroup is not null)
		{
			session.ActiveWorkingGroup.Status = GroupStatus.Error;
			session.ActiveWorkingGroup.EndTime = DateTime.Now;
			session.ActiveWorkingGroup = null;
		}

		session.Status = SessionStatus.Idle;

		if(session == CurrentSession)
		{
			NotifyStateChanged();
		}
	}

	void HandleSessionShutdown(ChatSession session, SessionShutdownEvent evt)
	{
		_logger.LogInformation("Session {SessionId} shutdown — type: {ShutdownType}, requests: {Requests}, duration: {Duration}ms",
			session.Id, evt.Data.ShutdownType, evt.Data.TotalPremiumRequests, evt.Data.TotalApiDurationMs);

		if(session.ActiveWorkingGroup is not null)
		{
			session.ActiveWorkingGroup.Status = GroupStatus.Complete;
			session.ActiveWorkingGroup.EndTime = DateTime.Now;
			session.ActiveWorkingGroup = null;
		}

		session.Status = SessionStatus.Idle;

		if(session == CurrentSession)
		{
			NotifyStateChanged();
		}
	}

	void HandleSessionWarning(ChatSession session, SessionWarningEvent evt)
	{
		_logger.LogWarning("Session {SessionId} warning [{WarningType}]: {Message}",
			session.Id, evt.Data.WarningType, evt.Data.Message);

		ChatMessage message = new()
		{
			Id = Guid.NewGuid().ToString(),
			Content = evt.Data.Message,
			IsUser = false,
			Timestamp = DateTime.Now,
			Type = MessageType.Error,
			EventType = evt.Type
		};

		session.Messages.Add(message);

		if(session == CurrentSession)
		{
			NotifyStateChanged();
		}
	}

	void HandleToolProgress(ChatSession session, ToolExecutionProgressEvent evt)
	{
		if(session.ActiveWorkingGroup is null)
		{
			return;
		}

		ToolExecution? toolExec = FindToolExecution(session.ActiveWorkingGroup, evt.Data.ToolCallId);

		if(toolExec is not null)
		{
			toolExec.ProgressMessage = evt.Data.ProgressMessage;
			toolExec.RawEvents.Add(new Lazy<string>(() => SerializeEvent(evt)));
	
			if(session == CurrentSession)
			{
				NotifyStateChanged();
			}
		}
	}

	void HandleToolPartialResult(ChatSession session, ToolExecutionPartialResultEvent evt)
	{
		if(session.ActiveWorkingGroup is null)
		{
			return;
		}

		ToolExecution? toolExec = FindToolExecution(session.ActiveWorkingGroup, evt.Data.ToolCallId);

		if(toolExec is not null)
		{
			toolExec.Output = (toolExec.Output ?? string.Empty) + evt.Data.PartialOutput;
	
			if(session == CurrentSession)
			{
				NotifyStateChanged();
			}
		}
	}

	void HandleSubagentStarted(ChatSession session, SubagentStartedEvent evt)
	{
		if(session.ActiveWorkingGroup is null)
		{
			session.ActiveWorkingGroup = new ActivityGroup
			{
				StartTime = evt.Timestamp.LocalDateTime,
				Status = GroupStatus.Running,
				IsExpanded = true
			};
		}

		// Check if a ToolExecution already exists for this ToolCallId (created by tool.execution_start)
		// If so, update it in-place rather than creating a duplicate entry
		List<ThinkingEvent> existingEvents = session.ActiveWorkingGroup.GetEventsSnapshot();
		ToolExecution? existingExec = existingEvents
			.Where(e => e.Type == ThinkingEventType.Tool && e.Tool is not null)
			.Select(e => e.Tool!)
			.FirstOrDefault(t => t.ToolCallId == evt.Data.ToolCallId);

		if(existingExec is not null)
		{
			existingExec.ToolName = evt.Data.AgentDisplayName ?? existingExec.ToolName;
			existingExec.RawEvents.Add(new Lazy<string>(() => SerializeEvent(evt)));
		}
		else
		{
			ToolExecution subagentExec = new()
			{
				ToolName = evt.Data.AgentDisplayName ?? string.Empty,
				ToolCallId = evt.Data.ToolCallId,
				StartTime = evt.Timestamp.LocalDateTime,
				Status = ToolStatus.Running
			};

			subagentExec.RawEvents.Add(new Lazy<string>(() => SerializeEvent(evt)));

			session.ActiveWorkingGroup.AddEvent(new ThinkingEvent
			{
				Type = ThinkingEventType.Tool,
				Tool = subagentExec,
				Timestamp = evt.Timestamp.LocalDateTime
			});
		}


		if(session == CurrentSession)
		{
			NotifyStateChanged();
		}
	}

	void HandleSubagentCompleted(ChatSession session, SubagentCompletedEvent evt)
	{
		if(session.ActiveWorkingGroup is null)
		{
			return;
		}

		List<ThinkingEvent> events = session.ActiveWorkingGroup.GetEventsSnapshot();
		ToolExecution? subagentExec = events
			.Where(e => e.Type == ThinkingEventType.Tool && e.Tool is not null)
			.Select(e => e.Tool!)
			.FirstOrDefault(t => t.ToolCallId == evt.Data.ToolCallId);

		if(subagentExec is not null)
		{
			subagentExec.Status = ToolStatus.Success;
			subagentExec.IsSuccess = true;
			subagentExec.EndTime = DateTime.Now;
			subagentExec.RawEvents.Add(new Lazy<string>(() => SerializeEvent(evt)));
		}


		if(session == CurrentSession)
		{
			NotifyStateChanged();
		}
	}

	void HandleSubagentFailed(ChatSession session, SubagentFailedEvent evt)
	{
		if(session.ActiveWorkingGroup is null)
		{
			return;
		}

		List<ThinkingEvent> events = session.ActiveWorkingGroup.GetEventsSnapshot();
		ToolExecution? subagentExec = events
			.Where(e => e.Type == ThinkingEventType.Tool && e.Tool is not null)
			.Select(e => e.Tool!)
			.FirstOrDefault(t => t.ToolCallId == evt.Data.ToolCallId);

		if(subagentExec is not null)
		{
			subagentExec.Status = ToolStatus.Error;
			subagentExec.IsSuccess = false;
			subagentExec.EndTime = DateTime.Now;
			subagentExec.Output = evt.Data.Error;
			subagentExec.RawEvents.Add(new Lazy<string>(() => SerializeEvent(evt)));
		}


		if(session == CurrentSession)
		{
			NotifyStateChanged();
		}
	}

	async Task StreamSummaryTextAsync(ChatSession session, ChatMessage message, string fullText)
	{
		const int chunkSize = 3; // Characters per tick
		const int delayMs = 8; // Milliseconds between chunks

		for(int i = 0; i < fullText.Length; i += chunkSize)
		{
			int end = Math.Min(i + chunkSize, fullText.Length);
			message.Content = fullText[..end];

			// Only notify if this is still the current session
			if(session == CurrentSession)
			{
				NotifyStateChanged();
			}
			await Task.Delay(delayMs);
		}

		message.Content = fullText;
		message.IsStreaming = false;
		message.IsComplete = true;

		// Only notify if this is still the current session
		if(session == CurrentSession)
		{
			NotifyStateChanged();
		}
	}
	static readonly JsonSerializerOptions _jsonOptions = new() { WriteIndented = true };

	/// <summary>
	/// Finds a ToolExecution by ToolCallId in the group's top-level tools and their children.
	/// </summary>
	static ToolExecution? FindToolExecution(ActivityGroup group, string? toolCallId)
	{
		if(toolCallId is null)
		{
			return null;
		}

		foreach(ThinkingEvent evt in group.GetEventsSnapshot())
		{
			if(evt.Type != ThinkingEventType.Tool || evt.Tool is null)
			{
				continue;
			}

			if(evt.Tool.ToolCallId == toolCallId)
			{
				return evt.Tool;
			}

			ToolExecution? child = evt.Tool.GetChildrenSnapshot()
				.FirstOrDefault(c => c.ToolCallId == toolCallId);

			if(child is not null)
			{
				return child;
			}
		}

		return null;
	}

	static string SerializeEvent(SessionEvent evt)
	{
		try
		{
			return JsonSerializer.Serialize(evt, evt.GetType(), _jsonOptions);
		}
		catch
		{
			return evt.ToString() ?? string.Empty;
		}
	}
}
