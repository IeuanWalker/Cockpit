using Cockpit.Features.SessionEvents.Models;
using Cockpit.Features.Sessions.Models;
using GitHub.Copilot;

namespace Cockpit.Features.SessionEvents.Handlers;

static class ToolCompleteHandler
{
	internal static void Handle(SessionModel session, ToolExecutionCompleteEvent evt)
	{
		if(session.ActiveWorkingGroup is null)
		{
			return;
		}

		ToolExecutionModel? toolExec = SessionEventHelpers.FindToolExecution(session.ActiveWorkingGroup, evt.Data.ToolCallId);

		if(toolExec is not null)
		{
			// Background sub-agent tools stay Running until subagent.completed fires —
			// their tool.execution_complete just means "agent launched", not "work done".
			if(toolExec.IsBackgroundAgent)
			{
				toolExec.AddRawEvent(new Lazy<string>(() => SessionEventHelpers.SerializeEvent(evt)));
				return;
			}

			bool success = evt.Data.Success;
			toolExec.Status = success ? ToolStatusEnum.Success : ToolStatusEnum.Error;
			toolExec.IsSuccess = success;
			toolExec.EndTime = evt.Timestamp.LocalDateTime;
			toolExec.Output = success
				? evt.Data.Result?.Content
				: evt.Data.Error?.Message ?? evt.Data.Result?.Content ?? "Tool execution failed";
			toolExec.AddRawEvent(new Lazy<string>(() => SessionEventHelpers.SerializeEvent(evt)));
		}
	}
}
