using Cockpit.Features.SessionEvents.Models;
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

		List<ThinkingEventModel> events = session.ActiveWorkingGroup.GetEventsSnapshot();
		ToolExecutionModel? subagentExec = events
			.Where(e => e.Type == ThinkingEventTypeEnum.Tool && e.Tool is not null)
			.Select(e => e.Tool!)
			.FirstOrDefault(t => t.ToolCallId == evt.Data.ToolCallId);

		if(subagentExec is not null)
		{
			subagentExec.Status = ToolStatusEnum.Error;
			subagentExec.IsSuccess = false;
			subagentExec.EndTime = DateTime.Now;
			subagentExec.Output = evt.Data.Error;
			subagentExec.AddRawEvent(new Lazy<string>(() => SessionEventHelpers.SerializeEvent(evt)));
		}
	}
}
