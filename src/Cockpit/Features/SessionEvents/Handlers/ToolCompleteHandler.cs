using Cockpit.Features.SessionEvents.Models;
using Cockpit.Features.Sessions.Models;
using GitHub.Copilot.SDK;

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

			toolExec.Status = ToolStatusEnum.Success;
			toolExec.IsSuccess = true;
			toolExec.EndTime = evt.Timestamp.LocalDateTime;
			toolExec.Output = evt.Data.Result?.Content;
			toolExec.AddRawEvent(new Lazy<string>(() => SessionEventHelpers.SerializeEvent(evt)));
		}
	}
}
