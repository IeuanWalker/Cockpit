using Cockpit.Features.Sessions.Models;
using GitHub.Copilot;
using Microsoft.Extensions.Logging;

namespace Cockpit.Features.SessionEvents.Handlers;

static class SessionShutdownHandler
{
	internal static void Handle(SessionModel session, SessionShutdownEvent evt, ILogger logger)
	{
		logger.LogInformation("Session {SessionId} shutdown — type: {ShutdownType}, duration: {Duration}ms",
			session.Id, evt.Data.ShutdownType, (long)evt.Data.TotalApiDuration.TotalMilliseconds);

		// Routine shutdowns are auto-restarts — the session continues, so we must not
		// finalise the active working group. Only Error shutdowns end the session for real.
		if(evt.Data.ShutdownType != ShutdownType.Routine)
		{
			SessionIdleHandler.Handle(session, evt.Timestamp);
		}
	}
}
