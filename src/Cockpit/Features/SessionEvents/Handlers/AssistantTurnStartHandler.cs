using Cockpit.Features.SessionEvents.Models;
using Cockpit.Features.Sessions.Models;
using GitHub.Copilot.SDK;

namespace Cockpit.Features.SessionEvents.Handlers;

static class AssistantTurnStartHandler
{
	internal static void Handle(SessionModel session, AssistantTurnStartEvent evt)
	{
		session.Status = SessionStatusEnum.Running;

		// A single user prompt can produce multiple assistant.turn_start events ("0", "1", ...).
		// Only consume a pending message at the first turn start for that prompt.
		string? turnId = evt.Data?.TurnId;
		bool shouldActivatePendingMessage = string.IsNullOrEmpty(turnId) || turnId == "0";
		ChatMessageModel? activatedPendingMsg = null;
		if(shouldActivatePendingMessage)
		{
			activatedPendingMsg = session.Messages.FirstOrDefault(m => m.IsUser && m.IsPending);
			activatedPendingMsg?.IsPending = false;
		}

		// Create working group immediately so the panel shows while the model thinks
		// (including extended/opaque reasoning where no tool events are emitted)
		session.ActiveWorkingGroup ??= new ActivityGroupModel
		{
			StartTime = DateTime.Now,
			Status = GroupStatusEnum.Running,
			IsExpanded = true,
			TriggeredByUserMessageId = activatedPendingMsg?.Id
		};
	}
}
