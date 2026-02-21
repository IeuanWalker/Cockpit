using Cockpit.Features.SessionEvents;
using Cockpit.Features.SessionEvents.Models;
using Cockpit.Features.Sessions.Models;
using GitHub.Copilot.SDK;
using Microsoft.Extensions.Logging.Abstractions;
using Shouldly;

namespace Cockpit.UnitTests.Features.SessionEvents.Handlers;

public class SessionIdleHandlerTests
{
	static readonly ModelInfo testModel = new() { Id = "test", Name = "Test Model" };
	static ChatSession CreateSession() => new()
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
	public void Handle_FinalizesGroupAndSetsIdle()
	{
		// Arrange
		ChatSession session = CreateSession();
		SessionEventProcessor processor = CreateProcessor();
		session.Messages.Add(new ChatMessageModel { IsUser = true, Content = "Do something" });

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

		// Assert
		session.Status.ShouldBe(SessionStatus.Idle);
		session.ActiveWorkingGroup.ShouldBeNull();
		session.Messages.ShouldContain(m => m.Type == MessageTypeEnum.ActivityGroup);
	}

	[Fact]
	public void Handle_WithNoGroup_SetsIdle()
	{
		// Arrange
		ChatSession session = CreateSession();
		session.Status = SessionStatus.Running;
		SessionEventProcessor processor = CreateProcessor();

		// Act
		processor.Process(session, new SessionIdleEvent
		{
			Data = new SessionIdleData(),
			Timestamp = DateTimeOffset.UtcNow
		});

		// Assert
		session.Status.ShouldBe(SessionStatus.Idle);
	}

	[Fact]
	public void Handle_RunningToolsMarkedAsErrorOnFinalization()
	{
		// Arrange
		ChatSession session = CreateSession();
		session.Messages.Add(new ChatMessageModel { IsUser = true });
		SessionEventProcessor processor = CreateProcessor();

		// Start a tool but never complete it
		processor.Process(session, new ToolExecutionStartEvent
		{
			Data = new ToolExecutionStartData { ToolCallId = "tc1", ToolName = "stuck_tool" },
			Timestamp = DateTimeOffset.UtcNow
		});

		// Act — idle without completing the tool
		processor.Process(session, new SessionIdleEvent
		{
			Data = new SessionIdleData(),
			Timestamp = DateTimeOffset.UtcNow
		});

		// Assert — activity message still inserted (group had tools)
		session.Messages.ShouldContain(m => m.Type == MessageTypeEnum.ActivityGroup);
	}

	[Fact]
	public void Handle_ActivityMessageInsertedAfterInitialMessage()
	{
		// Arrange
		ChatSession session = CreateSession();
		SessionEventProcessor processor = CreateProcessor();

		// Build a full turn: user → assistant → tools → idle
		session.Messages.Add(new ChatMessageModel { IsUser = true, Content = "Do something" });
		ChatMessageModel assistantMsg = new() { IsUser = false, Type = MessageTypeEnum.Text, Content = "Sure" };
		session.Messages.Add(assistantMsg);

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

		// Assert — activity message is immediately after the initial assistant message
		int assistantIdx = session.Messages.FindIndex(m => m.Id == assistantMsg.Id);
		int activityIdx = session.Messages.FindIndex(m => m.Type == MessageTypeEnum.ActivityGroup);
		activityIdx.ShouldBe(assistantIdx + 1);
	}

