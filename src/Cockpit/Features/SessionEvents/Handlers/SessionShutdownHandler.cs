using Cockpit.Features.Sessions.Models;
using GitHub.Copilot.SDK;
using Microsoft.Extensions.Logging;

namespace Cockpit.Features.SessionEvents.Handlers;

static class SessionShutdownHandler
{
	internal static void Handle(SessionModel session, SessionShutdownEvent evt, ILogger logger)
	{
		if(evt.Data is null)
		{
			return;
		}

		logger.LogInformation("Session {SessionId} shutdown — type: {ShutdownType}, requests: {Requests}, duration: {Duration}ms",
			session.Id, evt.Data.ShutdownType, evt.Data.TotalPremiumRequests, evt.Data.TotalApiDurationMs);

		// Routine shutdowns are auto-restarts — the session continues, so we must not
		// finalise the active working group. Only Error shutdowns end the session for real.
		if(evt.Data.ShutdownType != ShutdownType.Routine)
		{
			SessionIdleHandler.Handle(session, evt.Timestamp);
		}
	}
}
