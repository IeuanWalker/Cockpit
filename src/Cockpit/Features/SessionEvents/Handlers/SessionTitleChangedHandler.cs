using Cockpit.Features.Sessions.Models;
using GitHub.Copilot.SDK;

namespace Cockpit.Features.SessionEvents.Handlers;

static class SessionTitleChangedHandler
{
	internal static void Handle(SessionModel session, SessionTitleChangedEvent evt)
	{
		if(evt.Data is null)
		{
			return;
		}

		session.Title = evt.Data.Title;
	}
}
