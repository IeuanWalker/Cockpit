using Cockpit.Features.SessionEvents;
using Cockpit.Features.Sessions.Models;
using GitHub.Copilot.SDK;
using Microsoft.Extensions.Logging.Abstractions;
using Shouldly;

namespace Cockpit.UnitTests.Features.SessionEvents.Handlers;

public class SessionErrorHandlerTests
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
	public void Handle_SetsErrorStatus()
	{
		// Arrange
		SessionModel session = CreateSession();
		SessionEventProcessor processor = CreateProcessor();

		// Act
		processor.Process(session, new SessionErrorEvent
		{
			Data = new SessionErrorData { ErrorType = "fatal", Message = "Something went wrong" },
			Timestamp = DateTimeOffset.UtcNow
		});

		// Assert
		session.Status.ShouldBe(SessionStatusEnum.Error);
	}

	[Fact]
	public void Handle_AddsErrorMessageToSession()
	{
		// Arrange
		SessionModel session = CreateSession();
		SessionEventProcessor processor = CreateProcessor();

		// Act
		processor.Process(session, new SessionErrorEvent
		{
			Data = new SessionErrorData { ErrorType = "fatal", Message = "Something went wrong" },
			Timestamp = DateTimeOffset.UtcNow
		});

		// Assert
		session.Messages.ShouldContain(m => m.Content.Contains("Something went wrong"));
	}
}
