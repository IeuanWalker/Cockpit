using Cockpit.Features.SessionEvents.Models;
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

		List<ThinkingEventModel> events = session.ActiveWorkingGroup.GetEventsSnapshot();
		ToolExecutionModel? subagentExec = events
			.Where(e => e.Type == ThinkingEventTypeEnum.Tool && e.Tool is not null)
			.Select(e => e.Tool!)
			.FirstOrDefault(t => t.ToolCallId == evt.Data.ToolCallId);

		if(subagentExec is not null)
		{
			subagentExec.Status = ToolStatusEnum.Success;
			subagentExec.IsSuccess = true;
			subagentExec.EndTime = DateTime.Now;
			subagentExec.AddRawEvent(new Lazy<string>(() => SessionEventHelpers.SerializeEvent(evt)));
		}
	}
}
