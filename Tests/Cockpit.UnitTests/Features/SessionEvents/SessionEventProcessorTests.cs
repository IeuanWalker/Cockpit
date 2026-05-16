using Cockpit.Features.SessionEvents;
using Cockpit.Features.SessionEvents.Models;
using Cockpit.Features.Sessions.Models;
using GitHub.Copilot.SDK;
using Microsoft.Extensions.Logging.Abstractions;
using Shouldly;

namespace Cockpit.UnitTests.Features.SessionEvents;

/// <summary>
/// Tests for processor-level orchestration logic: safety-net group finalization,
/// LastActivity tracking, FinalizeOpenGroup, and exception isolation.
/// Handler-specific behaviour lives in the corresponding *HandlerTests files.
/// </summary>
public class SessionEventProcessorTests
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
	public void Process_UserMessage_WhenGroupOpen_FinalizesGroupFirst()
	{
		// Arrange
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
				Status = ToolStatusEnum.Success,
				StartTime = DateTime.Now
			},
			EventJson = null
		});
		session.ActiveWorkingGroup = group;

		UserMessageEvent evt = new()
		{
			Data = new UserMessageData { Content = "Follow-up" },
			Timestamp = DateTimeOffset.UtcNow
		};

		// Act
		processor.Process(session, evt);

		// Assert — group was finalized before the new message was appended
		session.ActiveWorkingGroup.ShouldBeNull();
		session.Messages.ShouldContain(m => m.IsUser && m.Content == "Follow-up");
	}

	[Fact]
	public void Process_UserMessage_UpdatesLastActivity()
	{
		// Arrange
		SessionModel session = CreateSession();
		DateTime beforeTest = DateTime.UtcNow.AddSeconds(-1);
		session.LastActivity = beforeTest;
		SessionEventProcessor processor = CreateProcessor();

		// Act
		processor.Process(session, new UserMessageEvent
		{
			Data = new UserMessageData { Content = "Hi" },
			Timestamp = DateTimeOffset.UtcNow
		});

		// Assert
		session.LastActivity.ShouldBeGreaterThan(beforeTest);
	}

	[Fact]
	public void Process_AgentContinuation_DoesNotUpdateLastActivity()
	{
		// Arrange — thinking-exhausted-continuation is an internal SDK signal, not real user activity
		SessionModel session = CreateSession();
		DateTime fixedTime = new(2020, 1, 1);
		session.LastActivity = fixedTime;
		SessionEventProcessor processor = CreateProcessor();

		// Act
		processor.Process(session, new UserMessageEvent
		{
			Data = new UserMessageData
			{
				Content = "Please continue from where you left off.",
				Source = "thinking-exhausted-continuation"
			},
			Timestamp = DateTimeOffset.UtcNow
		});

		// Assert — LastActivity must not change for agent-synthesised continuations
		session.LastActivity.ShouldBe(fixedTime);
	}

	[Fact]
	public void Process_AssistantTurnEnd_DoesNotUpdateLastActivity()
	{
		// Arrange — AssistantTurnEnd is informational; only UserMessage and SessionIdle update LastActivity
		SessionModel session = CreateSession();
		DateTime fixedTime = new(2020, 1, 1);
		session.LastActivity = fixedTime;
		SessionEventProcessor processor = CreateProcessor();

		// Act
		processor.Process(session, new AssistantTurnEndEvent
		{
			Data = new AssistantTurnEndData { TurnId = "t1" },
			Timestamp = DateTimeOffset.UtcNow
		});

		// Assert
		session.LastActivity.ShouldBe(fixedTime);
	}

	[Fact]
	public void Process_InformationalEvent_DoesNotUpdateLastActivity()
	{
		// Arrange
		SessionModel session = CreateSession();
		DateTime fixedTime = new(2020, 1, 1);
		session.LastActivity = fixedTime;
		SessionEventProcessor processor = CreateProcessor();

		// Act
		processor.Process(session, new SystemMessageEvent
		{
			Data = new SystemMessageData { Role = SystemMessageRole.System, Content = "init" },
			Timestamp = DateTimeOffset.UtcNow
		});

		// Assert — informational event must not touch LastActivity
		session.LastActivity.ShouldBe(fixedTime);
	}

	[Fact]
	public void FinalizeOpenGroup_WithOpenGroup_FinalizesIt()
	{
		// Arrange
		SessionModel session = CreateSession();
		SessionEventProcessor processor = CreateProcessor();
		session.Messages.Add(new ChatMessageModel { IsUser = true, EventJson = null });

		processor.Process(session, new ToolExecutionStartEvent
		{
			Data = new ToolExecutionStartData { ToolCallId = "tc1", ToolName = "read_file" },
			Timestamp = DateTimeOffset.UtcNow
		});

		// Act
		processor.FinalizeOpenGroup(session);

		// Assert
		session.ActiveWorkingGroup.ShouldBeNull();
		session.Status.ShouldBe(SessionStatusEnum.Idle);
	}

	[Fact]
	public void FinalizeOpenGroup_WithNoGroup_DoesNotThrow()
	{
		// Arrange
		SessionModel session = CreateSession();
		SessionEventProcessor processor = CreateProcessor();

		// Act & Assert
		Should.NotThrow(() => processor.FinalizeOpenGroup(session));
	}

	[Fact]
	public void Process_SessionIdle_WithPendingMessages_FinalizesAndKeepsRunning()
	{
		// Arrange: enqueue mode — group should be finalized but session stays Running
		SessionModel session = CreateSession();
		SessionEventProcessor processor = CreateProcessor();
		session.Status = SessionStatusEnum.Running;
		session.Messages.Add(new ChatMessageModel { Id = "user1", IsUser = true, Content = "Hello", EventJson = null });
		session.Messages.Add(new ChatMessageModel { Id = "user2", IsUser = true, Content = "Queued", IsPending = true, EventJson = null });
		session.ActiveWorkingGroup = new ActivityGroupModel
		{
			Status = GroupStatusEnum.Running,
			TriggeredByUserMessageId = "user1"
		};
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

		// Act
		processor.Process(session, new SessionIdleEvent
		{
			Data = new SessionIdleData(),
			Timestamp = DateTimeOffset.UtcNow
		});

		// Assert — group finalized into Messages, but session keeps Running for queued message
		session.ActiveWorkingGroup.ShouldBeNull();
		session.Messages.ShouldContain(m => m.Type == MessageTypeEnum.ActivityGroup);
		session.Status.ShouldBe(SessionStatusEnum.Running);
	}

	[Fact]
	public void Process_WhenNoMatchingGroup_DoesNotPropagate()
	{
		// ToolComplete with no active group — handler exits early; processor must not throw
		SessionModel session = CreateSession();
		SessionEventProcessor processor = CreateProcessor();

		Should.NotThrow(() => processor.Process(session, new ToolExecutionCompleteEvent
		{
			Data = new ToolExecutionCompleteData { ToolCallId = "nonexistent", Success = true },
			Timestamp = DateTimeOffset.UtcNow
		}));
	}

	/// <summary>
	/// Regression test for the replay ordering bug.
	///
	/// In immediate-mode, the SDK writes assistant.turn_start to the event log before the
	/// user.message echo. The 100 ms swap heuristic can fail when the gap is larger, leaving
	/// the log in the wrong order: [turn_start, user.message, ...].
	///
	/// Old behaviour: safety-net fired unconditionally → empty group cleared → subsequent
	/// assistant messages (turn 0) went to chat → tools anchored to that chat message →
	/// ops group appeared between two assistant messages.
	///
	/// Expected: the empty spurious group must be kept alive and re-anchored to the user
	/// message so that all subsequent events are attributed to the correct prompt.
	/// </summary>
	[Fact]
	public void Process_UserMessage_WhenEmptyGroupOpen_KeepsGroupAliveAndReanchors()
	{
		// Arrange — empty group already open (created by a premature AssistantTurnStart)
		SessionModel session = CreateSession();
		SessionEventProcessor processor = CreateProcessor();
		ActivityGroupModel spuriousGroup = new() { Status = GroupStatusEnum.Running };
		session.ActiveWorkingGroup = spuriousGroup;

		UserMessageEvent evt = new()
		{
			Data = new UserMessageData { Content = "Fix the settings popup" },
			Timestamp = DateTimeOffset.UtcNow
		};

		// Act
		processor.Process(session, evt);

		// Assert — spurious group must still be alive (not finalized or cleared)
		session.ActiveWorkingGroup.ShouldBeSameAs(spuriousGroup);

		// Assert — group is now anchored to the newly-added user message
		ChatMessageModel? userMsg = session.Messages.ShouldHaveSingleItem();
		userMsg.IsUser.ShouldBeTrue();
		userMsg.Content.ShouldBe("Fix the settings popup");
		spuriousGroup.TriggeredByUserMessageId.ShouldBe(userMsg.Id);
	}

	/// <summary>
	/// Full replay ordering regression: when AssistantTurnStart fires before user.message (swap
	/// heuristic failed), the final chat order must be [UserMsg, OpsGroup, Summary] — not
	/// [UserMsg, Turn0AssistantMsg, OpsGroup, Summary].
	/// </summary>
	[Fact]
	public void Replay_TurnStartBeforeUserMessage_ProducesCorrectChatOrder()
	{
		// Arrange
		SessionModel session = CreateSession();
		SessionEventProcessor processor = CreateProcessor();
		string turn0MsgId = Guid.NewGuid().ToString();

		// Step 1: AssistantTurnStart fires first (wrong order — swap heuristic failed)
		processor.Process(session, new AssistantTurnStartEvent
		{
			Data = new AssistantTurnStartData { TurnId = "0" },
			Timestamp = DateTimeOffset.UtcNow
		});

		// Step 2: user.message echo arrives late — empty group must be kept alive
		processor.Process(session, new UserMessageEvent
		{
			Data = new UserMessageData { Content = "Fix the settings popup" },
			Timestamp = DateTimeOffset.UtcNow
		});

		// Step 3: assistant.message with content — must go to thinking panel (not chat)
		processor.Process(session, new AssistantMessageEvent
		{
			Data = new AssistantMessageData
			{
				MessageId = turn0MsgId,
				Content = "I'll analyse the component."
			},
			Timestamp = DateTimeOffset.UtcNow
		});

		// Step 4: tool execution — added to the working group
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

		// Step 5: end of replay — no session.idle (it's ephemeral); finalize manually
		processor.FinalizeOpenGroup(session);

		// Assert — only two visible messages: user prompt and the summary (turn-0 assistant
		// message must have been routed to the thinking panel, not inserted into chat)
		session.Messages.Count.ShouldBe(3, "expected UserMsg + OpsGroup + Summary");

		ChatMessageModel userMsg = session.Messages[0];
		userMsg.IsUser.ShouldBeTrue();
		userMsg.Content.ShouldBe("Fix the settings popup");

		ChatMessageModel opsMsg = session.Messages[1];
		opsMsg.Type.ShouldBe(MessageTypeEnum.ActivityGroup);
		opsMsg.ActivityGroup.ShouldNotBeNull();
		opsMsg.ActivityGroup!.Tools.ShouldNotBeEmpty();

		ChatMessageModel summaryMsg = session.Messages[2];
		summaryMsg.IsUser.ShouldBeFalse();
		summaryMsg.Content.ShouldBe("I'll analyse the component.");
	}
}
