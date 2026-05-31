using Cockpit.Features.SessionEvents.Models;
using Cockpit.Features.Sessions.Models;
using GitHub.Copilot;

namespace Cockpit.Features.SessionEvents.Handlers;

static class ToolPartialResultHandler
{
	internal static void Handle(SessionModel session, ToolExecutionPartialResultEvent evt)
	{
		if(session.ActiveWorkingGroup is null)
		{
			return;
		}

		ToolExecutionModel? toolExec = SessionEventHelpers.FindToolExecution(session.ActiveWorkingGroup, evt.Data.ToolCallId);

		toolExec?.Output = (toolExec.Output ?? string.Empty) + evt.Data.PartialOutput;
	}
}
