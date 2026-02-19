using Cockpit.Features.SessionEvents;
using Cockpit.Models;
using GitHub.Copilot.SDK;
using Microsoft.Extensions.Logging.Abstractions;
using Shouldly;

namespace Cockpit.UnitTests.Features.SessionEvents.Handlers;

public class UserMessageHandlerTests
{
	static readonly ModelInfo testModel = new() { Id = "test", Name = "Test Model" };
	static ChatSession CreateSession() => new() { Model = testModel };
	static SessionEventProcessor CreateProcessor() => new(NullLogger<SessionEventProcessor>.Instance);

	[Fact]
	public void Handle_AddsMessageToSession()
	{
		// Arrange
		ChatSession session = CreateSession();
		SessionEventProcessor processor = CreateProcessor();
		UserMessageEvent evt = new()
		{
			Data = new UserMessageData { Content = "Hello world" },
			Timestamp = DateTimeOffset.UtcNow
		};

		// Act
		processor.Process(session, evt);

		// Assert
		session.Messages.Count.ShouldBe(1);
		session.Messages[0].Content.ShouldBe("Hello world");
		session.Messages[0].IsUser.ShouldBeTrue();
	}

	[Fact]
	public void Handle_SetsStatusToRunning()
	{
		// Arrange
		ChatSession session = CreateSession();
		SessionEventProcessor processor = CreateProcessor();
		UserMessageEvent evt = new()
		{
			Data = new UserMessageData { Content = "Ping" },
			Timestamp = DateTimeOffset.UtcNow
		};

		// Act
		processor.Process(session, evt);

		// Assert
		session.Status.ShouldBe(SessionStatus.Running);
	}
}
