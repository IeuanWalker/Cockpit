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
		// to the last user message in the list — this covers the cases where:
		//   (a) the very first user message was sent while the agent was idle (never pending), or
		//   (b) during session replay, an immediate-mode user.message arrives after turn_start and
		//       is created with IsPending=true, which would otherwise be skipped by the anchor
		//       fallback in SessionIdleHandler (which filters out IsPending messages).
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

			// When an enqueued/pending message triggers a new working group, embed it in the
			// group so the working panel shows what the user asked.
			if(activatedPendingMsg is not null)
			{
				session.ActiveWorkingGroup.AddEvent(new ThinkingEventModel
				{
					Type = ThinkingEventTypeEnum.UserMessage,
					Message = activatedPendingMsg.Content,
					Timestamp = activatedPendingMsg.Timestamp.LocalDateTime,
					EventJson = null
				});
			}
		}
	}
}
