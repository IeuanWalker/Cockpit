using Cockpit.Features.Sessions.Models;
using GitHub.Copilot.SDK;

namespace Cockpit.Features.SessionEvents.Handlers;

static class SessionTaskCompleteHandler
{
	/// <summary>
	/// Stores the SDK-provided task summary on the session so that
	/// <see cref="SessionIdleHandler"/> can use it as the preferred summary source
	/// instead of heuristically extracting the last thinking-panel message.
	/// </summary>
	internal static void Handle(SessionModel session, SessionTaskCompleteEvent evt)
	{
		if(string.IsNullOrWhiteSpace(evt.Data.Summary))
		{
			return;
		}

		session.PendingTaskSummary = evt.Data.Summary;
	}
}
