using Cockpit.Features.SessionEvents;
using Cockpit.Features.SessionEvents.Models;
using Cockpit.Features.Sessions.Models;
using GitHub.Copilot.SDK;
using Microsoft.Extensions.Logging.Abstractions;
using Shouldly;

namespace Cockpit.UnitTests.Features.SessionEvents.Handlers;

[Collection("SessionIdleEvent")]
public class DebouncedIdleFinalizationTests
{
	static readonly ModelInfo TestModel = new() { Id = "test", Name = "Test Model" };
	static SessionModel CreateSession() => new()
	{
		Id = "sessionId",
		Title = "Test Session",
		CreatedAt = DateTime.UtcNow,
		LastActivity = DateTime.UtcNow,
		Model = TestModel,
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
	public void DeferIdle_MovesGroupToPendingFinalization()
	{
		// Arrange
		SessionModel session = CreateSession();
		SessionEventProcessor processor = CreateProcessor();
		session.Status = SessionStatusEnum.Running;
		session.Messages.Add(new ChatMessageModel { Id = "user1", IsUser = true, Content = "Hello", EventJson = null });
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

		// Act — session.idle with deferIdle=true
		processor.Process(session, new SessionIdleEvent
		{
			Data = new SessionIdleData(),
			Timestamp = DateTimeOffset.UtcNow
		}, deferIdle: true);

		// Assert — group moved to pending, NOT inserted into Messages
		session.ActiveWorkingGroup.ShouldBeNull();
		session.PendingFinalizationGroup.ShouldNotBeNull();
		session.PendingFinalizationGroup.Tools.Count().ShouldBe(1);
		session.Messages.ShouldNotContain(m => m.Type == MessageTypeEnum.ActivityGroup);
		// Status stays Running during debounce window
		session.Status.ShouldBe(SessionStatusEnum.Running);
	}

	[Fact]
	public void DeferIdle_ContinuationTurnStart_RecoversGroup()
	{
		// Arrange
		SessionModel session = CreateSession();
		SessionEventProcessor processor = CreateProcessor();
		session.Status = SessionStatusEnum.Running;
		session.Messages.Add(new ChatMessageModel { Id = "user1", IsUser = true, Content = "Hello", EventJson = null });
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

		// Defer idle
		processor.Process(session, new SessionIdleEvent
		{
			Data = new SessionIdleData(),
			Timestamp = DateTimeOffset.UtcNow
		}, deferIdle: true);

		// Act — continuation turn_start arrives
		processor.Process(session, new AssistantTurnStartEvent
		{
			Data = new AssistantTurnStartData { TurnId = "" },
			Timestamp = DateTimeOffset.UtcNow
		}, deferIdle: true);

		// Assert — group recovered, tools from turn 1 still there
		session.PendingFinalizationGroup.ShouldBeNull();
		session.ActiveWorkingGroup.ShouldNotBeNull();
		session.ActiveWorkingGroup.Status.ShouldBe(GroupStatusEnum.Running);
		session.ActiveWorkingGroup.IsExpanded.ShouldBeTrue();
		session.ActiveWorkingGroup.Tools.Count().ShouldBe(1); // tool from turn 1
		session.Status.ShouldBe(SessionStatusEnum.Running);
		// Still no activity group in Messages
		session.Messages.ShouldNotContain(m => m.Type == MessageTypeEnum.ActivityGroup);
	}

	[Fact]
	public void DeferIdle_ContinuationThenMoreTools_AllInOneGroup()
	{
		// Arrange
		SessionModel session = CreateSession();
		SessionEventProcessor processor = CreateProcessor();
		session.Status = SessionStatusEnum.Running;
		session.Messages.Add(new ChatMessageModel { Id = "user1", IsUser = true, Content = "Hello", EventJson = null });
		session.ActiveWorkingGroup = new ActivityGroupModel
		{
			Status = GroupStatusEnum.Running,
			TriggeredByUserMessageId = "user1"
		};

		// Turn 1: tool execution
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

		// Session.idle between turns (deferred)
		processor.Process(session, new SessionIdleEvent
		{
			Data = new SessionIdleData(),
			Timestamp = DateTimeOffset.UtcNow
		}, deferIdle: true);

		// Turn 2: continuation
		processor.Process(session, new AssistantTurnStartEvent
		{
			Data = new AssistantTurnStartData { TurnId = "" },
			Timestamp = DateTimeOffset.UtcNow
		}, deferIdle: true);

		// Turn 2: more tools
		processor.Process(session, new ToolExecutionStartEvent
		{
			Data = new ToolExecutionStartData { ToolCallId = "tc2", ToolName = "edit_file" },
			Timestamp = DateTimeOffset.UtcNow
		});
		processor.Process(session, new ToolExecutionCompleteEvent
		{
			Data = new ToolExecutionCompleteData { ToolCallId = "tc2", Success = true },
			Timestamp = DateTimeOffset.UtcNow
		});

		// Final session.idle (non-deferred to simulate timer finalization)
		processor.Process(session, new SessionIdleEvent
		{
			Data = new SessionIdleData(),
			Timestamp = DateTimeOffset.UtcNow
		});

		// Assert — both tools in ONE activity group
		session.ActiveWorkingGroup.ShouldBeNull();
		List<ChatMessageModel> activityMessages = [.. session.Messages.Where(m => m.Type == MessageTypeEnum.ActivityGroup)];
		activityMessages.Count.ShouldBe(1);
		activityMessages[0].ActivityGroup!.Tools.Count().ShouldBe(2);
	}

	[Fact]
	public void DeferIdle_MetadataEvent_DoesNotFinalize()
	{
		// Arrange
		SessionModel session = CreateSession();
		SessionEventProcessor processor = CreateProcessor();
		session.Status = SessionStatusEnum.Running;
		session.Messages.Add(new ChatMessageModel { Id = "user1", IsUser = true, Content = "Hello", EventJson = null });
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

		// Defer idle
		processor.Process(session, new SessionIdleEvent
		{
			Data = new SessionIdleData(),
			Timestamp = DateTimeOffset.UtcNow
		}, deferIdle: true);

		// Act — metadata event (usage) arrives during debounce
		processor.Process(session, new AssistantUsageEvent
		{
			Data = new AssistantUsageData { Model = "gpt-4", InputTokens = 100, OutputTokens = 50 },
			Timestamp = DateTimeOffset.UtcNow
		}, deferIdle: true);

		// Assert — group still pending, NOT finalized
		session.PendingFinalizationGroup.ShouldNotBeNull();
		session.ActiveWorkingGroup.ShouldBeNull();
		session.Messages.ShouldNotContain(m => m.Type == MessageTypeEnum.ActivityGroup);
	}

	[Fact]
	public void DeferIdle_AbortEvent_FinalizesAsError()
	{
		// Arrange
		SessionModel session = CreateSession();
		SessionEventProcessor processor = CreateProcessor();
		session.Status = SessionStatusEnum.Running;
		session.Messages.Add(new ChatMessageModel { Id = "user1", IsUser = true, Content = "Hello", EventJson = null });
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

		// Defer idle
		processor.Process(session, new SessionIdleEvent
		{
			Data = new SessionIdleData(),
			Timestamp = DateTimeOffset.UtcNow
		}, deferIdle: true);

		// Act — abort arrives during debounce
		processor.Process(session, new AbortEvent
		{
			Data = new AbortData { Reason = "" },
			Timestamp = DateTimeOffset.UtcNow
		}, deferIdle: true);

		// Assert — group finalized with Error status
		session.PendingFinalizationGroup.ShouldBeNull();
		session.ActiveWorkingGroup.ShouldBeNull();
		ChatMessageModel? activityMsg = session.Messages.FirstOrDefault(m => m.Type == MessageTypeEnum.ActivityGroup);
		activityMsg.ShouldNotBeNull();
		activityMsg.ActivityGroup!.Status.ShouldBe(GroupStatusEnum.Error);
	}

	[Fact]
	public void DeferIdle_UserMessageEvent_FinalizesBeforeProcessing()
	{
		// Arrange
		SessionModel session = CreateSession();
		SessionEventProcessor processor = CreateProcessor();
		session.Status = SessionStatusEnum.Running;
		session.Messages.Add(new ChatMessageModel { Id = "user1", IsUser = true, Content = "Hello", EventJson = null });
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

		// Defer idle
		processor.Process(session, new SessionIdleEvent
		{
			Data = new SessionIdleData(),
			Timestamp = DateTimeOffset.UtcNow
		}, deferIdle: true);

		// Act — user message arrives during debounce
		processor.Process(session, new UserMessageEvent
		{
			Data = new UserMessageData { Content = "Follow up" },
			Timestamp = DateTimeOffset.UtcNow
		}, deferIdle: true);

		// Assert — old group finalized, user message added
		session.PendingFinalizationGroup.ShouldBeNull();
		session.Messages.ShouldContain(m => m.Type == MessageTypeEnum.ActivityGroup);
		session.Messages.ShouldContain(m => m.IsUser && m.Content == "Follow up");
	}

	[Fact]
	public void FinalizeIfPending_WhenPending_Finalizes()
	{
		// Arrange
		SessionModel session = CreateSession();
		SessionEventProcessor processor = CreateProcessor();
		session.Status = SessionStatusEnum.Running;
		session.Messages.Add(new ChatMessageModel { Id = "user1", IsUser = true, Content = "Hello", EventJson = null });
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

		// Defer idle
		processor.Process(session, new SessionIdleEvent
		{
			Data = new SessionIdleData(),
			Timestamp = DateTimeOffset.UtcNow
		}, deferIdle: true);

		// Act — simulate timer firing
		bool finalized = processor.FinalizeIfPending(session);

		// Assert
		finalized.ShouldBeTrue();
		session.PendingFinalizationGroup.ShouldBeNull();
		session.ActiveWorkingGroup.ShouldBeNull();
		session.Messages.ShouldContain(m => m.Type == MessageTypeEnum.ActivityGroup);
		session.Status.ShouldBe(SessionStatusEnum.Idle);
	}

	[Fact]
	public void FinalizeIfPending_WhenNotPending_ReturnsFalse()
	{
		// Arrange
		SessionModel session = CreateSession();
		SessionEventProcessor processor = CreateProcessor();

		// Act
		bool finalized = processor.FinalizeIfPending(session);

		// Assert
		finalized.ShouldBeFalse();
	}

	[Fact]
	public void DeferIdle_GenerationPreventsStaleRecovery()
	{
		// Arrange — simulates: idle → defer → timer captured gen → continuation recovers → new idle → defer
		SessionModel session = CreateSession();
		SessionEventProcessor processor = CreateProcessor();
		session.Status = SessionStatusEnum.Running;
		session.Messages.Add(new ChatMessageModel { Id = "user1", IsUser = true, Content = "Hello", EventJson = null });
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

		// First idle: defer
		processor.Process(session, new SessionIdleEvent
		{
			Data = new SessionIdleData(),
			Timestamp = DateTimeOffset.UtcNow
		}, deferIdle: true);

		int genAfterFirstIdle = session.IdleFinalizationGeneration;

		// Continuation recovers the group
		processor.Process(session, new AssistantTurnStartEvent
		{
			Data = new AssistantTurnStartData { TurnId = "" },
			Timestamp = DateTimeOffset.UtcNow
		}, deferIdle: true);

		// Generation was incremented by recovery
		session.IdleFinalizationGeneration.ShouldNotBe(genAfterFirstIdle);

		// More tools in turn 2
		processor.Process(session, new ToolExecutionStartEvent
		{
			Data = new ToolExecutionStartData { ToolCallId = "tc2", ToolName = "edit_file" },
			Timestamp = DateTimeOffset.UtcNow
		});
		processor.Process(session, new ToolExecutionCompleteEvent
		{
			Data = new ToolExecutionCompleteData { ToolCallId = "tc2", Success = true },
			Timestamp = DateTimeOffset.UtcNow
		});

		// Second idle: defer again
		processor.Process(session, new SessionIdleEvent
		{
			Data = new SessionIdleData(),
			Timestamp = DateTimeOffset.UtcNow
		}, deferIdle: true);

		// The stale timer from first idle should bail (generation changed)
		int currentGen = session.IdleFinalizationGeneration;
		currentGen.ShouldNotBe(genAfterFirstIdle);

		// Finalize via timer (simulating current generation)
		bool finalized = processor.FinalizeIfPending(session);
		finalized.ShouldBeTrue();

		// Only ONE activity group with BOTH tools
		List<ChatMessageModel> activityMessages = [.. session.Messages.Where(m => m.Type == MessageTypeEnum.ActivityGroup)];
		activityMessages.Count.ShouldBe(1);
		activityMessages[0].ActivityGroup!.Tools.Count().ShouldBe(2);
	}

	[Fact]
	public void DeferIdle_WithPendingMessages_KeepsRunning()
	{
		// Arrange — enqueue mode: pending message exists during idle
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

		// Act — session.idle with deferIdle=true and pending messages
		processor.Process(session, new SessionIdleEvent
		{
			Data = new SessionIdleData(),
			Timestamp = DateTimeOffset.UtcNow
		}, deferIdle: true);

		// Assert — NOT deferred (enqueue mode keeps running for pending message activation)
		session.PendingFinalizationGroup.ShouldBeNull();
		session.Status.ShouldBe(SessionStatusEnum.Running);
	}

	[Fact]
	public void DeferIdle_RoutineShutdown_DiscardsGroup()
	{
		// Arrange
		SessionModel session = CreateSession();
		SessionEventProcessor processor = CreateProcessor();
		session.Status = SessionStatusEnum.Running;
		session.Messages.Add(new ChatMessageModel { Id = "user1", IsUser = true, Content = "Hello", EventJson = null });
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

		// Defer idle
		processor.Process(session, new SessionIdleEvent
		{
			Data = new SessionIdleData(),
			Timestamp = DateTimeOffset.UtcNow
		}, deferIdle: true);

		// Act — routine shutdown (auto-restart)
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
		}, deferIdle: true);

		// Assert — group discarded (not finalized, not pending)
		session.PendingFinalizationGroup.ShouldBeNull();
		session.ActiveWorkingGroup.ShouldBeNull();
		session.Messages.ShouldNotContain(m => m.Type == MessageTypeEnum.ActivityGroup);
	}
}
