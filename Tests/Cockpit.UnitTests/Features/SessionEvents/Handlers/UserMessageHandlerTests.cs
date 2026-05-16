using Cockpit.Features.SessionEvents;
using Cockpit.Features.SessionEvents.Handlers;
using Cockpit.Features.SessionEvents.Models;
using Cockpit.Features.Sessions.Models;
using GitHub.Copilot.SDK;
using Microsoft.Extensions.Logging.Abstractions;
using Shouldly;

namespace Cockpit.UnitTests.Features.SessionEvents.Handlers;

[Collection("SessionIdleEvent")]
public class UserMessageHandlerTests
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
	public void Handle_AddsMessageToSession()
	{
		// Arrange
		SessionModel session = CreateSession();
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
		SessionModel session = CreateSession();
		SessionEventProcessor processor = CreateProcessor();
		UserMessageEvent evt = new()
		{
			Data = new UserMessageData { Content = "Ping" },
			Timestamp = DateTimeOffset.UtcNow
		};

		// Act
		processor.Process(session, evt);

		// Assert
		session.Status.ShouldBe(SessionStatusEnum.Running);
	}

	[Fact]
	public void Handle_MarksMessagePending_WhenAgentWasBusy()
	{
		// Arrange: session has an active working group with substantive work (agent is mid-turn)
		SessionModel session = CreateSession();
		SessionEventProcessor processor = CreateProcessor();
		ActivityGroupModel group = new() { Status = GroupStatusEnum.Running };
		group.AddEvent(new ThinkingEventModel
		{
			Type = ThinkingEventTypeEnum.Tool,
			Tool = new ToolExecutionModel
			{
				ToolName = "read_file",
				ToolCallId = "tc1",
				Status = ToolStatusEnum.Running,
				StartTime = DateTime.Now
			},
			EventJson = null
		});
		session.ActiveWorkingGroup = group;

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
		SessionModel session = CreateSession();
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
	public void Handle_OptimisticMessage_MarkedPending_WhenEnqueueModeAndAgentWasBusy()
	{
		// Arrange: enqueue-mode optimistic message — SessionFeature.Messages.cs sets IsPending=true
		// at send time when the agent is busy. After the SDK echo, IsPending must be preserved.
		SessionModel session = CreateSession();
		SessionEventProcessor processor = CreateProcessor();
		session.ActiveWorkingGroup = new ActivityGroupModel { Status = GroupStatusEnum.Running };

		// Simulate optimistic add with IsPending=true (enqueue mode)
		session.Messages.Add(new ChatMessageModel
		{
			Content = "Queued message",
			IsUser = true,
			IsComplete = false,
			IsPending = true,   // set by SessionFeature.Messages.cs for enqueue mode
			EventJson = null
		});

		UserMessageEvent evt = new()
		{
			Data = new UserMessageData { Content = "Queued message" },
			Timestamp = DateTimeOffset.UtcNow
		};

		// Act
		processor.Process(session, evt);

		// Assert: optimistic confirmed and IsPending preserved (cleared later by AssistantTurnStart)
		ChatMessageModel? msg = session.Messages.FirstOrDefault(m => m.IsUser);
		msg.ShouldNotBeNull();
		msg.IsComplete.ShouldBeTrue();
		msg.IsPending.ShouldBeTrue();
	}

	[Fact]
	public void Handle_OptimisticMessage_PreservesId_WhenConfirmed()
	{
		SessionModel session = CreateSession();
		SessionEventProcessor processor = CreateProcessor();
		string optimisticId = "optimistic-1";

		session.Messages.Add(new ChatMessageModel
		{
			Id = optimisticId,
			Content = "Queued message",
			IsUser = true,
			IsComplete = false,
			EventJson = null
		});

		processor.Process(session, new UserMessageEvent
		{
			Data = new UserMessageData { Content = "Queued message" },
			Timestamp = DateTimeOffset.UtcNow
		});

		ChatMessageModel msg = session.Messages.Single(m => m.IsUser && m.Content == "Queued message");
		msg.Id.ShouldBe(optimisticId);
		msg.IsComplete.ShouldBeTrue();
	}

	[Fact]
	public void Handle_OptimisticMessagesWithSameContent_ConfirmInFifoOrder()
	{
		SessionModel session = CreateSession();
		SessionEventProcessor processor = CreateProcessor();

		ChatMessageModel first = new()
		{
			Id = "m1",
			Content = "and again",
			IsUser = true,
			IsComplete = false,
			IsPending = true,
			EventJson = null
		};
		ChatMessageModel second = new()
		{
			Id = "m2",
			Content = "and again",
			IsUser = true,
			IsComplete = false,
			IsPending = true,
			EventJson = null
		};
		session.Messages.Add(first);
		session.Messages.Add(second);

		processor.Process(session, new UserMessageEvent
		{
			Data = new UserMessageData { Content = "and again" },
			Timestamp = DateTimeOffset.UtcNow
		});
		first.IsComplete.ShouldBeTrue();
		second.IsComplete.ShouldBeFalse();

		processor.Process(session, new UserMessageEvent
		{
			Data = new UserMessageData { Content = "and again" },
			Timestamp = DateTimeOffset.UtcNow
		});
		second.IsComplete.ShouldBeTrue();
	}

	[Fact]
	public void AssistantTurnStart_ClearsPendingFromOldestMessage()
	{
		// Arrange: two pending messages
		SessionModel session = CreateSession();
		SessionEventProcessor processor = CreateProcessor();

		ChatMessageModel pending1 = new() { Content = "First", IsUser = true, IsComplete = true, IsPending = true, EventJson = null };
		ChatMessageModel pending2 = new() { Content = "Second", IsUser = true, IsComplete = true, IsPending = true, EventJson = null };
		session.Messages.Add(pending1);
		session.Messages.Add(pending2);

		// Act: agent starts a new turn
		processor.Process(session, new AssistantTurnStartEvent { Data = new AssistantTurnStartData { TurnId = "0" }, Timestamp = DateTimeOffset.UtcNow });

		// Assert: only the FIRST pending message is activated
		pending1.IsPending.ShouldBeFalse();
		pending2.IsPending.ShouldBeTrue();
	}

	[Fact]
	public void AssistantTurnStart_NonInitialTurn_DoesNotClearNextPendingMessage()
	{
		SessionModel session = CreateSession();
		SessionEventProcessor processor = CreateProcessor();

		ChatMessageModel pending1 = new() { Content = "First", IsUser = true, IsComplete = true, IsPending = true, EventJson = null };
		ChatMessageModel pending2 = new() { Content = "Second", IsUser = true, IsComplete = true, IsPending = true, EventJson = null };
		session.Messages.Add(pending1);
		session.Messages.Add(pending2);

		// Initial turn for first queued message should activate only the first pending message.
		processor.Process(session, new AssistantTurnStartEvent
		{
			Data = new AssistantTurnStartData { TurnId = "0" },
			Timestamp = DateTimeOffset.UtcNow
		});
		pending1.IsPending.ShouldBeFalse();
		pending2.IsPending.ShouldBeTrue();

		// Follow-up turn within the same response should NOT activate the next pending message.
		processor.Process(session, new AssistantTurnStartEvent
		{
			Data = new AssistantTurnStartData { TurnId = "1" },
			Timestamp = DateTimeOffset.UtcNow
		});
		pending2.IsPending.ShouldBeTrue();
	}

	[Fact]
	public void FullPendingFlow_SecondMessageActivatesAfterFirstTurnCompletes()
	{
		// Arrange: first user message and agent's turn is running
		SessionModel session = CreateSession();
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

		// Second (enqueued) message grouped with first — appears BEFORE operations
		int activityGroupIndex = session.Messages.FindIndex(m => m.Type == MessageTypeEnum.ActivityGroup);
		int secondMsgIndex = session.Messages.IndexOf(secondMsg);
		secondMsgIndex.ShouldBeLessThan(activityGroupIndex,
			"enqueued user message should appear before the activity group");
	}

	[Fact]
	public void Handle_AgentGeneratedContinuation_NotAddedToChat_AndWorkingPanelStaysOpen()
	{
		// Arrange: agent is mid-turn (active working group)
		SessionModel session = CreateSession();
		SessionEventProcessor processor = CreateProcessor();
		ActivityGroupModel existingGroup = new() { Status = GroupStatusEnum.Running };
		session.ActiveWorkingGroup = existingGroup;

		UserMessageEvent evt = new()
		{
			Data = new UserMessageData
			{
				Content = "Please continue from where you left off.",
				Source = "thinking-exhausted-continuation"
			},
			Timestamp = DateTimeOffset.UtcNow
		};

		// Act
		processor.Process(session, evt);

		// Assert: continuation must NOT appear in the chat log
		session.Messages.ShouldNotContain(m => m.IsUser);
		// The working group must remain open (not finalized / completion sound not triggered)
		session.ActiveWorkingGroup.ShouldNotBeNull();
		session.ActiveWorkingGroup.ShouldBeSameAs(existingGroup);
		session.Status.ShouldBe(SessionStatusEnum.Running);
	}

	/// <summary>
	/// Regression test for the full thinking-exhausted-continuation event sequence as observed in production.
	/// Verifies that:
	///   1. The continuation message never appears in the chat log.
	///   2. The working panel stays open across the continuation (no premature idle/completion sound).
	///   3. Both tool turns are merged into a single activity group.
	///   4. The completion sound fires exactly once, only when the real session.idle arrives.
	/// </summary>
	[Fact]
	public void ThinkingExhaustedContinuation_FullSequence_SingleActivityGroupAndNoChat()
	{
		SessionModel session = CreateSession();
		SessionEventProcessor processor = CreateProcessor();

		// Track completion-sound firings via the OnSessionFinished event
		int finishedCount = 0;
		SessionIdleHandler.OnSessionFinished += () => finishedCount++;

		try
		{
			// Original user message + agent starts working
			processor.Process(session, new UserMessageEvent
			{
				Data = new UserMessageData { Content = "Do a big task" },
				Timestamp = DateTimeOffset.UtcNow
			});
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

			// SDK auto-generates the thinking-exhausted-continuation message mid-turn
			processor.Process(session, new UserMessageEvent
			{
				Data = new UserMessageData
				{
					Content = "Please continue from where you left off.",
					Source = "thinking-exhausted-continuation"
				},
				Timestamp = DateTimeOffset.UtcNow
			});

			// Continuation message must NOT appear in the chat
			session.Messages.ShouldNotContain(m => m.IsUser && m.Content == "Please continue from where you left off.");
			// Working panel must remain open — no premature completion sound
			session.ActiveWorkingGroup.ShouldNotBeNull();
			finishedCount.ShouldBe(0, "completion sound must not fire on continuation");

			// Continuation turn starts (non-zero TurnId, as seen in production logs)
			processor.Process(session, new AssistantTurnStartEvent
			{
				Data = new AssistantTurnStartData { TurnId = "5" },
				Timestamp = DateTimeOffset.UtcNow
			});
			processor.Process(session, new ToolExecutionStartEvent
			{
				Data = new ToolExecutionStartData { ToolCallId = "tc2", ToolName = "write_file" },
				Timestamp = DateTimeOffset.UtcNow
			});
			processor.Process(session, new ToolExecutionCompleteEvent
			{
				Data = new ToolExecutionCompleteData { ToolCallId = "tc2", Success = true },
				Timestamp = DateTimeOffset.UtcNow
			});

			// Real session idle — both tool turns are in a single group
			processor.Process(session, new SessionIdleEvent
			{
				Data = new SessionIdleData(),
				Timestamp = DateTimeOffset.UtcNow
			});

			// Exactly one activity group, containing both tools
			List<ChatMessageModel> activityMessages = [.. session.Messages.Where(m => m.Type == MessageTypeEnum.ActivityGroup)];
			activityMessages.Count.ShouldBe(1, "both turns should be merged into a single activity group");

			ActivityGroupModel group = activityMessages[0].ActivityGroup!;
			group.Tools.Count().ShouldBe(2, "activity group should contain tools from both turns");

			// Completion sound fires exactly once for the real idle
			finishedCount.ShouldBe(1);
		}
		finally
		{
			SessionIdleHandler.OnSessionFinished -= () => finishedCount++;
		}
	}
}
