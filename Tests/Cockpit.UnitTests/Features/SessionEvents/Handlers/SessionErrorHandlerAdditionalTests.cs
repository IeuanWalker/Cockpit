using Cockpit.Features.SessionEvents;
using Cockpit.Features.SessionEvents.Models;
using Cockpit.Features.Sessions.Models;
using GitHub.Copilot.SDK;
using Microsoft.Extensions.Logging.Abstractions;
using Shouldly;

namespace Cockpit.UnitTests.Features.SessionEvents.Handlers;

/// <summary>
/// Additional tests for <c>SessionErrorHandler</c> verifying message type, the null-data
/// guard, and the null-message fallback.
/// </summary>
public class SessionErrorHandlerAdditionalTests
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
	public void Handle_MessageType_IsError()
	{
		// The created message must have MessageTypeEnum.Error so the UI renders it as an error bubble
		SessionModel session = CreateSession();
		SessionEventProcessor processor = CreateProcessor();

		processor.Process(session, new SessionErrorEvent
		{
			Data = new SessionErrorData { ErrorType = "fatal", Message = "Oops" },
			Timestamp = DateTimeOffset.UtcNow
		});

		session.Messages[0].Type.ShouldBe(MessageTypeEnum.Error);
	}

	[Fact]
	public void Handle_NullData_IsIgnored()
	{
		// A SessionErrorEvent with null data must not throw and must not mutate the session
		SessionModel session = CreateSession();
		SessionEventProcessor processor = CreateProcessor();

		Should.NotThrow(() => processor.Process(session, new SessionErrorEvent
		{
			Data = null!,
			Timestamp = DateTimeOffset.UtcNow
		}));

		session.Messages.ShouldBeEmpty();
		session.Status.ShouldNotBe(SessionStatusEnum.Error);
	}

	[Fact]
	public void Handle_NullMessage_FallsBackToDefault()
	{
		// When Message is null the handler must fall back to "An error occurred"
		SessionModel session = CreateSession();
		SessionEventProcessor processor = CreateProcessor();

		processor.Process(session, new SessionErrorEvent
		{
			Data = new SessionErrorData { ErrorType = "unknown", Message = null },
			Timestamp = DateTimeOffset.UtcNow
		});

		session.Messages[0].Content.ShouldBe("An error occurred");
	}
}
