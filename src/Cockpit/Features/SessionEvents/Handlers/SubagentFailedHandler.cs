using Cockpit.Models;
using GitHub.Copilot.SDK;

namespace Cockpit.Features.SessionEvents.Handlers;

static class SubagentFailedHandler
{
	internal static void Handle(ChatSession session, SubagentFailedEvent evt)
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
			subagentExec.AddRawEvent(new Lazy<string>(() => SessionEventHelpers.SerializeEvent(evt)));
		}
	}
}
