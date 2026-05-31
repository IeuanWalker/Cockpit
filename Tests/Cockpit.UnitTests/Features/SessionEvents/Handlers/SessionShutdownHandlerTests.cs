using Cockpit.Features.SessionEvents;
using Cockpit.Features.SessionEvents.Models;
using Cockpit.Features.Sessions.Models;
using GitHub.Copilot;
using Microsoft.Extensions.Logging.Abstractions;
using Shouldly;

namespace Cockpit.UnitTests.Features.SessionEvents.Handlers;

public class SessionShutdownHandlerTests
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

	static SessionShutdownData CreateShutdownData(ShutdownType type) => new()
	{
		ShutdownType = type,
		CodeChanges = new ShutdownCodeChanges { FilesModified = [], LinesAdded = 0, LinesRemoved = 0 },
		ModelMetrics = new Dictionary<string, ShutdownModelMetric>(),
		SessionStartTime = 0,
		TotalApiDurationMs = 0,
		TotalPremiumRequests = 0
	};

	[Fact]
	public void Handle_RoutineShutdown_DoesNotFinalizeActiveGroup()
	{
		// Arrange — routine == auto-restart; the session continues, so the group must be preserved
		SessionModel session = CreateSession();
		SessionEventProcessor processor = CreateProcessor();
		session.Status = SessionStatusEnum.Running;

		processor.Process(session, new ToolExecutionStartEvent
		{
			Data = new ToolExecutionStartData { ToolCallId = "tc1", ToolName = "read_file" },
			Timestamp = DateTimeOffset.UtcNow
		});

		// Act
		processor.Process(session, new SessionShutdownEvent
		{
			Data = CreateShutdownData(ShutdownType.Routine),
			Timestamp = DateTimeOffset.UtcNow
		});

		// Assert — group is still open; routine shutdown must NOT invoke SessionIdleHandler
		session.ActiveWorkingGroup.ShouldNotBeNull();
		session.Status.ShouldNotBe(SessionStatusEnum.Idle);
	}

	[Fact]
	public void Handle_RoutineShutdown_WithNoGroup_DoesNotSetIdle()
	{
		// A routine shutdown with no active group must leave session status unchanged
		SessionModel session = CreateSession();
		session.Status = SessionStatusEnum.Running;
		SessionEventProcessor processor = CreateProcessor();

		processor.Process(session, new SessionShutdownEvent
		{
			Data = CreateShutdownData(ShutdownType.Routine),
			Timestamp = DateTimeOffset.UtcNow
		});

		session.Status.ShouldBe(SessionStatusEnum.Running);
	}

	[Fact]
	public void Handle_ErrorShutdown_FinalizesActiveGroup()
	{
		// Arrange — non-routine shutdown ends the session for real; the active group must be closed
		SessionModel session = CreateSession();
		SessionEventProcessor processor = CreateProcessor();
		session.Messages.Add(new ChatMessageModel { IsUser = true, EventJson = null });

		processor.Process(session, new ToolExecutionStartEvent
		{
			Data = new ToolExecutionStartData { ToolCallId = "tc1", ToolName = "read_file" },
			Timestamp = DateTimeOffset.UtcNow
		});

		// Act
		processor.Process(session, new SessionShutdownEvent
		{
			Data = CreateShutdownData(ShutdownType.Error),
			Timestamp = DateTimeOffset.UtcNow
		});

		// Assert — SessionIdleHandler was invoked; group finalized and activity message inserted
		session.ActiveWorkingGroup.ShouldBeNull();
		session.Status.ShouldBe(SessionStatusEnum.Idle);
		session.Messages.ShouldContain(m => m.Type == MessageTypeEnum.ActivityGroup);
	}

	[Fact]
	public void Handle_ErrorShutdown_WithNoActiveGroup_SetsIdle()
	{
		// Even without an active working group, a non-routine shutdown must transition to Idle
		SessionModel session = CreateSession();
		session.Status = SessionStatusEnum.Running;
		SessionEventProcessor processor = CreateProcessor();

		processor.Process(session, new SessionShutdownEvent
		{
			Data = CreateShutdownData(ShutdownType.Error),
			Timestamp = DateTimeOffset.UtcNow
		});

		session.Status.ShouldBe(SessionStatusEnum.Idle);
	}

	[Fact]
	public void Handle_NullData_IsIgnored()
	{
		// A shutdown event with null data must not throw and must not mutate the session
		SessionModel session = CreateSession();
		SessionEventProcessor processor = CreateProcessor();

		Should.NotThrow(() => processor.Process(session, new SessionShutdownEvent
		{
			Data = null!,
			Timestamp = DateTimeOffset.UtcNow
		}));
	}
}
