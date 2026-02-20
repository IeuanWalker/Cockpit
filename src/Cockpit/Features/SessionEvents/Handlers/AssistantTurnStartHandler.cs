using System.Diagnostics;
using Cockpit.Features.SessionEvents.Models;
using Cockpit.Models;

namespace Cockpit.Features.SessionEvents.Handlers;

static class AssistantTurnStartHandler
{
	internal static void Handle(ChatSession session)
	{
		Debug.WriteLine("AssistantTurnStartHandler");
		session.Status = SessionStatus.Running;

		// Activate the oldest pending user message (the one the agent is now processing)
		ChatMessageModel? pendingMsg = session.Messages.FirstOrDefault(m => m.IsUser && m.IsPending);
		if(pendingMsg is not null)
		{
			pendingMsg.IsPending = false;
		}

		// Create working group immediately so the panel shows while the model thinks
		// (including extended/opaque reasoning where no tool events are emitted)
		session.ActiveWorkingGroup ??= new ActivityGroupModel
		{
			StartTime = DateTime.Now,
			Status = GroupStatusEnum.Running,
			IsExpanded = true
		};
	}
}
