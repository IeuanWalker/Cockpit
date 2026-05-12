using Cockpit.Features.Sessions.Models;
using GitHub.Copilot.SDK;

namespace Cockpit.Features.SessionEvents.Handlers;

static class SessionContextChangedHandler
{
	internal static void Handle(SessionModel session, SessionContextChangedEvent evt)
	{
		if(evt.Data is null)
		{
			return;
		}

		if(evt.Data.Cwd is not null)
		{
			session.Context.CurrentWorkingDirectory = evt.Data.Cwd;
		}

		if(evt.Data.GitRoot is not null)
		{
			session.Context.GitRoot = evt.Data.GitRoot;
		}

		if(evt.Data.Repository is not null)
		{
			session.Context.Repository = evt.Data.Repository;
		}

		if(evt.Data.Branch is not null)
		{
			session.Context.Branch = evt.Data.Branch;
		}
	}
}
