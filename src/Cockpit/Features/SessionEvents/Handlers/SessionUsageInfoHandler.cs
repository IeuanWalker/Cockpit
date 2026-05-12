using Cockpit.Features.Sessions.Models;
using GitHub.Copilot.SDK;

namespace Cockpit.Features.SessionEvents.Handlers;

static class SessionUsageInfoHandler
{
	internal static void Handle(SessionModel session, SessionUsageInfoEvent evt)
	{
		if(evt.Data is null)
		{
			return;
		}

		session.TokenUsageInfo = new()
		{
			ConversationTokens = evt.Data.ConversationTokens,
			CurrentTokens = evt.Data.CurrentTokens,
			IsInitial = evt.Data.IsInitial,
			MessagesLength = evt.Data.MessagesLength,
			SystemTokens = evt.Data.SystemTokens,
			TokenLimit = evt.Data.TokenLimit,
			ToolDefinitionsTokens = evt.Data.ToolDefinitionsTokens
		};
	}
}
