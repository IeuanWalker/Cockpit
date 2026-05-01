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

		string content = evt.Data.Content ?? string.Empty;
		if(string.IsNullOrWhiteSpace(content))
		{
			return;
		}

		// Finalise any streaming delta placeholder for this reasoning block
		string messageId = evt.Data.ReasoningId is not null ? $"reasoning-{evt.Data.ReasoningId}" : "reasoning";
		session.StreamingMessages.Remove(messageId);

		if(session.ActiveWorkingGroup is null || session.ActiveWorkingGroup.Status != GroupStatusEnum.Running)
		{
			return;
		}

		// Reuse an in-progress streaming thinking event if one was created by delta events,
		// otherwise create a new completed reasoning entry.
		if(session.StreamingThinkingEvents.TryGetValue(messageId, out ThinkingEventModel? existing))
		{
			existing.Message = content;
			session.StreamingThinkingEvents.Remove(messageId);
			return;
		}

		ThinkingEventModel thinkingEvent = new()
		{
			Type = ThinkingEventTypeEnum.Reasoning,
			Message = content,
			Timestamp = evt.Timestamp.LocalDateTime,
			EventJson = [new Lazy<string>(() => SessionEventHelpers.SerializeEvent(evt))]
		};

		ToolExecutionModel? parentTool = evt.AgentId is not null
			? SessionEventHelpers.FindToolExecution(session.ActiveWorkingGroup, evt.AgentId)
			: null;

		if(parentTool is not null)
		{
			parentTool.AddChildEvent(thinkingEvent);
		}
		else
		{
			session.ActiveWorkingGroup.AddEvent(thinkingEvent);
		}
	}
}
