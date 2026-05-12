using Cockpit.Features.SessionEvents;
using Cockpit.Features.SessionEvents.Handlers;
using Cockpit.Features.SessionEvents.Models;
using Cockpit.Features.Sessions.Models;
using GitHub.Copilot.SDK;
using Microsoft.Extensions.Logging.Abstractions;
using Shouldly;

namespace Cockpit.UnitTests.Features.SessionEvents.Handlers;

/// <summary>
/// Additional tests for <c>SessionIdleHandler</c> covering <c>PendingTaskSummary</c> consumption,
/// the <see cref="SessionIdleHandler.OnSessionFinished"/> event, and
/// <see cref="SessionModel.SuppressFinishedNotification"/>.
/// </summary>
[Collection("SessionIdleEvent")]
public class SessionIdleHandlerAdditionalTests
{
	static readonly ModelInfo testModel = new() { Id = "test", Name = "Test Model" };
	static SessionModel CreateSession() => new()
	{
		Id = "sessionId",
		Title = "Test Session",
		CreatedAt = DateTime.UtcNow,
		LastActivity = DateTime.UtcNow,
		Model = testModel,
		Context = new()
		{
			CurrentWorkingDirectory = "",
			WorkspacePath = null,
			GitRoot = null,
			Branch = null,
			Repository = null
		}
	};
	static SessionEventProcessor CreateProcessor() => new(NullLogger<SessionEventProcessor>.Instance);

	// ── PendingTaskSummary ────────────────────────────────────────────────────

	[Fact]
	public void Handle_WithPendingTaskSummary_ClearsItAfterConsumption()
	{
		// PendingTaskSummary must be consumed (set to null) once session.idle fires
		SessionModel session = CreateSession();
		SessionEventProcessor processor = CreateProcessor();
		session.Messages.Add(new ChatMessageModel { IsUser = true, EventJson = null });
		session.PendingTaskSummary = "Task done!";

		processor.Process(session, new ToolExecutionStartEvent
		{
			Data = new ToolExecutionStartData { ToolCallId = "tc1", ToolName = "bash" },
			Timestamp = DateTimeOffset.UtcNow
		});
		processor.Process(session, new ToolExecutionCompleteEvent
		{
			Data = new ToolExecutionCompleteData { ToolCallId = "tc1", Success = true },
			Timestamp = DateTimeOffset.UtcNow
		});

		processor.Process(session, new SessionIdleEvent
		{
			Data = new SessionIdleData(),
			Timestamp = DateTimeOffset.UtcNow
		});

		session.PendingTaskSummary.ShouldBeNull();
	}

	[Fact]
	public void Handle_WithPendingTaskSummary_CreatesSummaryMessageFromIt()
	{
		// When PendingTaskSummary is set, the idle handler must insert a summary ChatMessageModel
		// whose content comes from that value (set immediately because no onStreamSummary delegate)
		SessionModel session = CreateSession();
		SessionEventProcessor processor = CreateProcessor();
		session.Messages.Add(new ChatMessageModel { IsUser = true, EventJson = null });
		session.PendingTaskSummary = "Here is the summary";

		processor.Process(session, new ToolExecutionStartEvent
		{
			Data = new ToolExecutionStartData { ToolCallId = "tc1", ToolName = "bash" },
			Timestamp = DateTimeOffset.UtcNow
		});
		processor.Process(session, new ToolExecutionCompleteEvent
		{
			Data = new ToolExecutionCompleteData { ToolCallId = "tc1", Success = true },
			Timestamp = DateTimeOffset.UtcNow
		});

		processor.Process(session, new SessionIdleEvent
		{
			Data = new SessionIdleData(),
			Timestamp = DateTimeOffset.UtcNow
		});

		// Without onStreamSummary, the content is set immediately (background/non-visible path)
		session.Messages.ShouldContain(m => !m.IsUser && m.Content == "Here is the summary");
	}

	[Fact]
	public void Handle_LastMessageEvent_UsedAsSummaryWhenNoPendingTaskSummary()
	{
		// When PendingTaskSummary is null, the last ThinkingEventModel of type Message is promoted
		// to a summary ChatMessageModel and removed from the group's events
		SessionModel session = CreateSession();
		SessionEventProcessor processor = CreateProcessor();
		session.Messages.Add(new ChatMessageModel { IsUser = true, EventJson = null });

		processor.Process(session, new ToolExecutionStartEvent
		{
			Data = new ToolExecutionStartData { ToolCallId = "tc1", ToolName = "bash" },
			Timestamp = DateTimeOffset.UtcNow
		});
		processor.Process(session, new ToolExecutionCompleteEvent
		{
			Data = new ToolExecutionCompleteData { ToolCallId = "tc1", Success = true },
			Timestamp = DateTimeOffset.UtcNow
		});

		// Add a message event to the group directly (simulating an AssistantMessageDeltaEvent that fired during the tool)
		session.ActiveWorkingGroup!.AddEvent(new ThinkingEventModel
		{
			Type = ThinkingEventTypeEnum.Message,
			Message = "Extracted summary",
			Timestamp = DateTime.UtcNow,
			EventJson = null
		});

		processor.Process(session, new SessionIdleEvent
		{
			Data = new SessionIdleData(),
			Timestamp = DateTimeOffset.UtcNow
		});

		// The last message event should be promoted to a standalone chat message
		session.Messages.ShouldContain(m => !m.IsUser && m.Content == "Extracted summary");
	}

	// ── OnSessionFinished event ───────────────────────────────────────────────

	[Fact]
	public void Handle_OnSessionFinished_FiredWhenComplete()
	{
		// OnSessionFinished must fire when the session transitions to Idle with a Complete result
		SessionModel session = CreateSession();
		SessionEventProcessor processor = CreateProcessor();
		bool eventFired = false;
		void handler() => eventFired = true;
		SessionIdleHandler.OnSessionFinished += handler;

		try
		{
			processor.Process(session, new SessionIdleEvent
			{
				Data = new SessionIdleData(),
				Timestamp = DateTimeOffset.UtcNow
			});

			eventFired.ShouldBeTrue();
		}
		finally
		{
			SessionIdleHandler.OnSessionFinished -= handler;
		}
	}

	[Fact]
	public void Handle_SuppressFinishedNotification_PreventsOnSessionFinishedFiring()
	{
		// When SuppressFinishedNotification is set (e.g. during session-history replay),
		// OnSessionFinished must NOT fire to avoid spurious completion notifications
		SessionModel session = CreateSession();
		session.SuppressFinishedNotification = true;
		SessionEventProcessor processor = CreateProcessor();
		bool eventFired = false;
		void handler() => eventFired = true;
		SessionIdleHandler.OnSessionFinished += handler;

		try
		{
			processor.Process(session, new SessionIdleEvent
			{
				Data = new SessionIdleData(),
				Timestamp = DateTimeOffset.UtcNow
			});

			eventFired.ShouldBeFalse();
		}
		finally
		{
			SessionIdleHandler.OnSessionFinished -= handler;
		}
	}
}
