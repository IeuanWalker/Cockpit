using System.Diagnostics;
using Cockpit.Models;

namespace Cockpit.Features.SessionEvents.Handlers;

static class AssistantTurnStartHandler
{
	internal static void Handle(ChatSession session)
	{
		Debug.WriteLine("AssistantTurnStartHandler");
		session.Status = SessionStatus.Running;
	}
}
