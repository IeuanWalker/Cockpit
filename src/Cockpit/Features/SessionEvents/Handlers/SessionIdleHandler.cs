using System.Diagnostics;
using Cockpit.Features.SessionEvents.Models;
using Cockpit.Features.Sessions.Models;

namespace Cockpit.Features.SessionEvents.Handlers;

static class SessionIdleHandler
{
	/// <summary>
	/// Raised when a session completes successfully (transitions to Idle after active work).
	/// Not raised on error/abort, or while a suppression scope is active.
	/// </summary>
	internal static event Action? OnSessionFinished;

	static int suppressCount;

	/// <summary>
	/// Returns a scope that suppresses <see cref="OnSessionFinished"/> until disposed.
	/// Safe to nest; the event is only re-enabled once all scopes are disposed.
	/// </summary>
	internal static IDisposable SuppressFinishedNotification()
	{
		Interlocked.Increment(ref suppressCount);
		return new SuppressScope();
	}

	sealed class SuppressScope : IDisposable
	{
		public void Dispose() => Interlocked.Decrement(ref suppressCount);
	}
	internal static void Handle(SessionModel session, Func<ChatMessageModel, string, Task>? onStreamSummary = null, GroupStatusEnum groupStatus = GroupStatusEnum.Complete)
	{
		if(session.ActiveWorkingGroup is not null)
		{
			ThinkingEventModel? lastEvent = session.ActiveWorkingGroup.Events.LastOrDefault();
			DateTimeOffset timestamp = lastEvent is not null ? lastEvent.Timestamp : DateTimeOffset.UtcNow;
			Handle(session, timestamp, onStreamSummary, groupStatus);
		}
		else
		{
			DateTimeOffset timestamp = session.Messages.LastOrDefault()?.Timestamp ?? DateTimeOffset.UtcNow;
			Handle(session, timestamp, onStreamSummary, groupStatus);
		}
	}
	internal static void Handle(SessionModel session, DateTimeOffset eventTimestamp, Func<ChatMessageModel, string, Task>? onStreamSummary = null, GroupStatusEnum groupStatus = GroupStatusEnum.Complete)
	{
		ActivityGroupModel? activeGroup = session.ActiveWorkingGroup;

		bool hasThinkingMessages = activeGroup?.GetEventsSnapshot().Any(e => e.Type == ThinkingEventTypeEnum.Message && !string.IsNullOrWhiteSpace(e.Message)) ?? false;
		if(activeGroup is not null && (activeGroup.Tools.Any() || hasThinkingMessages || groupStatus == GroupStatusEnum.Error))
		{
			ActivityGroupModel group = activeGroup;
			Debug.WriteLine($"Finalizing thinking group. Has {group.Tools.Count()} tools");

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

			// Mark any still-running tools as stopped (Error status), including children
			bool hasStoppedTools = false;
			foreach(ToolExecutionModel tool in group.Tools)
			{
				if(tool.Status == ToolStatusEnum.Running)
				{
					tool.Status = ToolStatusEnum.Error;
					tool.EndTime = tool.EndTime;
					tool.IsSuccess = false;
					hasStoppedTools = true;
				}

				foreach(ToolExecutionModel child in tool.GetChildrenSnapshot())
				{
					if(child.Status == ToolStatusEnum.Running)
					{
						child.Status = ToolStatusEnum.Error;
						child.EndTime = child.EndTime;
						child.IsSuccess = false;
					}
				}
			}

			group.Status = groupStatus;
			group.EndTime = eventTimestamp.LocalDateTime;
			group.IsExpanded = false;

			// Extract the last message event as the summary (but not "Session stopped")
			List<ThinkingEventModel> events = group.GetEventsSnapshot();
			ThinkingEventModel? lastMessage = events.LastOrDefault(e => e.Type == ThinkingEventTypeEnum.Message);

			ChatMessageModel? summaryMsg = null;
			if(lastMessage is not null && !string.IsNullOrWhiteSpace(lastMessage.Message))
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
			// Priority: InitialMessageId (assistant first msg) > TriggeredByUserMessageId > last non-pending user msg
			int anchorIndex = -1;
			if(!string.IsNullOrEmpty(group.InitialMessageId))
			{
				anchorIndex = session.Messages.FindIndex(m => m.Id == group.InitialMessageId);
			}
			else if(!string.IsNullOrEmpty(group.TriggeredByUserMessageId))
			{
				anchorIndex = session.Messages.FindIndex(m => m.Id == group.TriggeredByUserMessageId);
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

			// Only insert an activity group into chat when there were actual tool operations
			int activityInsertedAt = -1;
			if(group.Tools.Any() || groupStatus == GroupStatusEnum.Error)
			{
				ChatMessageModel activityMessage = new()
				{
					IsUser = false,
					Type = MessageTypeEnum.ActivityGroup,
					ActivityGroup = group,
					Timestamp = group.EndTime ?? DateTime.Now,
					Content = group.Tools.Any() ? GenerateActivitySummary(group) : "Aborted",
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
					Debug.WriteLine($"WARNING: No anchor found, appending activity group");
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
					_ = onStreamSummary(summaryMsg, lastMessage?.Message ?? string.Empty);
				}
				else
				{
					// For background sessions, set the content immediately
					summaryMsg.Content = lastMessage?.Message ?? string.Empty;
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

		session.Status = SessionStatusEnum.Idle;

		if(groupStatus == GroupStatusEnum.Complete && suppressCount == 0)
		{
			OnSessionFinished?.Invoke();
		}
	}

	static string GenerateActivitySummary(ActivityGroupModel group)
	{
		List<ToolExecutionModel> tools = [.. group.Tools];
		IEnumerable<string> toolNames = tools.Select(t => t.ToolName).Distinct().Take(3);
		int more = tools.Select(t => t.ToolName).Distinct().Count() - 3;
		string preview = string.Join(", ", toolNames) + (more > 0 ? $", +{more}" : "");

		return $"{tools.Count} operations ({preview})";
	}
}
