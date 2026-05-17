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
		string? turnId = evt.Data.TurnId;
		bool shouldActivatePendingMessage = string.IsNullOrEmpty(turnId) || turnId == "0";
		ChatMessageModel? activatedPendingMsg = null;
		if(shouldActivatePendingMessage)
		{
			activatedPendingMsg = session.Messages.FirstOrDefault(m => m.IsUser && m.IsPending);
			if(activatedPendingMsg is not null)
			{
				activatedPendingMsg.IsPending = false;
			}
		}

		// Create working group immediately so the panel shows while the model thinks
		// (including extended/opaque reasoning where no tool events are emitted).
		// If a pending message was just consumed, use its Id as the anchor. Otherwise fall back
		// to the last user message in the list — this covers the case where the first user message
		// was sent while the agent was idle (never pending).
		string? triggeredById = activatedPendingMsg?.Id
			?? session.Messages.LastOrDefault(m => m.IsUser)?.Id;

		if(session.ActiveWorkingGroup is null)
		{
			session.ActiveWorkingGroup = new ActivityGroupModel
			{
				StartTime = evt.Timestamp.LocalDateTime,
				Status = GroupStatusEnum.Running,
				IsExpanded = true,
				TriggeredByUserMessageId = triggeredById
			};
		}
	}
}
