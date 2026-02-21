using Cockpit.Features.SessionEvents.Models;
using Cockpit.Features.Sessions.Models;
using GitHub.Copilot.SDK;

namespace Cockpit.Features.SessionEvents.Handlers;

static class SubagentStartedHandler
{
	internal static void Handle(SessionModel session, SubagentStartedEvent evt)
	{
		session.ActiveWorkingGroup ??= new ActivityGroupModel
		{
			StartTime = evt.Timestamp.LocalDateTime,
			Status = GroupStatusEnum.Running,
			IsExpanded = true
		};

		// Check if a ToolExecution already exists for this ToolCallId (created by tool.execution_start)
		// If so, update it in-place rather than creating a duplicate entry
		List<ThinkingEventModel> existingEvents = session.ActiveWorkingGroup.GetEventsSnapshot();
		ToolExecutionModel? existingExec = existingEvents
			.Where(e => e.Type == ThinkingEventTypeEnum.Tool && e.Tool is not null)
			.Select(e => e.Tool!)
			.FirstOrDefault(t => t.ToolCallId == evt.Data.ToolCallId);

		if(existingExec is not null)
		{
			existingExec.ToolName = evt.Data.AgentDisplayName ?? existingExec.ToolName;
			existingExec.AddRawEvent(new Lazy<string>(() => SessionEventHelpers.SerializeEvent(evt)));
		}
		else
		{
			ToolExecutionModel subagentExec = new()
			{
				ToolName = evt.Data.AgentDisplayName ?? string.Empty,
				ToolCallId = evt.Data.ToolCallId,
				StartTime = evt.Timestamp.LocalDateTime,
				Status = ToolStatusEnum.Running
			};

			subagentExec.AddRawEvent(new Lazy<string>(() => SessionEventHelpers.SerializeEvent(evt)));

			session.ActiveWorkingGroup.AddEvent(new ThinkingEventModel
			{
				Type = ThinkingEventTypeEnum.Tool,
				Tool = subagentExec,
				Timestamp = evt.Timestamp.LocalDateTime
			});
		}
	}
}