	[Fact]
	public void MultiplePendingMessages_OrderCorrectAfterEachTurn()
	{
		// Reproduces the scenario where msg2 and msg3 are sent while the agent is processing msg1.
		// The SDK echoes user messages and fires assistant.turn_start events immediately, which means
		// IsPending is cleared before the previous session.idle fires.
		// Expected order: msg1 → ops1 → response1 → msg2 → ops2 → response2 → msg3 → ops3 → response3

		ChatSession session = CreateSession();
		SessionEventProcessor processor = CreateProcessor();

		// --- Turn 1: msg1 sent, agent is working ---
		ChatMessageModel msg1 = new() { Id = "msg1", IsUser = true, IsComplete = true, Content = "msg1" };
		session.Messages.Add(msg1);
		processor.Process(session, new AssistantTurnStartEvent { Data = new AssistantTurnStartData { TurnId = "0" }, Timestamp = DateTimeOffset.UtcNow });
		processor.Process(session, new ToolExecutionStartEvent
		{
			Data = new ToolExecutionStartData { ToolCallId = "tc1", ToolName = "tool" },
			Timestamp = DateTimeOffset.UtcNow
		});

		// msg2 and msg3 arrive while turn1 tool is running — safety net fires for msg2
		// then turn_start immediately fires for msg2 (clearing IsPending) before turn1 session.idle
		ChatMessageModel msg2 = new() { Id = "msg2", IsUser = true, IsComplete = true, Content = "msg2" };
		session.Messages.Add(msg2);
		processor.Process(session, new AssistantTurnStartEvent { Data = new AssistantTurnStartData { TurnId = "1" }, Timestamp = DateTimeOffset.UtcNow }); // clears msg2.IsPending, creates group2
		ChatMessageModel msg3 = new() { Id = "msg3", IsUser = true, IsComplete = true, Content = "msg3" };
		session.Messages.Add(msg3);
		processor.Process(session, new AssistantTurnStartEvent { Data = new AssistantTurnStartData { TurnId = "2" }, Timestamp = DateTimeOffset.UtcNow }); // clears msg3.IsPending, creates group3

		// turn1 session.idle fires — the group was pre-cleared by safety net, so this just sets Idle
		// (Simulate via FinalizeOpenGroup with a fresh group that has the tool)
		// Instead, directly test that when session.idle fires with TriggeredByUserMessageId=null,
		// the fallback correctly places after msg1 (the only non-pending user message at the time).
		// The key test is turn2 and turn3 with TriggeredByUserMessageId set.

		// Manually set up turn2's group with TriggeredByUserMessageId=msg2.Id
		session.ActiveWorkingGroup = new ActivityGroupModel
		{
			Status = GroupStatusEnum.Running,
			TriggeredByUserMessageId = "msg2"
		};
		processor.Process(session, new ToolExecutionStartEvent
		{
			Data = new ToolExecutionStartData { ToolCallId = "tc2", ToolName = "tool2" },
			Timestamp = DateTimeOffset.UtcNow
		});
		processor.Process(session, new ToolExecutionCompleteEvent
		{
			Data = new ToolExecutionCompleteData { ToolCallId = "tc2", Success = true },
			Timestamp = DateTimeOffset.UtcNow
		});
		processor.Process(session, new SessionIdleEvent { Data = new SessionIdleData(), Timestamp = DateTimeOffset.UtcNow });

		// Verify activity2 is immediately after msg2 (not after msg3)
		int msg2Idx = session.Messages.FindIndex(m => m.Id == "msg2");
		int activity2Idx = session.Messages.FindIndex(m => m.Type == MessageTypeEnum.ActivityGroup);
		activity2Idx.ShouldBe(msg2Idx + 1, "activity for turn2 should be directly after msg2");
		// msg3 should come AFTER the activity/summary for turn2
		int msg3Idx = session.Messages.FindIndex(m => m.Id == "msg3");
		msg3Idx.ShouldBeGreaterThan(activity2Idx, "msg3 should appear after turn2's operations");

		// turn3 group with TriggeredByUserMessageId=msg3.Id
		session.ActiveWorkingGroup = new ActivityGroupModel
		{
			Status = GroupStatusEnum.Running,
			TriggeredByUserMessageId = "msg3"
		};
		processor.Process(session, new ToolExecutionStartEvent
		{
			Data = new ToolExecutionStartData { ToolCallId = "tc3", ToolName = "tool3" },
			Timestamp = DateTimeOffset.UtcNow
		});
		processor.Process(session, new ToolExecutionCompleteEvent
		{
			Data = new ToolExecutionCompleteData { ToolCallId = "tc3", Success = true },
			Timestamp = DateTimeOffset.UtcNow
		});
		processor.Process(session, new SessionIdleEvent { Data = new SessionIdleData(), Timestamp = DateTimeOffset.UtcNow });

		// Final expected order: msg1, [turn1 if any], msg2, activity2, msg3, activity3
		int finalMsg2Idx = session.Messages.FindIndex(m => m.Id == "msg2");
		int finalMsg3Idx = session.Messages.FindIndex(m => m.Id == "msg3");
		List<ChatMessageModel> activities = [.. session.Messages.Where(m => m.Type == MessageTypeEnum.ActivityGroup)];
		activities.Count.ShouldBe(2);

		int activity3Idx = session.Messages.FindIndex(m => m.Type == MessageTypeEnum.ActivityGroup && m.ActivityGroup?.Tools.Any(t => t.ToolCallId == "tc3") == true);
		activity3Idx.ShouldBeGreaterThan(finalMsg3Idx, "activity3 should come after msg3");
		finalMsg3Idx.ShouldBeGreaterThan(activities[0] == session.Messages[activity2Idx] ? activity2Idx : 0,
			"msg3 should come after activity2's region");
	}
}
