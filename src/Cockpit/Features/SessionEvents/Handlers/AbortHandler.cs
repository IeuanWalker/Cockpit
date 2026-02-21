using Cockpit.Features.SessionEvents.Models;
using Cockpit.Features.Sessions.Models;
using GitHub.Copilot.SDK;
using Microsoft.Extensions.Logging;

namespace Cockpit.Features.SessionEvents.Handlers;

static class AbortHandler
{
	internal static void Handle(SessionModel session, AbortEvent evt, ILogger logger)
	{
		SessionIdleHandler.Handle(session, DateTimeOffset.Now, null, GroupStatusEnum.Error);

		// Clear any pending messages — they will never be processed after an abort
		foreach(ChatMessageModel msg in session.Messages.Where(m => m.IsPending))
		{
			msg.IsPending = false;
		}
	}
}
