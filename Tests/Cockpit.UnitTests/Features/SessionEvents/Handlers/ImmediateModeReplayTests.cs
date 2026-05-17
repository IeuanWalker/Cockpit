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
///   During replay, the user.message echo for a steered/enqueued send appears AFTER the
///   turn_start that processes it. The non-optimistic path in UserMessageHandler is only
///   reached during replay (live sends go through the optimistic path). In a completed session
///   no message is ever truly "Pending", so IsPending is always false in this path. The
///   safety net (wasAgentBusy=true + SessionIdleHandler) then inserts the preceding activity
///   group correctly before the message.
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
	/// Reproduces the replay sequence from the failing session using the natural
	/// SDK event order: assistant.turn_start fires first, then the user.message echo arrives.
	///
	/// The safety net (wasAgentBusy=true + SessionIdleHandler) inserts the first activity group
	/// before the immediate-mode message because IsPending=false in the non-optimistic path.
	///
	/// Expected final message order:
	///   [0] "Deep dive"              — first user message
	///   [1] ActivityGroup (turn 1)   — anchored after "Deep dive"
	///   [2] "Actually focus"         — immediate-mode second message (IsPending=false)
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

		// Natural SDK order for immediate/steered send: turn_start fires before the user.message echo.
		// No session.idle between turns — the group stays open.
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

		ChatMessageModel? actuallyFocusMsg = session.Messages.FirstOrDefault(m => m.IsUser && m.Content == "Actually focus");
		actuallyFocusMsg.ShouldNotBeNull();
		// IsPending must be false — the non-optimistic (replay) path never sets IsPending=true.
		actuallyFocusMsg.IsPending.ShouldBeFalse();

		// Turn 2 tools
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
			"'Actually focus' (immediate-mode) must appear after the first activity group");
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
	/// Full end-to-end enqueue scenario: the activity group for turn 1 is inserted BEFORE the
	/// second user message (correct ordering — ops come between the user messages that bound them),
	/// and a second group for turn 2 follows the second message.
	/// </summary>
	[Fact]
	public void EnqueueMode_FullFlow_ActivityGroupPositionedBeforeQueuedMessage()
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
		secondMsg.IsPending.ShouldBeFalse(
			"non-optimistic path is replay-only; replay messages are never pending");

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
		// (ops for turn 1 appear between the two user messages, ops for turn 2 follow second msg)
		int firstTaskIdx = session.Messages.FindIndex(m => m.IsUser && m.Content == "First task");
		int secondTaskIdx = session.Messages.IndexOf(secondMsg);
		int group1Idx = session.Messages.FindIndex(m => m.Type == MessageTypeEnum.ActivityGroup
			&& m.ActivityGroup?.Tools.Any(t => t.ToolCallId == "tc1") == true);
		int group2Idx = session.Messages.FindIndex(m => m.Type == MessageTypeEnum.ActivityGroup
			&& m.ActivityGroup?.Tools.Any(t => t.ToolCallId == "tc2") == true);

		secondTaskIdx.ShouldBeGreaterThan(firstTaskIdx, "'Second task' after 'First task'");
		group1Idx.ShouldBeLessThan(secondTaskIdx, "group 1 before 'Second task' (ops between user messages)");
		group2Idx.ShouldBeGreaterThan(group1Idx, "group 2 after group 1");
		group2Idx.ShouldBeGreaterThan(secondTaskIdx, "group 2 after 'Second task'");
	}

	/// <summary>
	/// Verifies that when the agent completes its work (final summary text in the last turn)
	/// the safety net does NOT suppress the final summary — it must be promoted to chat.
	/// This covers both the shutdown-before-user-message case AND the no-shutdown case,
	/// since detection is now based on <c>assistant.turn_end</c> / <c>assistant.turn_start</c>.
	///
	/// Real event order:
	///   1. Many tool turns inside ops group.
	///   2. Final turn: turn_start → assistant.message "Done..." → turn_end (sets AgentTurnCompleted).
	///   3. session.shutdown (Routine) — no longer needed to signal completion.
	///   4. user.message arrives → safety net fires with wasAgentBusy=true.
	///   5. Expected: summary IS extracted (not suppressed), appears after ops group in chat.
	/// </summary>
	[Fact]
	public void CompletedViaTurnEnd_FinalMessage_IsPromotedAsSummary()
	{
		SessionModel session = CreateSession();
		SessionEventProcessor processor = CreateProcessor();

		// Anchor user message
		processor.Process(session, new UserMessageEvent
		{
			Data = new UserMessageData { Content = "Fix the bug" },
			Timestamp = DateTimeOffset.UtcNow
		});

		// Agent starts working — opens an ops group with a tool call
		processor.Process(session, new AssistantTurnStartEvent
		{
			Data = new AssistantTurnStartData { TurnId = "t1" },
			Timestamp = DateTimeOffset.UtcNow
		});
		processor.Process(session, new ToolExecutionStartEvent
		{
			Data = new ToolExecutionStartData { ToolCallId = "tc1", ToolName = "edit" },
			Timestamp = DateTimeOffset.UtcNow
		});
		processor.Process(session, new ToolExecutionCompleteEvent
		{
			Data = new ToolExecutionCompleteData { ToolCallId = "tc1", Success = true },
			Timestamp = DateTimeOffset.UtcNow
		});

		// Final text-only turn: no new group created (ActiveWorkingGroup still alive),
		// message delta lands as a ThinkingEvent inside the existing group.
		// turn_end at the end of this turn sets AgentTurnCompleted = true.
		processor.Process(session, new AssistantTurnStartEvent
		{
			Data = new AssistantTurnStartData { TurnId = "t2" },
			Timestamp = DateTimeOffset.UtcNow
		});
		processor.Process(session, new AssistantMessageDeltaEvent
		{
			Data = new AssistantMessageDeltaData { MessageId = "summary-msg", DeltaContent = "Done. Here's what changed:" },
			Timestamp = DateTimeOffset.UtcNow
		});
		processor.Process(session, new AssistantMessageEvent
		{
			Data = new AssistantMessageData { MessageId = "summary-msg", Content = "Done. Here's what changed:" },
			Timestamp = DateTimeOffset.UtcNow
		});
		// turn_end marks the agent as having completed its last mini-turn
		processor.Process(session, new AssistantTurnEndEvent
		{
			Data = new AssistantTurnEndData { TurnId = "t2" },
			Timestamp = DateTimeOffset.UtcNow
		});
		session.AgentTurnCompleted.ShouldBeTrue("turn_end must set AgentTurnCompleted");

		// Routine shutdown fires — no longer sets a flag, but must not break anything
		processor.Process(session, new SessionShutdownEvent
		{
			Data = new SessionShutdownData
			{
				ShutdownType = ShutdownType.Routine,
				CodeChanges = new ShutdownCodeChanges { FilesModified = [], LinesAdded = 0, LinesRemoved = 0 },
				ModelMetrics = new Dictionary<string, ShutdownModelMetric>(),
				SessionStartTime = 0,
				TotalApiDurationMs = 0,
				TotalPremiumRequests = 0
			},
			Timestamp = DateTimeOffset.UtcNow
		});

		// Next user message → safety net fires; summary should NOT be suppressed
		processor.Process(session, new UserMessageEvent
		{
			Data = new UserMessageData { Content = "Now do X" },
			Timestamp = DateTimeOffset.UtcNow
		});

		// The flag must be consumed (reset) after firing
		session.AgentTurnCompleted.ShouldBeFalse("flag must be consumed after safety net fires");

		// Ops group must appear in chat
		session.Messages.ShouldContain(m => m.Type == MessageTypeEnum.ActivityGroup,
			"ops group must be inserted into chat");

		// Final summary must appear as a standalone text message (promoted, not suppressed)
		session.Messages.ShouldContain(m =>
			m.Type == MessageTypeEnum.Text && !m.IsUser && m.Content == "Done. Here's what changed:",
			"final agent summary must be promoted when the last turn completed");
	}

	/// <summary>
	/// Verifies that when the agent completes its work WITHOUT a session.shutdown
	/// (groups 4 and 5 in real sessions — no shutdown between consecutive user messages),
	/// the final summary is still promoted based on turn_end alone.
	///
	/// Real event order:
	///   1. Tool work turns.
	///   2. Final turn: turn_start → assistant.message "All done..." → turn_end (AgentTurnCompleted=true).
	///   3. No session.shutdown fires.
	///   4. user.message arrives → safety net: AgentTurnCompleted=true → summary promoted.
	/// </summary>
	[Fact]
	public void CompletedWithoutShutdown_FinalMessage_IsPromotedAsSummary()
	{
		SessionModel session = CreateSession();
		SessionEventProcessor processor = CreateProcessor();

		processor.Process(session, new UserMessageEvent
		{
			Data = new UserMessageData { Content = "carry on" },
			Timestamp = DateTimeOffset.UtcNow
		});
		processor.Process(session, new AssistantTurnStartEvent
		{
			Data = new AssistantTurnStartData { TurnId = "t1" },
			Timestamp = DateTimeOffset.UtcNow
		});
		processor.Process(session, new ToolExecutionStartEvent
		{
			Data = new ToolExecutionStartData { ToolCallId = "tc1", ToolName = "edit" },
			Timestamp = DateTimeOffset.UtcNow
		});
		processor.Process(session, new ToolExecutionCompleteEvent
		{
			Data = new ToolExecutionCompleteData { ToolCallId = "tc1", Success = true },
			Timestamp = DateTimeOffset.UtcNow
		});
		processor.Process(session, new AssistantTurnEndEvent
		{
			Data = new AssistantTurnEndData { TurnId = "t1" },
			Timestamp = DateTimeOffset.UtcNow
		});

		// Final summary turn: turn_start clears the flag, then turn_end sets it
		processor.Process(session, new AssistantTurnStartEvent
		{
			Data = new AssistantTurnStartData { TurnId = "t2" },
			Timestamp = DateTimeOffset.UtcNow
		});
		session.AgentTurnCompleted.ShouldBeFalse("turn_start must clear AgentTurnCompleted");

		processor.Process(session, new AssistantMessageEvent
		{
			Data = new AssistantMessageData { MessageId = "done-msg", Content = "All done. Here's the summary." },
			Timestamp = DateTimeOffset.UtcNow
		});
		processor.Process(session, new AssistantTurnEndEvent
		{
			Data = new AssistantTurnEndData { TurnId = "t2" },
			Timestamp = DateTimeOffset.UtcNow
		});
		session.AgentTurnCompleted.ShouldBeTrue("turn_end must set AgentTurnCompleted");

		// No session.shutdown — user sends next message directly
		processor.Process(session, new UserMessageEvent
		{
			Data = new UserMessageData { Content = "Model popup..." },
			Timestamp = DateTimeOffset.UtcNow
		});

		session.Messages.ShouldContain(m => m.Type == MessageTypeEnum.ActivityGroup,
			"ops group must be inserted into chat");
		session.Messages.ShouldContain(m =>
			m.Type == MessageTypeEnum.Text && !m.IsUser && m.Content == "All done. Here's the summary.",
			"final summary must be promoted even without a session.shutdown");
	}

	/// <summary>
	/// Verifies that when the agent is interrupted (user sends a message while the agent is
	/// mid-turn — turn_start fired but no matching turn_end before the user message),
	/// the partial ops group summary is suppressed.
	/// </summary>
	[Fact]
	public void InterruptedTurn_SummaryIsSuppressed()
	{
		SessionModel session = CreateSession();
		SessionEventProcessor processor = CreateProcessor();

		processor.Process(session, new UserMessageEvent
		{
			Data = new UserMessageData { Content = "Start a big task" },
			Timestamp = DateTimeOffset.UtcNow
		});
		processor.Process(session, new AssistantTurnStartEvent
		{
			Data = new AssistantTurnStartData { TurnId = "t1" },
			Timestamp = DateTimeOffset.UtcNow
		});
		processor.Process(session, new ToolExecutionStartEvent
		{
			Data = new ToolExecutionStartData { ToolCallId = "tc1", ToolName = "edit" },
			Timestamp = DateTimeOffset.UtcNow
		});
		// Agent is mid-tool when user sends new message
		processor.Process(session, new AssistantTurnEndEvent
		{
			Data = new AssistantTurnEndData { TurnId = "t1" },
			Timestamp = DateTimeOffset.UtcNow
		});
		// turn_start fires for next mini-turn (agent is still in open turn)
		processor.Process(session, new AssistantTurnStartEvent
		{
			Data = new AssistantTurnStartData { TurnId = "t2" },
			Timestamp = DateTimeOffset.UtcNow
		});
		session.AgentTurnCompleted.ShouldBeFalse("turn_start after turn_end must clear the flag");

		// User interrupts — safety net: AgentTurnCompleted=false → summary suppressed
		processor.Process(session, new UserMessageEvent
		{
			Data = new UserMessageData { Content = "Actually, focus on permissions" },
			Timestamp = DateTimeOffset.UtcNow
		});

		// No standalone agent text messages should appear — summary is inside ops group
		session.Messages.ShouldNotContain(m => m.Type == MessageTypeEnum.Text && !m.IsUser,
			"interrupted turn must not promote any partial message to standalone chat");
	}

	/// <summary>
	/// Verifies that when <c>tool.execution_start</c> fires with no active group (the typical
	/// replay scenario where <c>assistant.turn_start</c> fires BEFORE the user.message echo),
	/// the new group is anchored to the last completed user message, not left floating, and
	/// any agent text messages that leaked into the chat before the tool started are absorbed
	/// into the new ops group.
	///
	/// Event order:
	///   1. User message "u1" → added to chat.
	///   2. Safety net closes prior group (user message echo arrives while agent busy).
	///   3. User message "u2" → added to chat (no active group).
	///   4. <c>assistant.message</c> "Good point…" → leaked to chat (no active group, flagged IsLeakedPreGroupMessage).
	///   5. <c>tool.execution_start</c> → ToolStartHandler creates group, anchors to u2, absorbs "Good point…".
	/// </summary>
	[Fact]
	public void ToolStartHandler_CreatesGroupWithAnchorAndAbsorbsLeakedMessage_WhenNoActiveGroup()
	{
		SessionModel session = CreateSession();
		SessionEventProcessor processor = CreateProcessor();

		// Seed the state: two completed user messages in chat, no active group
		ChatMessageModel u1 = new()
		{
			Id = "u1",
			IsUser = true,
			Content = "First message",
			IsComplete = true,
			IsPending = false,
			EventJson = null
		};
		ChatMessageModel u2 = new()
		{
			Id = "u2",
			IsUser = true,
			Content = "Immediate message",
			IsComplete = true,
			IsPending = false,
			EventJson = null
		};
		session.Messages.Add(u1);
		session.Messages.Add(u2);
		session.ActiveWorkingGroup = null;

		// Agent emits a message with no active group → leaked to chat
		processor.Process(session, new AssistantMessageEvent
		{
			Data = new AssistantMessageData { MessageId = "leaked-msg", Content = "Good point, let me check..." },
			Timestamp = DateTimeOffset.UtcNow
		});

		// Confirm it landed in chat and was flagged
		ChatMessageModel? leakedInChat = session.Messages.FirstOrDefault(m => m.Id == "leaked-msg");
		leakedInChat.ShouldNotBeNull("assistant.message with no active group must land in chat");
		leakedInChat!.IsLeakedPreGroupMessage.ShouldBeTrue("leaked message must be flagged for later absorption");

		// Tool starts → ToolStartHandler creates group, anchors to u2, absorbs leaked msg
		processor.Process(session, new ToolExecutionStartEvent
		{
			Data = new ToolExecutionStartData { ToolCallId = "tc1", ToolName = "read_file" },
			Timestamp = DateTimeOffset.UtcNow
		});

		// Leaked message must be removed from main chat
		session.Messages.ShouldNotContain(m => m.Id == "leaked-msg",
			"leaked pre-group message must be absorbed into the ops group");

		// The new ops group must be anchored to u2
		ActivityGroupModel? group = session.ActiveWorkingGroup;
		group.ShouldNotBeNull();
		group!.TriggeredByUserMessageId.ShouldBe("u2",
			"group must be anchored to the last completed user message");

		// The leaked text must be inside the group
		group.GetEventsSnapshot()
			.ShouldContain(e => e.Type == ThinkingEventTypeEnum.Message && e.Message == "Good point, let me check...",
			"leaked text must be re-parented as a thinking event inside the ops group");
	}

	/// <summary>
	/// End-to-end immediate-mode flow: the agent message that arrives after the safety net
	/// (with no active group) must NOT appear as a standalone chat message — it must end up
	/// inside the ops group, whether the group is created by <c>assistant.turn_start</c> or
	/// by <c>tool.execution_start</c>.
	///
	/// Sequence:
	///   turn_start(G1 alive) → user.message "immediate" → safety net closes G1 → u2 in chat →
	///   assistant.message "Good point…" (leaked) → tool.execution_start → group G2 absorbs leaked msg.
	/// </summary>
	[Fact]
	public void ImmediateMode_AssistantMsgBeforeTool_AbsorbedIntoOpsGroup_NotStandaloneChat()
	{
		SessionModel session = CreateSession();
		SessionEventProcessor processor = CreateProcessor();

		// Turn 1 starts, group G1 created via tool
		processor.Process(session, new UserMessageEvent
		{
			Data = new UserMessageData { Content = "First message" },
			Timestamp = DateTimeOffset.UtcNow
		});
		processor.Process(session, new ToolExecutionStartEvent
		{
			Data = new ToolExecutionStartData { ToolCallId = "tc1", ToolName = "read_file" },
			Timestamp = DateTimeOffset.UtcNow
		});
		session.ActiveWorkingGroup.ShouldNotBeNull();

		// Immediate user message: turn_start fires before the echo (simulated by having an active group)
		// then the echo arrives → safety net closes G1
		processor.Process(session, new UserMessageEvent
		{
			Data = new UserMessageData { Content = "Actually, focus on permissions" },
			Timestamp = DateTimeOffset.UtcNow
		});

		// G1 should be closed now (safety net fired)
		session.ActiveWorkingGroup.ShouldBeNull("safety net must close the prior group");

		// Agent replies with a text message — no active group → leaked to chat
		processor.Process(session, new AssistantMessageEvent
		{
			Data = new AssistantMessageData { MessageId = "reply-msg", Content = "Good point. Let me refocus..." },
			Timestamp = DateTimeOffset.UtcNow
		});

		// Tool starts → creates G2 and absorbs the leaked message
		processor.Process(session, new ToolExecutionStartEvent
		{
			Data = new ToolExecutionStartData { ToolCallId = "tc2", ToolName = "search_code" },
			Timestamp = DateTimeOffset.UtcNow
		});

		// The agent reply must NOT be a standalone chat message
		session.Messages.ShouldNotContain(m => m.Id == "reply-msg",
			"agent message emitted before tool start must be absorbed into the ops group, not remain standalone");

		// It must be inside G2
		ActivityGroupModel? g2 = session.ActiveWorkingGroup;
		g2.ShouldNotBeNull();
		g2!.GetEventsSnapshot()
			.ShouldContain(e => e.Type == ThinkingEventTypeEnum.Message && e.Message == "Good point. Let me refocus...",
			"leaked agent text must appear inside the ops group");
	}

	/// <summary>
	/// Verifies that text messages streamed to chat between the safety net closing the prior
	/// group and the new <c>assistant.turn_start</c> creating the next one are absorbed into
	/// the new ops group rather than remaining as standalone chat messages.
	///
	/// Real event order in immediate mode:
	///   1. Safety net fires (user.message echo arrives with wasAgentBusy) → closes group 1, adds "u2" to chat.
	///   2. Agent emits a delta while no active group exists → leaked to chat (after "u2").
	///   3. <c>assistant.turn_start</c> TurnId="0" fires → creates group 2, absorbs the leaked text.
	/// </summary>
	[Fact]
	public void ImmediateMode_LeakedTextMessage_AbsorbedIntoNextOpsGroup()
	{
		SessionModel session = CreateSession();
		SessionEventProcessor processor = CreateProcessor();

		// Simulate post-safety-net state:
		// - No active group (safety net already closed the prior group)
		// - Anchor user message already in session.Messages (added when safety net ran)
		session.ActiveWorkingGroup = null;
		ChatMessageModel anchorMsg = new()
		{
			Id = "u2",
			IsUser = true,
			Content = "Actually focus on permissions",
			IsComplete = true,
			IsPending = false,
			EventJson = null
		};
		session.Messages.Add(anchorMsg);

		// Agent emits a delta AFTER the anchor (no active group → leaked to chat)
		processor.Process(session, new AssistantMessageDeltaEvent
		{
			Data = new AssistantMessageDeltaData { MessageId = "leaked-msg", DeltaContent = "Reading permissions files..." },
			Timestamp = DateTimeOffset.UtcNow
		});
		processor.Process(session, new AssistantMessageEvent
		{
			Data = new AssistantMessageData { MessageId = "leaked-msg", Content = "Reading permissions files..." },
			Timestamp = DateTimeOffset.UtcNow
		});

		// Leaked text must be in chat at this point (after anchor "u2")
		session.Messages.ShouldContain(m => m.Id == "leaked-msg",
			"delta with no active group must land in chat");
		session.Messages.IndexOf(session.Messages.First(m => m.Id == "leaked-msg"))
			.ShouldBeGreaterThan(session.Messages.IndexOf(anchorMsg),
			"leaked message must appear after anchor user message");

		// turn_start fires → creates group 2 and absorbs the leaked text
		processor.Process(session, new AssistantTurnStartEvent
		{
			Data = new AssistantTurnStartData { TurnId = "0" },
			Timestamp = DateTimeOffset.UtcNow
		});

		// The leaked text must be gone from the main chat
		session.Messages.ShouldNotContain(m => m.Id == "leaked-msg",
			"leaked pre-group text must be absorbed into the ops group, not remain in chat");

		// It must exist inside the new ops group's thinking events
		ActivityGroupModel? group2 = session.ActiveWorkingGroup;
		group2.ShouldNotBeNull();
		group2!.GetEventsSnapshot()
			.ShouldContain(e => e.Type == ThinkingEventTypeEnum.Message && e.Message == "Reading permissions files...",
			"leaked text must be re-parented as a thinking event inside the new ops group");
	}
}
