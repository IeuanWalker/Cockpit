using Cockpit.Features.SessionEvents.Models.Enums;
using Cockpit.Models;
using GitHub.Copilot.SDK;
using Microsoft.Extensions.Logging;

namespace Cockpit.Features.SessionEvents.Handlers;

static class AbortHandler
{
	internal static void Handle(ChatSession session, AbortEvent evt, ILogger logger)
	{
		logger.LogWarning("Session {SessionId} aborted: {Reason}", session.Id, evt.Data.Reason);

		if(session.ActiveWorkingGroup is not null)
		{
			session.ActiveWorkingGroup.Status = GroupStatusEnum.Error;
			session.ActiveWorkingGroup.EndTime = DateTime.Now;
			session.ActiveWorkingGroup = null;
		}

		session.Status = SessionStatus.Idle;
	}
}
