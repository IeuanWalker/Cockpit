using Cockpit.Features.SessionEvents;
using Cockpit.Features.SessionEvents.Models;
using Cockpit.Models;
using GitHub.Copilot.SDK;
using Microsoft.Extensions.Logging.Abstractions;
using Shouldly;

namespace Cockpit.UnitTests.Features.SessionEvents.Handlers;

public class UserMessageHandlerTests
{
	static readonly ModelInfo testModel = new() { Id = "test", Name = "Test Model" };
	static ChatSession CreateSession() => new() { Model = testModel };
	static SessionEventProcessor CreateProcessor() => new(NullLogger<SessionEventProcessor>.Instance);

	[Fact]
	public void Handle_AddsMessageToSession()
	{
		// Arrange
		ChatSession session = CreateSession();
		SessionEventProcessor processor = CreateProcessor();
		UserMessageEvent evt = new()
		{
			Data = new UserMessageData { Content = "Hello world" },
			Timestamp = DateTimeOffset.UtcNow
		};

		// Act
		processor.Process(session, evt);

		// Assert
		session.Messages.Count.ShouldBe(1);
		session.Messages[0].Content.ShouldBe("Hello world");
		session.Messages[0].IsUser.ShouldBeTrue();
	}

	[Fact]
	public void Handle_SetsStatusToRunning()
	{
		// Arrange
		ChatSession session = CreateSession();
		SessionEventProcessor processor = CreateProcessor();
		UserMessageEvent evt = new()
		{
			Data = new UserMessageData { Content = "Ping" },
			Timestamp = DateTimeOffset.UtcNow
		};

		// Act
		processor.Process(session, evt);

		// Assert
		session.Status.ShouldBe(SessionStatus.Running);
	}

	[Fact]
	public void Handle_MarksMessagePending_WhenAgentWasBusy()
	{
		// Arrange: session has an active working group (agent is mid-turn)
		ChatSession session = CreateSession();
		SessionEventProcessor processor = CreateProcessor();
		session.ActiveWorkingGroup = new ActivityGroupModel { Status = GroupStatusEnum.Running };

		UserMessageEvent evt = new()
		{
			Data = new UserMessageData { Content = "Second message" },
			Timestamp = DateTimeOffset.UtcNow
		};

		// Act
		processor.Process(session, evt);

		// Assert: message should be marked pending
		ChatMessageModel? msg = session.Messages.FirstOrDefault(m => m.IsUser);
		msg.ShouldNotBeNull();
		msg.IsPending.ShouldBeTrue();
	}

	[Fact]
	public void Handle_DoesNotMarkPending_WhenAgentIsIdle()
	{
		// Arrange: no active working group
		ChatSession session = CreateSession();
		SessionEventProcessor processor = CreateProcessor();

		UserMessageEvent evt = new()
		{
			Data = new UserMessageData { Content = "Normal message" },
			Timestamp = DateTimeOffset.UtcNow
		};

		// Act
		processor.Process(session, evt);

		// Assert: message should NOT be pending
		session.Messages[0].IsPending.ShouldBeFalse();
	}

	[Fact]
	public void Handle_OptimisticMessage_MarkedPending_WhenAgentWasBusy()
	{
		// Arrange: optimistic message added while agent was busy
		ChatSession session = CreateSession();
		SessionEventProcessor processor = CreateProcessor();
		session.ActiveWorkingGroup = new ActivityGroupModel { Status = GroupStatusEnum.Running };

		// Simulate optimistic add
		session.Messages.Add(new ChatMessageModel
		{
			Content = "Queued message",
			IsUser = true,
			IsComplete = false
		});

		UserMessageEvent evt = new()
		{
			Data = new UserMessageData { Content = "Queued message" },
			Timestamp = DateTimeOffset.UtcNow
		};

		// Act
		processor.Process(session, evt);

		// Assert: optimistic message confirmed and marked pending
		ChatMessageModel? msg = session.Messages.FirstOrDefault(m => m.IsUser);
		msg.ShouldNotBeNull();
		msg.IsComplete.ShouldBeTrue();
		msg.IsPending.ShouldBeTrue();
	}

	[Fact]
	public void AssistantTurnStart_ClearsPendingFromOldestMessage()
	{
		// Arrange: two pending messages
		ChatSession session = CreateSession();
		SessionEventProcessor processor = CreateProcessor();

		ChatMessageModel pending1 = new() { Content = "First", IsUser = true, IsComplete = true, IsPending = true };
		ChatMessageModel pending2 = new() { Content = "Second", IsUser = true, IsComplete = true, IsPending = true };
		session.Messages.Add(pending1);
		session.Messages.Add(pending2);

		// Act: agent starts a new turn
		processor.Process(session, new AssistantTurnStartEvent { Data = new AssistantTurnStartData { TurnId = "0" }, Timestamp = DateTimeOffset.UtcNow });

		// Assert: only the FIRST pending message is activated
		pending1.IsPending.ShouldBeFalse();
		pending2.IsPending.ShouldBeTrue();
	}

	[Fact]
	public void FullPendingFlow_SecondMessageActivatesAfterFirstTurnCompletes()
	{
		// Arrange: first user message and agent's turn is running
		ChatSession session = CreateSession();
		SessionEventProcessor processor = CreateProcessor();

		// First message
		processor.Process(session, new UserMessageEvent
		{
			Data = new UserMessageData { Content = "First message" },
			Timestamp = DateTimeOffset.UtcNow
		});

		// Agent starts working (tool runs)
		processor.Process(session, new ToolExecutionStartEvent
		{
			Data = new ToolExecutionStartData { ToolCallId = "tc1", ToolName = "read_file" },
			Timestamp = DateTimeOffset.UtcNow
		});

		// Second message arrives while agent is busy (active working group exists)
		processor.Process(session, new UserMessageEvent
		{
			Data = new UserMessageData { Content = "Second message" },
			Timestamp = DateTimeOffset.UtcNow
		});

		// Second message should be pending
		ChatMessageModel? secondMsg = session.Messages.FirstOrDefault(m => m.IsUser && m.Content == "Second message");
		secondMsg.ShouldNotBeNull();
		secondMsg.IsPending.ShouldBeTrue();

		// First turn completes
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

		// Second turn starts — pending message becomes active
		processor.Process(session, new AssistantTurnStartEvent { Data = new AssistantTurnStartData { TurnId = "0" }, Timestamp = DateTimeOffset.UtcNow });

		secondMsg.IsPending.ShouldBeFalse();

		// Activity group for first turn inserted BEFORE second message
		int activityGroupIndex = session.Messages.FindIndex(m => m.Type == MessageTypeEnum.ActivityGroup);
		int secondMsgIndex = session.Messages.IndexOf(secondMsg);
		activityGroupIndex.ShouldBeLessThan(secondMsgIndex);
	}
}
