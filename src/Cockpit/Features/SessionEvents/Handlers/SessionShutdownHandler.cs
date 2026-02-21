using Cockpit.Features.Sessions.Models;
using GitHub.Copilot.SDK;
using Microsoft.Extensions.Logging;

namespace Cockpit.Features.SessionEvents.Handlers;

static class SessionShutdownHandler
{
	internal static void Handle(SessionModel session, SessionShutdownEvent evt, ILogger logger)
	{
		logger.LogInformation("Session {SessionId} shutdown — type: {ShutdownType}, requests: {Requests}, duration: {Duration}ms",
			session.Id, evt.Data.ShutdownType, evt.Data.TotalPremiumRequests, evt.Data.TotalApiDurationMs);

		SessionIdleHandler.Handle(session, DateTimeOffset.Now);
	}
}
