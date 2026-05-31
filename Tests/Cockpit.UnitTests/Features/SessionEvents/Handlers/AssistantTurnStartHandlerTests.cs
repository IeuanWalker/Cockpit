using Cockpit.Features.SessionEvents;
using Cockpit.Features.SessionEvents.Models;
using Cockpit.Features.Sessions.Models;
using GitHub.Copilot;
using Microsoft.Extensions.Logging.Abstractions;
using Shouldly;

namespace Cockpit.UnitTests.Features.SessionEvents.Handlers;

/// <summary>
/// Tests for AssistantTurnStartHandler, with particular focus on the
/// TriggeredByUserMessageId fallback that ensures working groups are correctly
/// anchored even when no pending message is consumed (e.g. first send while idle,
/// or immediate-mode session replay).
/// </summary>
public class AssistantTurnStartHandlerTests
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

	[Fact]
	public void TurnId0_NoPendingMessages_AnchorsFallbackToLastUserMessage()
	{
		// Arrange: normal first send — user message is complete, not pending (agent was idle)
		SessionModel session = CreateSession();
		SessionEventProcessor processor = CreateProcessor();
		ChatMessageModel userMsg = new() { Id = "msg-1", Content = "Do something", IsUser = true, IsComplete = true, EventJson = null };
		session.Messages.Add(userMsg);

		// Act
		processor.Process(session, new AssistantTurnStartEvent
		{
			Data = new AssistantTurnStartData { TurnId = "0" },
			Timestamp = DateTimeOffset.UtcNow
		});

		// Assert: no pending to consume — fallback anchors to the last user message
		session.ActiveWorkingGroup.ShouldNotBeNull();
		session.ActiveWorkingGroup.TriggeredByUserMessageId.ShouldBe("msg-1");
	}

	[Fact]
	public void TurnId0_WithPendingMessage_UsesConsumedPendingAsAnchor()
	{
		// Arrange: enqueue mode — message was marked pending because agent was busy at send time
		SessionModel session = CreateSession();
		SessionEventProcessor processor = CreateProcessor();
		ChatMessageModel pendingMsg = new() { Id = "pending-1", Content = "Queued", IsUser = true, IsComplete = true, IsPending = true, EventJson = null };
		session.Messages.Add(pendingMsg);

		// Act
		processor.Process(session, new AssistantTurnStartEvent
		{
			Data = new AssistantTurnStartData { TurnId = "0" },
			Timestamp = DateTimeOffset.UtcNow
		});

		// Assert: pending message consumed and used as the anchor
		session.ActiveWorkingGroup.ShouldNotBeNull();
		session.ActiveWorkingGroup.TriggeredByUserMessageId.ShouldBe("pending-1");
		pendingMsg.IsPending.ShouldBeFalse("consumed pending message must be cleared");
	}

	[Fact]
	public void NonInitialTurnId_DoesNotConsumePending_ButStillAnchorsToLastUserMessage()
	{
		// Non-initial turns (turnId != "0") must NOT consume a pending message, but the working
		// group must still be anchored to the last user message via the fallback. This is the
		// critical path for immediate-mode session replay: the SDK writes assistant.turn_start
		// (non-"0") just milliseconds before the user.message echo arrives. After the swap
		// pre-sort, the user message is in the list (with IsPending=true set by the safety net)
		// but the subsequent non-initial turn_start must still anchor to it.
		SessionModel session = CreateSession();
		SessionEventProcessor processor = CreateProcessor();
		ChatMessageModel userMsg = new() { Id = "msg-immediate", Content = "Actually focus", IsUser = true, IsComplete = true, IsPending = true, EventJson = null };
		session.Messages.Add(userMsg);

		// Act: non-initial turnId — should NOT consume pending
		processor.Process(session, new AssistantTurnStartEvent
		{
			Data = new AssistantTurnStartData { TurnId = "2" },
			Timestamp = DateTimeOffset.UtcNow
		});

		// Assert
		userMsg.IsPending.ShouldBeTrue("non-initial turn must not clear IsPending");
		session.ActiveWorkingGroup.ShouldNotBeNull();
		session.ActiveWorkingGroup.TriggeredByUserMessageId.ShouldBe("msg-immediate",
			"fallback must anchor to last user message even when IsPending=true");
	}

	[Fact]
	public void TurnId0_NoUserMessages_TriggeredByIsNull()
	{
		// Edge case: turn_start fires before any user message has been processed
		// (e.g. agent-side continuation with no user echo yet). Anchor must be null.
		SessionModel session = CreateSession();
		SessionEventProcessor processor = CreateProcessor();

		processor.Process(session, new AssistantTurnStartEvent
		{
			Data = new AssistantTurnStartData { TurnId = "0" },
			Timestamp = DateTimeOffset.UtcNow
		});

		session.ActiveWorkingGroup.ShouldNotBeNull();
		session.ActiveWorkingGroup.TriggeredByUserMessageId.ShouldBeNull();
	}

	[Fact]
	public void TurnId0_MultipleUserMessages_AnchorIsLastOne()
	{
		// When multiple non-pending messages exist, the fallback must pick the last (most recent)
		SessionModel session = CreateSession();
		SessionEventProcessor processor = CreateProcessor();
		session.Messages.Add(new ChatMessageModel { Id = "msg-1", Content = "First", IsUser = true, IsComplete = true, EventJson = null });
		session.Messages.Add(new ChatMessageModel { Id = "msg-2", Content = "Second", IsUser = true, IsComplete = true, EventJson = null });
		session.Messages.Add(new ChatMessageModel { Id = "msg-3", Content = "Third", IsUser = true, IsComplete = true, EventJson = null });

		processor.Process(session, new AssistantTurnStartEvent
		{
			Data = new AssistantTurnStartData { TurnId = "0" },
			Timestamp = DateTimeOffset.UtcNow
		});

		session.ActiveWorkingGroup!.TriggeredByUserMessageId.ShouldBe("msg-3");
	}

	[Fact]
	public void SecondTurnStart_ExistingGroupNotReplaced()
	{
		// The ??= operator must prevent replacing the existing working group when a follow-up
		// assistant.turn_start fires within the same multi-turn response
		SessionModel session = CreateSession();
		SessionEventProcessor processor = CreateProcessor();
		session.Messages.Add(new ChatMessageModel { Id = "msg-1", IsUser = true, IsComplete = true, EventJson = null });

		processor.Process(session, new AssistantTurnStartEvent
		{
			Data = new AssistantTurnStartData { TurnId = "0" },
			Timestamp = DateTimeOffset.UtcNow
		});
		ActivityGroupModel originalGroup = session.ActiveWorkingGroup!;

		processor.Process(session, new AssistantTurnStartEvent
		{
			Data = new AssistantTurnStartData { TurnId = "1" },
			Timestamp = DateTimeOffset.UtcNow
		});

		// Group must be the same object — not replaced by the follow-up turn
		session.ActiveWorkingGroup.ShouldBeSameAs(originalGroup);
	}

	[Fact]
	public void TurnId0_OnlyNonUserMessagesPresent_TriggeredByIsNull()
	{
		// Non-user messages (assistant text, activity groups) must not be picked up by the fallback
		SessionModel session = CreateSession();
		SessionEventProcessor processor = CreateProcessor();
		session.Messages.Add(new ChatMessageModel { IsUser = false, Content = "Hi there", EventJson = null });

		processor.Process(session, new AssistantTurnStartEvent
		{
			Data = new AssistantTurnStartData { TurnId = "0" },
			Timestamp = DateTimeOffset.UtcNow
		});

		session.ActiveWorkingGroup!.TriggeredByUserMessageId.ShouldBeNull(
			"non-user messages must not be used as the working group anchor");
	}
}
