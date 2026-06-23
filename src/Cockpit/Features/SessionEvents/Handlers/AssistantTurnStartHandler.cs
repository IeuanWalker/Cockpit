using Cockpit.Features.SessionEvents.Models;
using Cockpit.Features.Sessions.Models;
using GitHub.Copilot;

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
			activatedPendingMsg?.IsPending = false;
		}

		// Create working group immediately so the panel shows while the model thinks
		// (including extended/opaque reasoning where no tool events are emitted).
		// If a pending message was just consumed, use its Id as the anchor. Otherwise fall back
		// to the last user message in the list — this covers the case where the first user message
		// was sent while the agent was idle (never pending).
		string? triggeredById = activatedPendingMsg?.Id ?? session.Messages.LastOrDefault(m => m.IsUser)?.Id;

		// Replace a placeholder group (kept alive between turns) OR create a fresh group when
		// none exists. An existing real group is left in place (multi-turn within same session).
		// When there IS a real group (immediate turn started before the prior idle/safety-net fired),
		// leave HasQueuedImmediateMessage alone — SessionIdleHandler will consume it when it finalizes
		// the old group and decides whether to keep the session running.
		if(session.ActiveWorkingGroup is null || session.ActiveWorkingGroup.IsPlaceholder)
		{
			session.ActiveWorkingGroup = new ActivityGroupModel
			{
				StartTime = evt.Timestamp.LocalDateTime,
				Status = GroupStatusEnum.Running,
				IsExpanded = true,
				TriggeredByUserMessageId = triggeredById
			};

			// Absorb any text messages that leaked into chat before this turn started.
			// These are messages the agent streamed after the safety net closed the prior group
			// but before this turn_start fired (so no active group existed to capture them).
			// Only messages explicitly flagged as leaked (IsLeakedPreGroupMessage) are absorbed —
			// this avoids mistakenly swallowing legitimate prior-turn response messages.
			if(!string.IsNullOrEmpty(triggeredById))
			{
				int anchorIndex = session.Messages.FindIndex(m => m.Id == triggeredById);
				if(anchorIndex >= 0)
				{
					for(int i = anchorIndex + 1; i < session.Messages.Count;)
					{
						ChatMessageModel m = session.Messages[i];
						if(!m.IsUser && m.Type == MessageTypeEnum.Text && m.IsLeakedPreGroupMessage)
						{
							session.ActiveWorkingGroup.AddEvent(new ThinkingEventModel
							{
								Id = m.Id,
								Type = ThinkingEventTypeEnum.Message,
								Message = m.Content,
								Timestamp = m.Timestamp.LocalDateTime,
								EventJson = m.EventJson
							});
							session.Messages.RemoveAt(i);
							if(m.Id is not null)
							{
								session.StreamingMessages.Remove(m.Id);
							}
						}
						else
						{
							i++;
						}
					}
				}
			}
		}
	}
}
