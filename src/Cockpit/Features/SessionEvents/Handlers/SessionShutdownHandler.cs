using Cockpit.Features.SessionEvents.Models.Enums;
using Cockpit.Models;
using GitHub.Copilot.SDK;
using Microsoft.Extensions.Logging;

namespace Cockpit.Features.SessionEvents.Handlers;

static class SessionShutdownHandler
{
	internal static void Handle(ChatSession session, SessionShutdownEvent evt, ILogger logger)
	{
		logger.LogInformation("Session {SessionId} shutdown — type: {ShutdownType}, requests: {Requests}, duration: {Duration}ms",
			session.Id, evt.Data.ShutdownType, evt.Data.TotalPremiumRequests, evt.Data.TotalApiDurationMs);

		if(session.ActiveWorkingGroup is not null)
		{
			session.ActiveWorkingGroup.Status = GroupStatusEnum.Complete;
			session.ActiveWorkingGroup.EndTime = DateTime.Now;
			session.ActiveWorkingGroup = null;
		}

		session.Status = SessionStatus.Idle;
	}
}
