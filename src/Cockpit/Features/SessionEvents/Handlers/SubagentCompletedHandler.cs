using Cockpit.Features.SessionEvents.Models;
using Cockpit.Features.Sessions.Models;
using GitHub.Copilot.SDK;

namespace Cockpit.Features.SessionEvents.Handlers;

static class SubagentCompletedHandler
{
	internal static void Handle(SessionModel session, SubagentCompletedEvent evt)
	{
		if(session.ActiveWorkingGroup is null)
		{
			return;
		}

		ToolExecutionModel? subagentExec = SessionEventHelpers.FindToolExecution(session.ActiveWorkingGroup, evt.Data.ToolCallId);

		if(subagentExec is not null)
		{
			subagentExec.Status = ToolStatusEnum.Success;
			subagentExec.IsSuccess = true;
			subagentExec.EndTime = evt.Timestamp.LocalDateTime;
			subagentExec.AddRawEvent(new Lazy<string>(() => SessionEventHelpers.SerializeEvent(evt)));
		}
	}
}
