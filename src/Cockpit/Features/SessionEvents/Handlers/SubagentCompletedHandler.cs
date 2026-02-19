using Cockpit.Models;
using GitHub.Copilot.SDK;

namespace Cockpit.Features.SessionEvents.Handlers;

static class SubagentCompletedHandler
{
	internal static void Handle(ChatSession session, SubagentCompletedEvent evt)
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
			subagentExec.AddRawEvent(new Lazy<string>(() => SessionEventHelpers.SerializeEvent(evt)));
		}
	}
}
