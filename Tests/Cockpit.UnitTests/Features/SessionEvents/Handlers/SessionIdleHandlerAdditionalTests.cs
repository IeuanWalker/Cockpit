using Cockpit.Features.SessionEvents;
using Cockpit.Features.SessionEvents.Handlers;
using Cockpit.Features.SessionEvents.Models;
using Cockpit.Features.Sessions.Models;
using GitHub.Copilot;
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

	// ── suppressSummary (safety-net and error paths) ──────────────────────────

	[Fact]
	public void SafetyNet_SuppressSummary_ThinkingMessageStaysInGroup()
	{
		// When the safety net fires (agent mid-turn interrupted by a new user message),
		// the last thinking-panel message must NOT be promoted to a standalone chat message.
		// It is intermediate planning text, not a final response.
		SessionModel session = CreateSession();
		SessionEventProcessor processor = CreateProcessor();

		ChatMessageModel userMsg1 = new() { Id = "u1", IsUser = true, Content = "First task", IsComplete = true, EventJson = null };
		session.Messages.Add(userMsg1);

		processor.Process(session, new AssistantTurnStartEvent
		{
			Data = new AssistantTurnStartData { TurnId = "0" },
			Timestamp = DateTimeOffset.UtcNow
		});
		processor.Process(session, new ToolExecutionStartEvent
		{
			Data = new ToolExecutionStartData { ToolCallId = "tc1", ToolName = "read_file" },
			Timestamp = DateTimeOffset.UtcNow
		});
		processor.Process(session, new ToolExecutionCompleteEvent
		{
			Data = new ToolExecutionCompleteData { ToolCallId = "tc1", Success = true },
			Timestamp = DateTimeOffset.UtcNow
		});

		// Agent emits planning text mid-turn (before session.idle)
		session.ActiveWorkingGroup!.AddEvent(new ThinkingEventModel
		{
			Type = ThinkingEventTypeEnum.Message,
			Message = "Reading a representative set of key files...",
			Timestamp = DateTime.UtcNow,
			EventJson = null
		});

		// User interrupts mid-turn — safety net fires (wasAgentBusy=true)
		processor.Process(session, new AssistantTurnStartEvent
		{
			Data = new AssistantTurnStartData { TurnId = "2" },
			Timestamp = DateTimeOffset.UtcNow
		});
		processor.Process(session, new UserMessageEvent
		{
			Data = new UserMessageData { Content = "Actually focus" },
			Timestamp = DateTimeOffset.UtcNow
		});

		// The planning text must NOT appear as a standalone chat message
		session.Messages.ShouldNotContain(m => !m.IsUser && m.Type == MessageTypeEnum.Text && m.Content == "Reading a representative set of key files...");

		// The ops group must still be in chat (tools ran)
		session.Messages.ShouldContain(m => m.Type == MessageTypeEnum.ActivityGroup);
	}

	[Fact]
	public void Error_SuppressSummary_ThinkingMessageStaysInGroup()
	{
		// When the session errors out, intermediate planning text must NOT be promoted
		// to a standalone chat message — only the error message should appear in chat.
		SessionModel session = CreateSession();
		SessionEventProcessor processor = CreateProcessor();

		session.Messages.Add(new ChatMessageModel { IsUser = true, Content = "Do something", EventJson = null });

		processor.Process(session, new AssistantTurnStartEvent
		{
			Data = new AssistantTurnStartData { TurnId = "0" },
			Timestamp = DateTimeOffset.UtcNow
		});
		processor.Process(session, new ToolExecutionStartEvent
		{
			Data = new ToolExecutionStartData { ToolCallId = "tc1", ToolName = "bash" },
			Timestamp = DateTimeOffset.UtcNow
		});

		// Agent emits planning text mid-turn
		session.ActiveWorkingGroup!.AddEvent(new ThinkingEventModel
		{
			Type = ThinkingEventTypeEnum.Message,
			Message = "Running a glob to find all permission-related files...",
			Timestamp = DateTime.UtcNow,
			EventJson = null
		});

		// Session errors out
		processor.Process(session, new SessionErrorEvent
		{
			Data = new SessionErrorData { ErrorType = "fatal", Message = "Rate limit exceeded" },
			Timestamp = DateTimeOffset.UtcNow
		});

		// Planning text must NOT appear as a standalone chat message
		session.Messages.ShouldNotContain(m => !m.IsUser && m.Type == MessageTypeEnum.Text && m.Content == "Running a glob to find all permission-related files...");

		// Error message must still appear
		session.Messages.ShouldContain(m => m.Type == MessageTypeEnum.Error && m.Content == "Rate limit exceeded");
	}

	[Fact]
	public void SafetyNet_PendingTaskSummaryStillCleared_EvenWhenSuppressed()
	{
		// PendingTaskSummary must always be consumed (nulled out) by the safety net,
		// even when suppressSummary=true prevents it from being emitted as a message.
		// Without this, the stale summary would leak into the next turn's idle event.
		SessionModel session = CreateSession();
		SessionEventProcessor processor = CreateProcessor();

		ChatMessageModel userMsg1 = new() { Id = "u1", IsUser = true, Content = "First task", IsComplete = true, EventJson = null };
		session.Messages.Add(userMsg1);

		processor.Process(session, new AssistantTurnStartEvent
		{
			Data = new AssistantTurnStartData { TurnId = "0" },
			Timestamp = DateTimeOffset.UtcNow
		});
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

		// A task-complete fires before the interruption
		session.PendingTaskSummary = "Partial work done";

		// User interrupts — safety net fires
		processor.Process(session, new AssistantTurnStartEvent
		{
			Data = new AssistantTurnStartData { TurnId = "2" },
			Timestamp = DateTimeOffset.UtcNow
		});
		processor.Process(session, new UserMessageEvent
		{
			Data = new UserMessageData { Content = "Stop and do this instead" },
			Timestamp = DateTimeOffset.UtcNow
		});

		// PendingTaskSummary must be consumed even though summary content was suppressed
		session.PendingTaskSummary.ShouldBeNull("stale summary must be cleared to prevent it appearing in the next turn");
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
