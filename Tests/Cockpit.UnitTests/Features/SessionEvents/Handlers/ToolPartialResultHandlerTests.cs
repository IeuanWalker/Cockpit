using Cockpit.Features.SessionEvents;
using Cockpit.Features.SessionEvents.Models;
using Cockpit.Features.Sessions.Models;
using GitHub.Copilot.SDK;
using Microsoft.Extensions.Logging.Abstractions;
using Shouldly;

namespace Cockpit.UnitTests.Features.SessionEvents.Handlers;

public class ToolPartialResultHandlerTests
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
	public void Handle_AppendsPartialOutputToTool()
	{
		// Arrange
		SessionModel session = CreateSession();
		SessionEventProcessor processor = CreateProcessor();

		processor.Process(session, new ToolExecutionStartEvent
		{
			Data = new ToolExecutionStartData { ToolCallId = "tc1", ToolName = "bash" },
			Timestamp = DateTimeOffset.UtcNow
		});

		// Act — two partial results arrive
		processor.Process(session, new ToolExecutionPartialResultEvent
		{
			Data = new ToolExecutionPartialResultData { ToolCallId = "tc1", PartialOutput = "Hello " },
			Timestamp = DateTimeOffset.UtcNow
		});
		processor.Process(session, new ToolExecutionPartialResultEvent
		{
			Data = new ToolExecutionPartialResultData { ToolCallId = "tc1", PartialOutput = "world" },
			Timestamp = DateTimeOffset.UtcNow
		});

		// Assert — output accumulated
		ToolExecutionModel tool = session.ActiveWorkingGroup!.Tools.First();
		tool.Output.ShouldBe("Hello world");
	}

	[Fact]
	public void Handle_WithNoActiveGroup_IsIgnored()
	{
		// Arrange
		SessionModel session = CreateSession();
		SessionEventProcessor processor = CreateProcessor();

		// Act & Assert — no group, should not throw
		Should.NotThrow(() => processor.Process(session, new ToolExecutionPartialResultEvent
		{
			Data = new ToolExecutionPartialResultData { ToolCallId = "tc1", PartialOutput = "data" },
			Timestamp = DateTimeOffset.UtcNow
		}));
	}

	[Fact]
	public void Handle_WithUnknownToolCallId_IsIgnored()
	{
		// Arrange
		SessionModel session = CreateSession();
		SessionEventProcessor processor = CreateProcessor();

		processor.Process(session, new ToolExecutionStartEvent
		{
			Data = new ToolExecutionStartData { ToolCallId = "tc1", ToolName = "bash" },
			Timestamp = DateTimeOffset.UtcNow
		});

		// Act — partial result for a different tool
		Should.NotThrow(() => processor.Process(session, new ToolExecutionPartialResultEvent
		{
			Data = new ToolExecutionPartialResultData { ToolCallId = "tc999", PartialOutput = "nope" },
			Timestamp = DateTimeOffset.UtcNow
		}));

		// Assert — existing tool unaffected
		session.ActiveWorkingGroup!.Tools.First().Output.ShouldBeNull();
	}

	[Fact]
	public void Handle_PartialOutputBeforeComplete_LaterOverwrittenByComplete()
	{
		// Arrange
		SessionModel session = CreateSession();
		SessionEventProcessor processor = CreateProcessor();

		processor.Process(session, new ToolExecutionStartEvent
		{
			Data = new ToolExecutionStartData { ToolCallId = "tc1", ToolName = "bash" },
			Timestamp = DateTimeOffset.UtcNow
		});
		processor.Process(session, new ToolExecutionPartialResultEvent
		{
			Data = new ToolExecutionPartialResultData { ToolCallId = "tc1", PartialOutput = "partial output" },
			Timestamp = DateTimeOffset.UtcNow
		});

		// Act — complete overwrites with final result
		processor.Process(session, new ToolExecutionCompleteEvent
		{
			Data = new ToolExecutionCompleteData { ToolCallId = "tc1", Success = true, Result = new ToolExecutionCompleteResult { Content = "final output" } },
			Timestamp = DateTimeOffset.UtcNow
		});

		// Assert — final result replaces accumulated partial
		ToolExecutionModel tool = session.ActiveWorkingGroup!.Tools.First();
		tool.Output.ShouldBe("final output");
		tool.Status.ShouldBe(ToolStatusEnum.Success);
	}
}
