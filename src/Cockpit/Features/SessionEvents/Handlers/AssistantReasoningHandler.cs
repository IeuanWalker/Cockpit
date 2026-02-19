using System.Diagnostics;
using Cockpit.Models;
using GitHub.Copilot.SDK;

namespace Cockpit.Features.SessionEvents.Handlers;

static class AssistantReasoningHandler
{
	internal static void Handle(ChatSession session, AssistantReasoningEvent evt)
	{
		Debug.WriteLine("AssistantReasoningHandler");
		Debug.WriteLine(evt);

		if(evt.Data is null)
		{
			return;
		}

		string messageId = "reasoning";

		if(session.StreamingMessages.TryGetValue(messageId, out ChatMessage? existingMessage))
		{
			existingMessage.ReasoningContent = evt.Data.Content ?? string.Empty;
			existingMessage.IsStreaming = false;
			existingMessage.IsComplete = true;
			session.StreamingMessages.Remove(messageId);
		}
	}
}
