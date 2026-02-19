using System.Diagnostics;
using Cockpit.Models;
using GitHub.Copilot.SDK;

namespace Cockpit.Features.SessionEvents.Handlers;

static class AssistantReasoningDeltaHandler
{
	internal static void Handle(ChatSession session, AssistantReasoningDeltaEvent evt)
	{
		Debug.WriteLine("AssistantReasoningDeltaHandler");
		Debug.WriteLine(evt);

		// Reasoning is only shown in the thinking panel, not in chat
	}
}
