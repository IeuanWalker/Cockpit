using Cockpit.Features.SessionEvents.Models;
using Cockpit.Features.Sessions.Models;
using GitHub.Copilot.SDK;

namespace Cockpit.Features.SessionEvents.Handlers;

static class AssistantReasoningDeltaHandler
{
	/// <summary>
	/// Handles a streaming reasoning-token delta, accumulating content into a
	/// <see cref="ThinkingEventModel"/> in the active working group so that reasoning
	/// is visible live as it streams rather than only once the full block arrives.
	/// </summary>
	internal static void Handle(SessionModel session, AssistantReasoningDeltaEvent evt)
	{
		if(evt.Data is null)
		{
			return;
		}

		string deltaContent = evt.Data.DeltaContent ?? string.Empty;
		if(string.IsNullOrEmpty(deltaContent))
		{
			return;
		}

		if(session.ActiveWorkingGroup is null || session.ActiveWorkingGroup.Status != GroupStatusEnum.Running)
		{
			return;
		}

		string key = evt.Data.ReasoningId is not null ? $"reasoning-{evt.Data.ReasoningId}" : "reasoning";

		if(!session.StreamingThinkingEvents.TryGetValue(key, out ThinkingEventModel? thinkingEvent))
		{
			thinkingEvent = new ThinkingEventModel
			{
				Id = key,
				Type = ThinkingEventTypeEnum.Reasoning,
				Message = string.Empty,
				Timestamp = evt.Timestamp.LocalDateTime,
				EventJson = []
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

			session.StreamingThinkingEvents[key] = thinkingEvent;
		}

		thinkingEvent.Message += deltaContent;
		thinkingEvent.EventJson?.Add(new Lazy<string>(() => SessionEventHelpers.SerializeEvent(evt)));
	}
}
