using System.Diagnostics;
using Cockpit.Models;
using GitHub.Copilot.SDK;

namespace Cockpit.Features.SessionEvents.Handlers;

static class ToolCompleteHandler
{
	internal static void Handle(ChatSession session, ToolExecutionCompleteEvent evt)
	{
		Debug.WriteLine("ToolCompleteHandler");
		Debug.WriteLine(evt);

		if(evt.Data is null || session.ActiveWorkingGroup is null)
		{
			return;
		}

		ToolExecution? toolExec = SessionEventHelpers.FindToolExecution(session.ActiveWorkingGroup, evt.Data.ToolCallId);

		if(toolExec is not null)
		{
			toolExec.Status = ToolStatus.Success;
			toolExec.IsSuccess = true;
			toolExec.EndTime = evt.Timestamp.LocalDateTime;
			toolExec.Output = evt.Data.Result?.ToString();
			toolExec.AddRawEvent(new Lazy<string>(() => SessionEventHelpers.SerializeEvent(evt)));
		}
	}
}
