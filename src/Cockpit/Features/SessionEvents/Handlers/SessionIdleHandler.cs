using System.Diagnostics;
using Cockpit.Features.SessionEvents.Models;
using Cockpit.Models;

namespace Cockpit.Features.SessionEvents.Handlers;

static class SessionIdleHandler
{
	internal static void Handle(ChatSession session, DateTimeOffset? eventTimestamp = null, Func<ChatMessageModel, string, Task>? onStreamSummary = null, GroupStatusEnum groupStatus = GroupStatusEnum.Complete)
	{
		DateTime now = eventTimestamp?.LocalDateTime ?? DateTime.Now;
		Debug.WriteLine("SessionIdleHandler - Finalizing activity group");

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
				return;
			}

			// Mark any still-running tools as stopped (Error status), including children
			bool hasStoppedTools = false;
			foreach(ToolExecutionModel tool in group.Tools)
			{
				if(tool.Status == ToolStatusEnum.Running)
				{
					tool.Status = ToolStatusEnum.Error;
					tool.EndTime = DateTime.Now;
					tool.IsSuccess = false;
					hasStoppedTools = true;
				}

				foreach(ToolExecutionModel child in tool.GetChildrenSnapshot())
				{
					if(child.Status == ToolStatusEnum.Running)
					{
						child.Status = ToolStatusEnum.Error;
						child.EndTime = DateTime.Now;
						child.IsSuccess = false;
					}
				}
			}

			group.Status = groupStatus;
			group.EndTime = now;
			group.IsExpanded = false;

			// Extract the last message event as the summary (but not "Session stopped")
			List<ThinkingEventModel> events = group.GetEventsSnapshot();
			ThinkingEventModel? lastMessage = events.LastOrDefault(e => e.Type == ThinkingEventTypeEnum.Message);

			bool hasSummary = false;
			if(lastMessage is not null && !string.IsNullOrWhiteSpace(lastMessage.Message))
			{
				// Remove the summary from thinking events
				group.RemoveEvent(lastMessage);

				// Add summary message as streaming - will be progressively revealed
				ChatMessageModel summaryMsg = new()
				{
					Id = lastMessage.Id ?? Guid.NewGuid().ToString(),
					Content = string.Empty,
					IsUser = false,
					Timestamp = now,
					Type = MessageTypeEnum.Text,
					IsStreaming = true,
					IsComplete = false
				};
				session.Messages.Add(summaryMsg);
				Debug.WriteLine($"Added summary message to chat: {summaryMsg.Id}");

				if(onStreamSummary is not null)
				{
					// Stream progressively for the current (visible) session
					_ = onStreamSummary(summaryMsg, lastMessage.Message);
				}
				else
				{
					// For background sessions, set the content immediately
					summaryMsg.Content = lastMessage.Message;
					summaryMsg.IsStreaming = false;
					summaryMsg.IsComplete = true;
				}
				hasSummary = true;
			}

			// Add termination event to operation list AFTER extracting summary
			if(groupStatus == GroupStatusEnum.Error)
			{
				group.AddEvent(new ThinkingEventModel
				{
					Type = ThinkingEventTypeEnum.Message,
					Message = "Session aborted",
					Timestamp = now
				});
			}
			else if(hasStoppedTools)
			{
				group.AddEvent(new ThinkingEventModel
				{
					Type = ThinkingEventTypeEnum.Message,
					Message = "Session stopped",
					Timestamp = now
				});
			}

			// Only insert an activity group into chat when there were actual tool operations
			if(group.Tools.Any() || groupStatus == GroupStatusEnum.Error)
			{
				ChatMessageModel activityMessage = new()
				{
					IsUser = false,
					Type = MessageTypeEnum.ActivityGroup,
					ActivityGroup = group,
					Timestamp = group.EndTime ?? DateTime.Now,
					Content = group.Tools.Any() ? GenerateActivitySummary(group) : "Aborted"
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
						int insertIndex = hasSummary ? Math.Max(0, session.Messages.Count - 1) : session.Messages.Count;
						session.Messages.Insert(insertIndex, activityMessage);
					}
				}
				else
				{
					int lastUserIndex = -1;
					int searchLimit = hasSummary ? session.Messages.Count - 2 : session.Messages.Count - 1;
					for(int i = searchLimit; i >= 0; i--)
					{
						// Skip pending or not-yet-confirmed user messages — they belong to a future turn
						if(session.Messages[i].IsUser && session.Messages[i].IsComplete && !session.Messages[i].IsPending)
						{
							lastUserIndex = i;
							break;
						}
					}

					int insertIndex = lastUserIndex >= 0 ? lastUserIndex + 1 : hasSummary ? Math.Max(0, session.Messages.Count - 1) : session.Messages.Count;

					// Ensure we never insert at index 0 if there are messages
					if(insertIndex == 0 && session.Messages.Count > 0)
					{
						insertIndex = session.Messages.Count;
						Debug.WriteLine($"WARNING: Attempted to insert activity group at index 0, moved to end");
					}

					session.Messages.Insert(insertIndex, activityMessage);
					Debug.WriteLine($"Inserted activity group at index {insertIndex} (lastUserIndex={lastUserIndex}, hasSummary={hasSummary}, Count={session.Messages.Count})");
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

		session.Status = SessionStatus.Idle;
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
