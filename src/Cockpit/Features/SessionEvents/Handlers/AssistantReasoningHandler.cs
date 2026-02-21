using Cockpit.Features.SessionEvents.Models;
using Cockpit.Features.Sessions.Models;
using GitHub.Copilot.SDK;

namespace Cockpit.Features.SessionEvents.Handlers;

static class AssistantReasoningHandler
{
	internal static void Handle(SessionModel session, AssistantReasoningEvent evt)
	{
		if(evt.Data is null)
		{
			return;
		}

		string messageId = "reasoning";

		if(session.StreamingMessages.TryGetValue(messageId, out ChatMessageModel? existingMessage))
		{
			existingMessage.ReasoningContent = evt.Data.Content ?? string.Empty;
			existingMessage.IsStreaming = false;
			existingMessage.IsComplete = true;
			session.StreamingMessages.Remove(messageId);
		}
	}
}
