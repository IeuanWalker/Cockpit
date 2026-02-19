using Cockpit.Models;
using GitHub.Copilot.SDK;

namespace Cockpit.Features.SessionEvents.Handlers;

static class SubagentStartedHandler
{
	internal static void Handle(ChatSession session, SubagentStartedEvent evt)
	{
		session.ActiveWorkingGroup ??= new ActivityGroup
		{
			StartTime = evt.Timestamp.LocalDateTime,
			Status = GroupStatus.Running,
			IsExpanded = true
		};

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
			existingExec.AddRawEvent(new Lazy<string>(() => SessionEventHelpers.SerializeEvent(evt)));
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

			subagentExec.AddRawEvent(new Lazy<string>(() => SessionEventHelpers.SerializeEvent(evt)));

			session.ActiveWorkingGroup.AddEvent(new ThinkingEvent
			{
				Type = ThinkingEventType.Tool,
				Tool = subagentExec,
				Timestamp = evt.Timestamp.LocalDateTime
			});
		}
	}
}
