using Cockpit.Features.SessionEvents;
using Cockpit.Features.SessionEvents.Models;
using Cockpit.Features.Sessions.Models;
using GitHub.Copilot.SDK;
using Microsoft.Extensions.Logging.Abstractions;
using Shouldly;

namespace Cockpit.UnitTests.Features.SessionEvents.Handlers;

public class AssistantReasoningHandlerTests
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

	// ── AssistantReasoningDeltaHandler ────────────────────────────────────────

	[Fact]
	public void ReasoningDelta_DuringActiveGroup_AccumulatesInThinkingPanel()
	{
		// Arrange
		SessionModel session = CreateSession();
		SessionEventProcessor processor = CreateProcessor();

		// Open a working group
		processor.Process(session, new ToolExecutionStartEvent
		{
			Data = new ToolExecutionStartData { ToolCallId = "tc1", ToolName = "bash" },
			Timestamp = DateTimeOffset.UtcNow
		});

		// Act — two reasoning deltas
		processor.Process(session, new AssistantReasoningDeltaEvent
		{
			Data = new AssistantReasoningDeltaData { ReasoningId = "r1", DeltaContent = "I think " },
			Timestamp = DateTimeOffset.UtcNow
		});
		processor.Process(session, new AssistantReasoningDeltaEvent
		{
			Data = new AssistantReasoningDeltaData { ReasoningId = "r1", DeltaContent = "carefully." },
			Timestamp = DateTimeOffset.UtcNow
		});

		// Assert — single reasoning event in the group with accumulated content
		List<ThinkingEventModel> events = session.ActiveWorkingGroup!.GetEventsSnapshot();
		ThinkingEventModel? reasoningEvt = events.FirstOrDefault(e => e.Type == ThinkingEventTypeEnum.Reasoning);
		reasoningEvt.ShouldNotBeNull();
		reasoningEvt!.Message.ShouldBe("I think carefully.");
	}

	[Fact]
	public void ReasoningDelta_WithNoActiveGroup_IsIgnored()
	{
		SessionModel session = CreateSession();
		SessionEventProcessor processor = CreateProcessor();

		Should.NotThrow(() => processor.Process(session, new AssistantReasoningDeltaEvent
		{
			Data = new AssistantReasoningDeltaData { ReasoningId = "r1", DeltaContent = "thinking..." },
			Timestamp = DateTimeOffset.UtcNow
		}));

		session.Messages.ShouldBeEmpty();
	}

	[Fact]
	public void ReasoningDelta_NullData_IsIgnored()
	{
		SessionModel session = CreateSession();
		SessionEventProcessor processor = CreateProcessor();
		session.ActiveWorkingGroup = new ActivityGroupModel { Status = GroupStatusEnum.Running };

		Should.NotThrow(() => processor.Process(session, new AssistantReasoningDeltaEvent
		{
			Data = null!,
			Timestamp = DateTimeOffset.UtcNow
		}));
	}

	[Fact]
	public void ReasoningDelta_EmptyDelta_IsIgnored()
	{
		SessionModel session = CreateSession();
		SessionEventProcessor processor = CreateProcessor();
		session.ActiveWorkingGroup = new ActivityGroupModel { Status = GroupStatusEnum.Running };

		processor.Process(session, new AssistantReasoningDeltaEvent
		{
			Data = new AssistantReasoningDeltaData { ReasoningId = "r1", DeltaContent = string.Empty },
			Timestamp = DateTimeOffset.UtcNow
		});

		// No events added for empty delta
		session.ActiveWorkingGroup!.GetEventsSnapshot().ShouldBeEmpty();
	}

	// ── AssistantReasoningHandler ─────────────────────────────────────────────

	[Fact]
	public void ReasoningComplete_UpdatesStreamingThinkingEvent()
	{
		// Arrange — deltas already built a streaming thinking event
		SessionModel session = CreateSession();
		SessionEventProcessor processor = CreateProcessor();

		processor.Process(session, new ToolExecutionStartEvent
		{
			Data = new ToolExecutionStartData { ToolCallId = "tc1", ToolName = "bash" },
			Timestamp = DateTimeOffset.UtcNow
		});
		processor.Process(session, new AssistantReasoningDeltaEvent
		{
			Data = new AssistantReasoningDeltaData { ReasoningId = "r1", DeltaContent = "partial..." },
			Timestamp = DateTimeOffset.UtcNow
		});

		// Act — complete event overrides the delta content with the canonical text
		processor.Process(session, new AssistantReasoningEvent
		{
			Data = new AssistantReasoningData { ReasoningId = "r1", Content = "Full reasoning text." },
			Timestamp = DateTimeOffset.UtcNow
		});

		// Assert — the in-place thinking event has the final content
		ThinkingEventModel? reasoningEvt = session.ActiveWorkingGroup!.GetEventsSnapshot()
			.FirstOrDefault(e => e.Type == ThinkingEventTypeEnum.Reasoning);
		reasoningEvt.ShouldNotBeNull();
		reasoningEvt!.Message.ShouldBe("Full reasoning text.");

		// StreamingThinkingEvents cleared after completion
		session.StreamingThinkingEvents.ShouldNotContainKey("reasoning-r1");
	}

	[Fact]
	public void ReasoningComplete_WithNoActiveGroup_IsIgnored()
	{
		SessionModel session = CreateSession();
		SessionEventProcessor processor = CreateProcessor();

		Should.NotThrow(() => processor.Process(session, new AssistantReasoningEvent
		{
			Data = new AssistantReasoningData { ReasoningId = "r1", Content = "Some reasoning." },
			Timestamp = DateTimeOffset.UtcNow
		}));
	}

	[Fact]
	public void ReasoningComplete_NullData_IsIgnored()
	{
		SessionModel session = CreateSession();
		SessionEventProcessor processor = CreateProcessor();
		session.ActiveWorkingGroup = new ActivityGroupModel { Status = GroupStatusEnum.Running };

		Should.NotThrow(() => processor.Process(session, new AssistantReasoningEvent
		{
			Data = null!,
			Timestamp = DateTimeOffset.UtcNow
		}));
	}

	[Fact]
	public void ReasoningComplete_NoDeltasFirst_CreatesNewReasoningEvent()
	{
		// When the complete event arrives without prior delta events (e.g. background session replay),
		// a new ThinkingEventModel is created directly.
		SessionModel session = CreateSession();
		SessionEventProcessor processor = CreateProcessor();

		processor.Process(session, new ToolExecutionStartEvent
		{
			Data = new ToolExecutionStartData { ToolCallId = "tc1", ToolName = "bash" },
			Timestamp = DateTimeOffset.UtcNow
		});

		processor.Process(session, new AssistantReasoningEvent
		{
			Data = new AssistantReasoningData { ReasoningId = "r1", Content = "Direct reasoning." },
			Timestamp = DateTimeOffset.UtcNow
		});

		ThinkingEventModel? reasoningEvt = session.ActiveWorkingGroup!.GetEventsSnapshot()
			.FirstOrDefault(e => e.Type == ThinkingEventTypeEnum.Reasoning);
		reasoningEvt.ShouldNotBeNull();
		reasoningEvt!.Message.ShouldBe("Direct reasoning.");
	}
}
