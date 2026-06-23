using Cockpit.Features.SessionEvents.Models;
using Cockpit.Features.Sessions.Models;
using GitHub.Copilot;

namespace Cockpit.Features.SessionEvents.Handlers;

/// <summary>
/// Handles agent-synthesised <c>thinking-exhausted-continuation</c> user messages.
/// These are internal SDK signals — not real user input — so they must not appear in the
/// chat log and must not close the working panel or trigger the completion sound.
/// </summary>
static class ThinkingExhaustedContinuationHandler
{
	internal static void Handle(SessionModel session, UserMessageEvent evt)
	{
		// Keep the existing working group alive, or open a new one if none is active,
		// so the working panel stays visible while the agent continues.
		session.ActiveWorkingGroup ??= new ActivityGroupModel
		{
			StartTime = evt.Timestamp.LocalDateTime,
			Status = GroupStatusEnum.Running,
			IsExpanded = true,
			TriggeredByUserMessageId = session.Messages.LastOrDefault(m => m.IsUser)?.Id
		};

		session.Status = SessionStatusEnum.Running;
	}
}
