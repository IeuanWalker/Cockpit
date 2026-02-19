using Cockpit.Models;
using GitHub.Copilot.SDK;

namespace Cockpit.Features.SessionEvents.Handlers;

static class ToolProgressHandler
{
	internal static void Handle(ChatSession session, ToolExecutionProgressEvent evt)
	{
		if(session.ActiveWorkingGroup is null)
		{
			return;
		}

		ToolExecution? toolExec = SessionEventHelpers.FindToolExecution(session.ActiveWorkingGroup, evt.Data.ToolCallId);

		if(toolExec is not null)
		{
			toolExec.ProgressMessage = evt.Data.ProgressMessage;
			toolExec.RawEvents.Add(new Lazy<string>(() => SessionEventHelpers.SerializeEvent(evt)));
		}
	}
}
