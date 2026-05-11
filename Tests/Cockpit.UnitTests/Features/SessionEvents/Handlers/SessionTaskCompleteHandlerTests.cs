using Cockpit.Features.SessionEvents;
using Cockpit.Features.Sessions.Models;
using GitHub.Copilot.SDK;
using Microsoft.Extensions.Logging.Abstractions;
using Shouldly;

namespace Cockpit.UnitTests.Features.SessionEvents.Handlers;

[Collection("SessionIdleEvent")]
public class SessionTaskCompleteHandlerTests
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
	public void Handle_StoresSummaryOnSession()
	{
		// Arrange
		SessionModel session = CreateSession();
		SessionEventProcessor processor = CreateProcessor();

		// Act
		processor.Process(session, new SessionTaskCompleteEvent
		{
			Data = new SessionTaskCompleteData { Summary = "All done!", Success = true },
			Timestamp = DateTimeOffset.UtcNow
		});

		// Assert
		session.PendingTaskSummary.ShouldBe("All done!");
	}

	[Fact]
	public void Handle_NullSummary_DoesNotOverwrite()
	{
		// Arrange: a summary was already stored
		SessionModel session = CreateSession();
		session.PendingTaskSummary = "existing";
		SessionEventProcessor processor = CreateProcessor();

		// Act — no summary in this event
		processor.Process(session, new SessionTaskCompleteEvent
		{
			Data = new SessionTaskCompleteData { Summary = null, Success = true },
			Timestamp = DateTimeOffset.UtcNow
		});

		// Assert — prior summary preserved
		session.PendingTaskSummary.ShouldBe("existing");
	}

	[Fact]
	public void Handle_EmptySummary_DoesNotOverwrite()
	{
		// Arrange
		SessionModel session = CreateSession();
		session.PendingTaskSummary = "existing";
		SessionEventProcessor processor = CreateProcessor();

		// Act — whitespace-only summary treated as absent
		processor.Process(session, new SessionTaskCompleteEvent
		{
			Data = new SessionTaskCompleteData { Summary = "   ", Success = true },
			Timestamp = DateTimeOffset.UtcNow
		});

		// Assert — prior summary preserved
		session.PendingTaskSummary.ShouldBe("existing");
	}

	[Fact]
	public void Handle_SummaryConsumedByIdleHandler()
	{
		// When session.idle fires after session.task_complete, the summary is used as the
		// assistant message content and PendingTaskSummary is cleared.
		SessionModel session = CreateSession();
		SessionEventProcessor processor = CreateProcessor();
		session.Messages.Add(new Cockpit.Features.SessionEvents.Models.ChatMessageModel { IsUser = true, EventJson = null });

		processor.Process(session, new ToolExecutionStartEvent
		{
			Data = new ToolExecutionStartData { ToolCallId = "tc1", ToolName = "bash" },
			Timestamp = DateTimeOffset.UtcNow
		});
		processor.Process(session, new ToolExecutionCompleteEvent
		{
			Data = new ToolExecutionCompleteData { ToolCallId = "tc1", Success = true },
			Timestamp = DateTimeOffset.UtcNow
		});
		processor.Process(session, new SessionTaskCompleteEvent
		{
			Data = new SessionTaskCompleteData { Summary = "Task complete", Success = true },
			Timestamp = DateTimeOffset.UtcNow
		});

		// Act
		processor.Process(session, new SessionIdleEvent
		{
			Data = new SessionIdleData(),
			Timestamp = DateTimeOffset.UtcNow
		});

		// Assert — summary consumed and cleared
		session.PendingTaskSummary.ShouldBeNull();
		// A summary message is inserted with the task summary content (streaming handled separately)
		session.Messages.ShouldContain(m => !m.IsUser && (m.Content == "Task complete" || m.IsStreaming));
	}
}
