using System.Diagnostics;
using Cockpit.Features.SessionEvents.Models;
using Cockpit.Features.Sessions.Models;
using Microsoft.Extensions.Logging;

namespace Cockpit.Features.SessionEvents.Handlers;

static class SessionIdleHandler
{
	/// <summary>
	/// Raised when a session completes successfully (transitions to Idle after active work).
	/// Not raised on error/abort, or when <see cref="SessionModel.SuppressFinishedNotification"/> is set.
	/// </summary>
	internal static event Action? OnSessionFinished;

	internal static void Handle(SessionModel session, Func<ChatMessageModel, string, Task>? onStreamSummary = null, GroupStatusEnum groupStatus = GroupStatusEnum.Complete, ILogger? logger = null, bool suppressSummary = false)
	{
		if(session.ActiveWorkingGroup is not null)
		{
			ThinkingEventModel? lastEvent = session.ActiveWorkingGroup.GetEventsSnapshot().LastOrDefault();
			DateTimeOffset timestamp = lastEvent is not null ? lastEvent.Timestamp : DateTimeOffset.UtcNow;
			Handle(session, timestamp, onStreamSummary, groupStatus, logger, suppressSummary);
		}
		else
		{
			DateTimeOffset timestamp = session.Messages.LastOrDefault()?.Timestamp ?? DateTimeOffset.UtcNow;
			Handle(session, timestamp, onStreamSummary, groupStatus, logger, suppressSummary);
		}
	}
	internal static void Handle(SessionModel session, DateTimeOffset eventTimestamp, Func<ChatMessageModel, string, Task>? onStreamSummary = null, GroupStatusEnum groupStatus = GroupStatusEnum.Complete, ILogger? logger = null, bool suppressSummary = false)
	{
		ActivityGroupModel? activeGroup = session.ActiveWorkingGroup;

		bool hasThinkingMessages = activeGroup?.GetEventsSnapshot().Any(e => (e.Type == ThinkingEventTypeEnum.Message || e.Type == ThinkingEventTypeEnum.Reasoning) && !string.IsNullOrWhiteSpace(e.Message)) ?? false;
		if(activeGroup is not null && (activeGroup.Tools.Any() || hasThinkingMessages || groupStatus == GroupStatusEnum.Error))
		{
			ActivityGroupModel group = activeGroup;
			List<ToolExecutionModel> tools = [.. group.Tools]; // Snapshot once — avoids repeated lock acquisitions
			Debug.WriteLine($"Finalizing thinking group. Has {tools.Count} tools");

			// Check if activity message already exists for this group
			bool activityMessageExists = session.Messages.Any(m =>
				m.Type == MessageTypeEnum.ActivityGroup && m.ActivityGroup?.Id == group.Id);

			if(activityMessageExists)
			{
				Debug.WriteLine($"Activity message already exists for group {group.Id}, skipping insertion");
				session.ActiveWorkingGroup = null;
				session.Status = SessionStatusEnum.Idle;
				return;
			}

			// Mark any still-running tools as stopped (Error status), recursively including all descendants
			bool hasStoppedTools = false;
			foreach(ToolExecutionModel tool in tools)
			{
				hasStoppedTools |= MarkStoppedRecursively(tool, eventTimestamp);
			}

			group.Status = groupStatus;
			group.EndTime = eventTimestamp.LocalDateTime;
			group.IsExpanded = false;

			// Check for a task-complete summary override before falling back to last message extraction.
			// Always clear PendingTaskSummary regardless of suppressSummary to prevent a stale value
			// from leaking into the next turn's idle event.
			string? pendingTaskSummary = session.PendingTaskSummary;
			session.PendingTaskSummary = null;

			// When the turn was interrupted by the safety net (suppressSummary) or ended with an error,
			// the last thinking message is intermediate planning text — not a final response.
			// Suppress both extraction paths so it stays inside the ops group expander.
			bool suppressSummaryContent = suppressSummary || groupStatus == GroupStatusEnum.Error;

			// Extract the last message event as the summary (but not "Session stopped")
			List<ThinkingEventModel> events = group.GetEventsSnapshot();
			ThinkingEventModel? lastMessage = (!suppressSummaryContent && pendingTaskSummary is null)
				? events.LastOrDefault(e => e.Type == ThinkingEventTypeEnum.Message)
				: null;

			ChatMessageModel? summaryMsg = null;
			if(!suppressSummaryContent && pendingTaskSummary is not null)
			{
				summaryMsg = new ChatMessageModel
				{
					Id = Guid.NewGuid().ToString(),
					Content = string.Empty,
					IsUser = false,
					Timestamp = eventTimestamp.LocalDateTime,
					Type = MessageTypeEnum.Text,
					IsStreaming = true,
					IsComplete = false,
					EventJson = null
				};
			}
			else if(!suppressSummaryContent && lastMessage is not null && !string.IsNullOrWhiteSpace(lastMessage.Message))
			{
				// Remove the summary from thinking events
				group.RemoveEvent(lastMessage);

				summaryMsg = new ChatMessageModel
				{
					Id = lastMessage.Id ?? Guid.NewGuid().ToString(),
					Content = string.Empty,
					IsUser = false,
					Timestamp = eventTimestamp.LocalDateTime,
					Type = MessageTypeEnum.Text,
					IsStreaming = true,
					IsComplete = false,
					EventJson = lastMessage.EventJson
				};
			}

			// Add termination event to operation list AFTER extracting summary
			if(groupStatus == GroupStatusEnum.Error)
			{
				group.AddEvent(new ThinkingEventModel
				{
					Type = ThinkingEventTypeEnum.Message,
					Message = "Session aborted",
					Timestamp = eventTimestamp.LocalDateTime,
					EventJson = null
				});
			}
			else if(hasStoppedTools)
			{
				group.AddEvent(new ThinkingEventModel
				{
					Type = ThinkingEventTypeEnum.Message,
					Message = "Session stopped",
					Timestamp = eventTimestamp.LocalDateTime,
					EventJson = null
				});
			}

			// Determine anchor index: the position we'll insert after
			// Priority: TriggeredByUserMessageId (user prompt) > InitialMessageId (assistant first msg) > last non-pending user msg
			// The activity group must never appear before the user message that triggered it.
			int anchorIndex = -1;
			int triggerIndex = -1;
			if(!string.IsNullOrEmpty(group.TriggeredByUserMessageId))
			{
				triggerIndex = session.Messages.FindIndex(m => m.Id == group.TriggeredByUserMessageId);
				anchorIndex = triggerIndex;
			}

			if(!string.IsNullOrEmpty(group.InitialMessageId))
			{
				int initialIndex = session.Messages.FindIndex(m => m.Id == group.InitialMessageId);
				// Only use InitialMessageId if it's after the triggering user message (or no trigger exists)
				if(initialIndex >= 0 && initialIndex > anchorIndex)
				{
					anchorIndex = initialIndex;
				}
			}

			if(anchorIndex < 0)
			{
				// Fallback: find last non-pending, complete user message
				for(int i = session.Messages.Count - 1; i >= 0; i--)
				{
					if(session.Messages[i].IsUser && session.Messages[i].IsComplete && !session.Messages[i].IsPending)
					{
						anchorIndex = i;
						break;
					}
				}
			}

			// Extend anchor past already-inserted non-user items (activity groups, summaries)
			// so that subsequent ops groups from multi-turn replay are placed in chronological order.
			// Stop at any user message — pending messages represent future turns and the ops group
			// for the current turn must appear before them, not after.
			if(anchorIndex >= 0)
			{
				for(int i = anchorIndex + 1; i < session.Messages.Count; i++)
				{
					if(session.Messages[i].IsUser)
					{
						// Any user message (pending or active) marks the boundary of the current turn.
						break;
					}
					else
					{
						// Skip past non-user items (activity groups, summaries from prior finalizations)
						anchorIndex = i;
					}
				}
			}

			// Only insert an activity group into chat when there were actual tool operations
			int activityInsertedAt = -1;
			if(tools.Count > 0 || groupStatus == GroupStatusEnum.Error)
			{
				ChatMessageModel activityMessage = new()
				{
					IsUser = false,
					Type = MessageTypeEnum.ActivityGroup,
					ActivityGroup = group,
					Timestamp = group.EndTime ?? DateTime.Now,
					Content = tools.Count > 0 ? GenerateActivitySummary(tools) : "Aborted",
					EventJson = null
				};

				int activityIndex;
				if(anchorIndex >= 0)
				{
					activityIndex = anchorIndex + 1;
				}
				else if(session.Messages.Count > 0)
				{
					activityIndex = session.Messages.Count;
					logger?.LogWarning(
						"Session {SessionId}: no anchor found for activity group (InitialMessageId={InitialMessageId}, TriggeredByUserMessageId={TriggeredByUserMessageId}), appending at end",
						session.Id, group.InitialMessageId, group.TriggeredByUserMessageId);
				}
				else
				{
					activityIndex = 0;
				}

				session.Messages.Insert(activityIndex, activityMessage);
				activityInsertedAt = activityIndex;
				Debug.WriteLine($"Inserted activity group at index {activityIndex} (anchor={anchorIndex})");
			}

			// Insert summary: right after activity (if any), otherwise right after anchor, otherwise before first pending
			if(summaryMsg is not null)
			{
				int summaryIndex;
				if(activityInsertedAt >= 0)
				{
					// Place summary right after the activity group
					summaryIndex = activityInsertedAt + 1;
				}
				else if(anchorIndex >= 0)
				{
					summaryIndex = anchorIndex + 1;
				}
				else
				{
					// No anchor — fall back to inserting before the first pending message
					int pendingIdx = session.Messages.FindIndex(m => m.IsUser && m.IsPending);
					summaryIndex = pendingIdx >= 0 ? pendingIdx : session.Messages.Count;
				}

				session.Messages.Insert(summaryIndex, summaryMsg);
				Debug.WriteLine($"Added summary message to chat at index {summaryIndex}: {summaryMsg.Id}");

				if(onStreamSummary is not null)
				{
					// Stream progressively for the current (visible) session
					_ = onStreamSummary(summaryMsg, pendingTaskSummary ?? lastMessage?.Message ?? string.Empty);
				}
				else
				{
					// For background sessions, set the content immediately
					summaryMsg.Content = pendingTaskSummary ?? lastMessage?.Message ?? string.Empty;
					summaryMsg.IsStreaming = false;
					summaryMsg.IsComplete = true;
				}
			}

			// Clear the thinking group
			session.ActiveWorkingGroup = null;
		}
		else if(activeGroup is not null)
		{
			Debug.WriteLine("Clearing empty thinking group");
			session.ActiveWorkingGroup = null;
		}
		else
		{
			Debug.WriteLine("No active thinking group to finalize");
		}

		// Keep the session running when the user has more queued work:
		// - Pending enqueued messages (IsPending = true in session.Messages)
		// - An immediate (steering) message sent while the agent was busy
		// In both cases the agent is about to start a new turn — suppress the completion
		// sound and keep the working panel visible with an empty placeholder group so the
		// UI does not flash through Idle between turns.
		bool keepRunning = groupStatus == GroupStatusEnum.Complete
			&& (session.Messages.Any(m => m.IsUser && m.IsPending) || session.HasQueuedImmediateMessage);

		// Consume the immediate-message flag here regardless of path. AssistantTurnStartHandler
		// deliberately leaves it set when the new turn fires before this idle event, so it
		// survives to this point where it can actually be acted on.
		session.HasQueuedImmediateMessage = false;

		if(keepRunning)
		{
			session.Status = SessionStatusEnum.Running;
			session.ActiveWorkingGroup = new ActivityGroupModel
			{
				StartTime = DateTime.Now,
				Status = GroupStatusEnum.Running,
				IsExpanded = true,
				IsPlaceholder = true
			};
		}
		else
		{
			session.Status = SessionStatusEnum.Idle;

			if(groupStatus == GroupStatusEnum.Complete && !session.SuppressFinishedNotification)
			{
				OnSessionFinished?.Invoke();
			}
		}
	}

	/// <summary>
	/// Raises the <see cref="OnSessionFinished"/> event externally (e.g. from the debounce timer
	/// in <see cref="Sessions.SessionFeature"/>).
	/// </summary>
	internal static void RaiseSessionFinished() => OnSessionFinished?.Invoke();

	static string GenerateActivitySummary(List<ToolExecutionModel> tools)
	{
		List<string> distinctNames = [.. tools.Select(t => t.ToolName).Distinct()];
		string preview = string.Join(", ", distinctNames.Take(3));
		if(distinctNames.Count > 3)
		{
			preview += $", +{distinctNames.Count - 3}";
		}

		return $"{tools.Count} operations ({preview})";
	}

	static bool MarkStoppedRecursively(ToolExecutionModel tool, DateTimeOffset timestamp)
	{
		bool stopped = false;
		if(tool.Status == ToolStatusEnum.Running)
		{
			tool.Status = ToolStatusEnum.Error;
			tool.EndTime = timestamp.LocalDateTime;
			tool.IsSuccess = false;
			stopped = true;
		}

		foreach(ToolExecutionModel child in tool.GetChildrenSnapshot())
		{
			stopped |= MarkStoppedRecursively(child, timestamp);
		}

		return stopped;
	}
}
