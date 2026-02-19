using Cockpit.Models;
using GitHub.Copilot.SDK;

namespace Cockpit.Features.SessionEvents.Handlers;

static class ToolPartialResultHandler
{
	internal static void Handle(ChatSession session, ToolExecutionPartialResultEvent evt)
	{
		if(session.ActiveWorkingGroup is null)
		{
			return;
		}

		ToolExecution? toolExec = SessionEventHelpers.FindToolExecution(session.ActiveWorkingGroup, evt.Data.ToolCallId);

		if(toolExec is not null)
		{
			toolExec.Output = (toolExec.Output ?? string.Empty) + evt.Data.PartialOutput;
		}
	}
}
