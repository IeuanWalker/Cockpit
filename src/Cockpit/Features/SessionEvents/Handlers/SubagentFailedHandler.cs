using Cockpit.Features.SessionEvents.Models;
using Cockpit.Features.Sessions.Models;
using GitHub.Copilot.SDK;

namespace Cockpit.Features.SessionEvents.Handlers;

static class SubagentFailedHandler
{
	internal static void Handle(SessionModel session, SubagentFailedEvent evt)
	{
		if(session.ActiveWorkingGroup is null)
		{
			return;
		}

		ToolExecutionModel? subagentExec = SessionEventHelpers.FindToolExecution(session.ActiveWorkingGroup, evt.Data.ToolCallId);

		if(subagentExec is not null)
		{
			subagentExec.Status = ToolStatusEnum.Error;
			subagentExec.IsSuccess = false;
			subagentExec.EndTime = evt.Timestamp.LocalDateTime;
			subagentExec.Output = evt.Data.Error;
			subagentExec.AddRawEvent(new Lazy<string>(() => SessionEventHelpers.SerializeEvent(evt)));
		}
	}
}
