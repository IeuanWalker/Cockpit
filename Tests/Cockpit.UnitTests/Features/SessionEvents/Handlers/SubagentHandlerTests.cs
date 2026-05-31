using Cockpit.Features.SessionEvents;
using Cockpit.Features.SessionEvents.Models;
using Cockpit.Features.Sessions.Models;
using GitHub.Copilot;
using Microsoft.Extensions.Logging.Abstractions;
using Shouldly;

namespace Cockpit.UnitTests.Features.SessionEvents.Handlers;

public class SubagentHandlerTests
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

	// ── SubagentStarted ───────────────────────────────────────────────────────

	[Fact]
	public void SubagentStarted_CreatesActivityGroupIfNone()
	{
		// Arrange
		SessionModel session = CreateSession();
		SessionEventProcessor processor = CreateProcessor();

		// Act
		processor.Process(session, new SubagentStartedEvent
		{
			Data = new SubagentStartedData { ToolCallId = "sa1", AgentDisplayName = "CodeAgent", AgentName = "CodeAgent", AgentDescription = "" },
			Timestamp = DateTimeOffset.UtcNow
		});

		// Assert
		session.ActiveWorkingGroup.ShouldNotBeNull();
		session.ActiveWorkingGroup.Tools.ShouldContain(t => t.ToolCallId == "sa1");
	}

	[Fact]
	public void SubagentStarted_WithExistingToolExec_MarksItAsBackgroundAgent()
	{
		// Arrange: tool.execution_start fires before subagent.started
		SessionModel session = CreateSession();
		SessionEventProcessor processor = CreateProcessor();

		processor.Process(session, new ToolExecutionStartEvent
		{
			Data = new ToolExecutionStartData { ToolCallId = "sa1", ToolName = "run_agent" },
			Timestamp = DateTimeOffset.UtcNow
		});

		// Act
		processor.Process(session, new SubagentStartedEvent
		{
			Data = new SubagentStartedData { ToolCallId = "sa1", AgentDisplayName = "CodeAgent", AgentName = "CodeAgent", AgentDescription = "" },
			Timestamp = DateTimeOffset.UtcNow
		});

		// Assert — existing exec reused, display name updated, IsBackgroundAgent set
		ToolExecutionModel tool = session.ActiveWorkingGroup!.Tools.First();
		tool.ToolCallId.ShouldBe("sa1");
		tool.ToolName.ShouldBe("CodeAgent");
		tool.IsBackgroundAgent.ShouldBeTrue();
		tool.Status.ShouldBe(ToolStatusEnum.Running);
	}

	[Fact]
	public void SubagentStarted_ExistingTool_ResetToRunning_EvenIfAlreadyCompleted()
	{
		// Background tool.execution_complete may arrive before subagent.started.
		// subagent.started must force the tool back to Running.
		SessionModel session = CreateSession();
		SessionEventProcessor processor = CreateProcessor();

		processor.Process(session, new ToolExecutionStartEvent
		{
			Data = new ToolExecutionStartData { ToolCallId = "sa1", ToolName = "run_agent" },
			Timestamp = DateTimeOffset.UtcNow
		});
		processor.Process(session, new ToolExecutionCompleteEvent
		{
			Data = new ToolExecutionCompleteData { ToolCallId = "sa1", Success = true },
			Timestamp = DateTimeOffset.UtcNow
		});

		ToolExecutionModel tool = session.ActiveWorkingGroup!.Tools.First();
		tool.Status.ShouldBe(ToolStatusEnum.Success);

		// Act
		processor.Process(session, new SubagentStartedEvent
		{
			Data = new SubagentStartedData { ToolCallId = "sa1", AgentDisplayName = "CodeAgent", AgentName = "CodeAgent", AgentDescription = "" },
			Timestamp = DateTimeOffset.UtcNow
		});

		// Assert — reset to Running so the UI shows the agent is still working
		tool.Status.ShouldBe(ToolStatusEnum.Running);
		tool.EndTime.ShouldBeNull();
	}

	// ── SubagentCompleted ─────────────────────────────────────────────────────

	[Fact]
	public void SubagentCompleted_MarksSubagentSuccess()
	{
		// Arrange
		SessionModel session = CreateSession();
		SessionEventProcessor processor = CreateProcessor();

		processor.Process(session, new SubagentStartedEvent
		{
			Data = new SubagentStartedData { ToolCallId = "sa1", AgentDisplayName = "CodeAgent", AgentName = "CodeAgent", AgentDescription = "" },
			Timestamp = DateTimeOffset.UtcNow
		});

		// Act
		processor.Process(session, new SubagentCompletedEvent
		{
			Data = new SubagentCompletedData { ToolCallId = "sa1", AgentDisplayName = "CodeAgent", AgentName = "CodeAgent" },
			Timestamp = DateTimeOffset.UtcNow
		});

		// Assert
		ToolExecutionModel tool = session.ActiveWorkingGroup!.Tools.First();
		tool.Status.ShouldBe(ToolStatusEnum.Success);
		tool.IsSuccess.ShouldBeTrue();
		tool.EndTime.ShouldNotBeNull();
	}

	[Fact]
	public void SubagentCompleted_WithNoActiveGroup_IsIgnored()
	{
		SessionModel session = CreateSession();
		SessionEventProcessor processor = CreateProcessor();

		Should.NotThrow(() => processor.Process(session, new SubagentCompletedEvent
		{
			Data = new SubagentCompletedData { ToolCallId = "sa1", AgentDisplayName = "CodeAgent", AgentName = "CodeAgent" },
			Timestamp = DateTimeOffset.UtcNow
		}));
	}

	[Fact]
	public void SubagentCompleted_NullData_IsIgnored()
	{
		SessionModel session = CreateSession();
		SessionEventProcessor processor = CreateProcessor();
		session.ActiveWorkingGroup = new ActivityGroupModel { Status = GroupStatusEnum.Running };

		Should.NotThrow(() => processor.Process(session, new SubagentCompletedEvent
		{
			Data = null!,
			Timestamp = DateTimeOffset.UtcNow
		}));
	}

	// ── SubagentFailed ────────────────────────────────────────────────────────

	[Fact]
	public void SubagentFailed_MarksSubagentError()
	{
		// Arrange
		SessionModel session = CreateSession();
		SessionEventProcessor processor = CreateProcessor();

		processor.Process(session, new SubagentStartedEvent
		{
			Data = new SubagentStartedData { ToolCallId = "sa1", AgentDisplayName = "CodeAgent", AgentName = "CodeAgent", AgentDescription = "" },
			Timestamp = DateTimeOffset.UtcNow
		});

		// Act
		processor.Process(session, new SubagentFailedEvent
		{
			Data = new SubagentFailedData { ToolCallId = "sa1", Error = "Timeout", AgentDisplayName = "CodeAgent", AgentName = "CodeAgent" },
			Timestamp = DateTimeOffset.UtcNow
		});

		// Assert
		ToolExecutionModel tool = session.ActiveWorkingGroup!.Tools.First();
		tool.Status.ShouldBe(ToolStatusEnum.Error);
		tool.IsSuccess.ShouldBeFalse();
		tool.Output.ShouldBe("Timeout");
		tool.EndTime.ShouldNotBeNull();
	}

	[Fact]
	public void SubagentFailed_WithNoActiveGroup_IsIgnored()
	{
		SessionModel session = CreateSession();
		SessionEventProcessor processor = CreateProcessor();

		Should.NotThrow(() => processor.Process(session, new SubagentFailedEvent
		{
			Data = new SubagentFailedData { ToolCallId = "sa1", Error = "Timeout", AgentDisplayName = "CodeAgent", AgentName = "CodeAgent" },
			Timestamp = DateTimeOffset.UtcNow
		}));
	}

	[Fact]
	public void SubagentFailed_NullData_IsIgnored()
	{
		SessionModel session = CreateSession();
		SessionEventProcessor processor = CreateProcessor();
		session.ActiveWorkingGroup = new ActivityGroupModel { Status = GroupStatusEnum.Running };

		Should.NotThrow(() => processor.Process(session, new SubagentFailedEvent
		{
			Data = null!,
			Timestamp = DateTimeOffset.UtcNow
		}));
	}

	// ── Background agent lifecycle ────────────────────────────────────────────

	[Fact]
	public void BackgroundAgent_ToolCompleteDoesNotMarkSuccess_UntilSubagentCompleted()
	{
		// Background agent: tool.execution_complete "just means agent launched", not done.
		// tool.Status must stay Running until subagent.completed fires.
		SessionModel session = CreateSession();
		SessionEventProcessor processor = CreateProcessor();

		processor.Process(session, new ToolExecutionStartEvent
		{
			Data = new ToolExecutionStartData { ToolCallId = "sa1", ToolName = "run_agent" },
			Timestamp = DateTimeOffset.UtcNow
		});
		processor.Process(session, new SubagentStartedEvent
		{
			Data = new SubagentStartedData { ToolCallId = "sa1", AgentDisplayName = "BgAgent", AgentName = "BgAgent", AgentDescription = "" },
			Timestamp = DateTimeOffset.UtcNow
		});

		// tool.execution_complete arrives — must NOT set Success for background agents
		processor.Process(session, new ToolExecutionCompleteEvent
		{
			Data = new ToolExecutionCompleteData { ToolCallId = "sa1", Success = true },
			Timestamp = DateTimeOffset.UtcNow
		});

		ToolExecutionModel tool = session.ActiveWorkingGroup!.Tools.First();
		tool.Status.ShouldBe(ToolStatusEnum.Running, "background agent must stay Running until subagent.completed");

		// subagent.completed now finalises
		processor.Process(session, new SubagentCompletedEvent
		{
			Data = new SubagentCompletedData { ToolCallId = "sa1", AgentDisplayName = "BgAgent", AgentName = "BgAgent" },
			Timestamp = DateTimeOffset.UtcNow
		});

		tool.Status.ShouldBe(ToolStatusEnum.Success);
	}
}
