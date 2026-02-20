using Cockpit.Features.SessionEvents.Models;
using Cockpit.Models;
using GitHub.Copilot.SDK;
using Microsoft.Extensions.Logging;

namespace Cockpit.Features.SessionEvents.Handlers;

static class AbortHandler
{
	internal static void Handle(ChatSession session, AbortEvent evt, ILogger logger)
	{
		logger.LogWarning("Session {SessionId} aborted: {Reason}", session.Id, evt.Data.Reason);

		SessionIdleHandler.Handle(session, DateTimeOffset.Now, null, GroupStatusEnum.Error);
	}
}
