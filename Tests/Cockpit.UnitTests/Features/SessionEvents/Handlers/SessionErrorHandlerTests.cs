using Cockpit.Features.SessionEvents;
using Cockpit.Models;
using GitHub.Copilot.SDK;
using Microsoft.Extensions.Logging.Abstractions;
using Shouldly;

namespace Cockpit.UnitTests.Features.SessionEvents.Handlers;

public class SessionErrorHandlerTests
{
	static readonly ModelInfo testModel = new() { Id = "test", Name = "Test Model" };
	static ChatSession CreateSession() => new() { Model = testModel };
	static SessionEventProcessor CreateProcessor() => new(NullLogger<SessionEventProcessor>.Instance);

	[Fact]
	public void Handle_SetsErrorStatus()
	{
		// Arrange
		ChatSession session = CreateSession();
		SessionEventProcessor processor = CreateProcessor();

		// Act
		processor.Process(session, new SessionErrorEvent
		{
			Data = new SessionErrorData { ErrorType = "fatal", Message = "Something went wrong" },
			Timestamp = DateTimeOffset.UtcNow
		});

		// Assert
		session.Status.ShouldBe(SessionStatus.Error);
	}

	[Fact]
	public void Handle_AddsErrorMessageToSession()
	{
		// Arrange
		ChatSession session = CreateSession();
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
