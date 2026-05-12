using Cockpit.Features.SessionEvents;
using Cockpit.Features.SessionEvents.Models;
using Cockpit.Features.Sessions.Models;
using GitHub.Copilot.SDK;
using Microsoft.Extensions.Logging.Abstractions;
using Shouldly;

namespace Cockpit.UnitTests.Features.SessionEvents.Handlers;

/// <summary>
/// Regression tests for the two immediate-mode bugs:
///
/// Bug 1 — "Pending…" UI stuck state:
///   When a user sends a message in immediate mode while the agent is mid-turn, the SDK echoes
///   the user.message *after* assistant.turn_start for the new response. This makes wasAgentBusy=true
///   when the echo arrives, which previously escalated IsPending even for immediate-mode messages.
///   Fix: optimistic IsPending is preserved only if set at send time; wasAgentBusy is never used
///   to retroactively mark an optimistic message as pending.
///
/// Bug 2 — Wrong ordering on session resume:
///   During replay, assistant.turn_start fires before user.message (both within ~2 ms).
///   The swap pre-sort in SessionFeature.Lifecycle.cs reorders these pairs so the user message
///   arrives first. Combined with the AssistantTurnStartHandler fallback anchor (TriggeredByUserMessageId
///   falls back to LastOrDefault user message), working groups are placed after the message that
///   triggered them even when that message has IsPending=true.
/// </summary>
[Collection("SessionIdleEvent")]
public class ImmediateModeReplayTests
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

	/// <summary>
	/// Reproduces the exact replay sequence from the failing session (c55572c6-...) after the
	/// swap pre-sort has reordered the immediate-mode user message before its turn_start.
	///
	/// Expected final message order:
	///   [0] "Deep dive"              — first user message
	///   [1] ActivityGroup (turn 1)   — anchored after "Deep dive"
	///   [2] "Actually focus"         — immediate-mode second message
	///   [3] ActivityGroup (turn 2)   — anchored after "Actually focus"
	/// </summary>
	[Fact]
	public void Replay_ImmediateMode_ActivityGroupsPositionedAfterTheirTriggeringMessages()
	{
		SessionModel session = CreateSession();
		SessionEventProcessor processor = CreateProcessor();

		// Turn 1: first user message, then agent starts working
		processor.Process(session, new UserMessageEvent
		{
			Data = new UserMessageData { Content = "Deep dive" },
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

		// Immediate-mode second message arrives while turn 1 is still running.
		// After the swap pre-sort the user.message arrives first (BEFORE its turn_start).
		// The safety net fires: group 1 is closed and inserted. wasAgentBusy=true so
		// UserMessageHandler sets IsPending=true on "Actually focus" (replay — no optimistic).
		DateTimeOffset immediateSendTs = DateTimeOffset.UtcNow;
		processor.Process(session, new UserMessageEvent
		{
			Data = new UserMessageData { Content = "Actually focus" },
			Timestamp = immediateSendTs
		});

		ChatMessageModel? actuallyFocusMsg = session.Messages.FirstOrDefault(m => m.IsUser && m.Content == "Actually focus");
		actuallyFocusMsg.ShouldNotBeNull();
		// IsPending=true here is expected — the safety net was busy. The key fix is that the
		// subsequent AssistantTurnStart anchors to this message via fallback, ignoring IsPending.
		actuallyFocusMsg.IsPending.ShouldBeTrue();

		// Turn 2: non-initial turn_start (turnId "2") — must anchor to "Actually focus" via fallback
		processor.Process(session, new AssistantTurnStartEvent
		{
			Data = new AssistantTurnStartData { TurnId = "2" },
			Timestamp = immediateSendTs.AddMilliseconds(2)
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

		// Finalize remaining open group (simulates session.shutdown without session.idle)
		processor.FinalizeOpenGroup(session);

		// Assert ordering
		List<ChatMessageModel> messages = session.Messages;
		int deepDiveIdx = messages.FindIndex(m => m.IsUser && m.Content == "Deep dive");
		int actuallyFocusIdx = messages.IndexOf(actuallyFocusMsg);
		int group1Idx = messages.FindIndex(m => m.Type == MessageTypeEnum.ActivityGroup
			&& m.ActivityGroup?.Tools.Any(t => t.ToolCallId == "tc1") == true);
		int group2Idx = messages.FindIndex(m => m.Type == MessageTypeEnum.ActivityGroup
			&& m.ActivityGroup?.Tools.Any(t => t.ToolCallId == "tc2") == true);

		deepDiveIdx.ShouldBeGreaterThanOrEqualTo(0);
		group1Idx.ShouldBeGreaterThanOrEqualTo(0, "activity group for turn 1 must be inserted");
		actuallyFocusIdx.ShouldBeGreaterThanOrEqualTo(0);
		group2Idx.ShouldBeGreaterThanOrEqualTo(0, "activity group for turn 2 must be inserted");

		group1Idx.ShouldBeGreaterThan(deepDiveIdx,
			"activity group 1 must come after 'Deep dive'");
		actuallyFocusIdx.ShouldBeGreaterThan(group1Idx,
			"'Actually focus' must appear after the first activity group");
		group2Idx.ShouldBeGreaterThan(actuallyFocusIdx,
			"activity group 2 must come after 'Actually focus'");
	}

	/// <summary>
	/// Verifies that an optimistic message whose IsPending=false at send time (immediate mode)
	/// is NOT retroactively marked pending when the SDK echo arrives with wasAgentBusy=true.
	/// Reproduces the "Pending…" stuck-state UI bug.
	/// </summary>
	[Fact]
	public void ImmediateMode_OptimisticMessage_NotMarkedPending_WhenConfirmedWithAgentBusy()
	{
		// Arrange: agent is mid-turn, user sends immediate-mode message (IsPending=false at send)
		SessionModel session = CreateSession();
		SessionEventProcessor processor = CreateProcessor();
		session.ActiveWorkingGroup = new ActivityGroupModel { Status = GroupStatusEnum.Running };

		ChatMessageModel optimistic = new()
		{
			Id = "opt-1",
			Content = "Interrupt now",
			IsUser = true,
			IsComplete = false,
			IsPending = false,   // immediate mode: not queued
			EventJson = null
		};
		session.Messages.Add(optimistic);

		// Act: SDK echoes the user.message (wasAgentBusy=true inside the processor)
		processor.Process(session, new UserMessageEvent
		{
			Data = new UserMessageData { Content = "Interrupt now" },
			Timestamp = DateTimeOffset.UtcNow
		});

		// Assert
		ChatMessageModel confirmed = session.Messages.Single(m => m.IsUser && m.Content == "Interrupt now");
		confirmed.IsComplete.ShouldBeTrue("optimistic must be confirmed by the event");
		confirmed.IsPending.ShouldBeFalse(
			"immediate-mode message must never be stuck as Pending — nothing will clear it");
	}

	/// <summary>
	/// Contrasting case: enqueue-mode optimistic message (IsPending=true at send) must retain
	/// its pending state after confirmation. The pending flag is later cleared when the agent
	/// starts a new turn (AssistantTurnStartHandler, turnId "0").
	/// </summary>
	[Fact]
	public void EnqueueMode_OptimisticMessage_RetainsPending_WhenConfirmedWithAgentBusy()
	{
		SessionModel session = CreateSession();
		SessionEventProcessor processor = CreateProcessor();
		session.ActiveWorkingGroup = new ActivityGroupModel { Status = GroupStatusEnum.Running };

		ChatMessageModel optimistic = new()
		{
			Id = "opt-2",
			Content = "Queue this",
			IsUser = true,
			IsComplete = false,
			IsPending = true,   // enqueue mode: explicitly queued
			EventJson = null
		};
		session.Messages.Add(optimistic);

		processor.Process(session, new UserMessageEvent
		{
			Data = new UserMessageData { Content = "Queue this" },
			Timestamp = DateTimeOffset.UtcNow
		});

		ChatMessageModel confirmed = session.Messages.Single(m => m.IsUser && m.Content == "Queue this");
		confirmed.IsComplete.ShouldBeTrue();
		confirmed.IsPending.ShouldBeTrue(
			"enqueue-mode message must stay pending until the agent starts its new turn");
	}

	/// <summary>
	/// Full end-to-end enqueue scenario: IsPending is cleared when AssistantTurnStart (turnId "0")
	/// fires after the previous turn completes, and the activity group is inserted in the correct
	/// position — after the enqueue-mode message, not before it.
	/// </summary>
	[Fact]
	public void EnqueueMode_FullFlow_ActivityGroupPositionedAfterQueuedMessage()
	{
		SessionModel session = CreateSession();
		SessionEventProcessor processor = CreateProcessor();

		// Turn 1
		processor.Process(session, new UserMessageEvent
		{
			Data = new UserMessageData { Content = "First task" },
			Timestamp = DateTimeOffset.UtcNow
		});
		processor.Process(session, new AssistantTurnStartEvent
		{
			Data = new AssistantTurnStartData { TurnId = "0" },
			Timestamp = DateTimeOffset.UtcNow
		});
		processor.Process(session, new ToolExecutionStartEvent
		{
			Data = new ToolExecutionStartData { ToolCallId = "tc1", ToolName = "tool1" },
			Timestamp = DateTimeOffset.UtcNow
		});

		// Second message arrives in enqueue mode while tool is running
		processor.Process(session, new UserMessageEvent
		{
			Data = new UserMessageData { Content = "Second task" },
			Timestamp = DateTimeOffset.UtcNow
		});
		ChatMessageModel? secondMsg = session.Messages.FirstOrDefault(m => m.IsUser && m.Content == "Second task");
		secondMsg.ShouldNotBeNull();
		secondMsg.IsPending.ShouldBeTrue();

		// Turn 1 completes
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

		// Turn 2 starts — activates the pending "Second task" message
		processor.Process(session, new AssistantTurnStartEvent
		{
			Data = new AssistantTurnStartData { TurnId = "0" },
			Timestamp = DateTimeOffset.UtcNow
		});
		secondMsg.IsPending.ShouldBeFalse("pending must be cleared when its turn starts");

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
		processor.Process(session, new SessionIdleEvent
		{
			Data = new SessionIdleData(),
			Timestamp = DateTimeOffset.UtcNow
		});

		// Expected order: "First task" → group1 → "Second task" → group2
		int firstTaskIdx = session.Messages.FindIndex(m => m.IsUser && m.Content == "First task");
		int secondTaskIdx = session.Messages.IndexOf(secondMsg);
		int group1Idx = session.Messages.FindIndex(m => m.Type == MessageTypeEnum.ActivityGroup
			&& m.ActivityGroup?.Tools.Any(t => t.ToolCallId == "tc1") == true);
		int group2Idx = session.Messages.FindIndex(m => m.Type == MessageTypeEnum.ActivityGroup
			&& m.ActivityGroup?.Tools.Any(t => t.ToolCallId == "tc2") == true);

		group1Idx.ShouldBeGreaterThan(firstTaskIdx, "group 1 after 'First task'");
		secondTaskIdx.ShouldBeGreaterThan(group1Idx, "'Second task' after group 1");
		group2Idx.ShouldBeGreaterThan(secondTaskIdx, "group 2 after 'Second task'");
	}
}
