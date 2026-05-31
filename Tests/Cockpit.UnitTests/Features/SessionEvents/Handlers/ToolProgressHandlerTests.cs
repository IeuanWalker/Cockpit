using Cockpit.Features.SessionEvents;
using Cockpit.Features.SessionEvents.Models;
using Cockpit.Features.Sessions.Models;
using GitHub.Copilot;
using Microsoft.Extensions.Logging.Abstractions;
using Shouldly;

namespace Cockpit.UnitTests.Features.SessionEvents.Handlers;

public class ToolProgressHandlerTests
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
	public void Handle_UpdatesProgressMessage()
	{
		// Arrange
		SessionModel session = CreateSession();
		SessionEventProcessor processor = CreateProcessor();

		processor.Process(session, new ToolExecutionStartEvent
		{
			Data = new ToolExecutionStartData { ToolCallId = "tc1", ToolName = "long_task" },
			Timestamp = DateTimeOffset.UtcNow
		});

		// Act
		processor.Process(session, new ToolExecutionProgressEvent
		{
			Data = new ToolExecutionProgressData { ToolCallId = "tc1", ProgressMessage = "50% done" },
			Timestamp = DateTimeOffset.UtcNow
		});

		// Assert
		ToolExecutionModel tool = session.ActiveWorkingGroup!.Tools.First();
		tool.ProgressMessage.ShouldBe("50% done");
	}

	[Fact]
	public void Handle_WithNoActiveGroup_IsIgnored()
	{
		// Arrange
		SessionModel session = CreateSession();
		SessionEventProcessor processor = CreateProcessor();

		// Act & Assert — no group, should not throw
		Should.NotThrow(() => processor.Process(session, new ToolExecutionProgressEvent
		{
			Data = new ToolExecutionProgressData { ToolCallId = "tc1", ProgressMessage = "50%" },
			Timestamp = DateTimeOffset.UtcNow
		}));
	}
}
