using Cockpit.Features.SessionEvents;
using Cockpit.Features.Sessions.Models;
using GitHub.Copilot.SDK;
using Microsoft.Extensions.Logging.Abstractions;
using Shouldly;

namespace Cockpit.UnitTests.Features.SessionEvents.Handlers;

public class AssistantMessageHandlerTests
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
	public void Handle_FinalizesStreamingMessage()
	{
		// Arrange
		SessionModel session = CreateSession();
		SessionEventProcessor processor = CreateProcessor();
		const string messageId = "msg1";

		// Build up a streaming message
		processor.Process(session, new AssistantMessageDeltaEvent
		{
			Data = new AssistantMessageDeltaData { MessageId = messageId, DeltaContent = "Hello " },
			Timestamp = DateTimeOffset.UtcNow
		});
		processor.Process(session, new AssistantMessageDeltaEvent
		{
			Data = new AssistantMessageDeltaData { MessageId = messageId, DeltaContent = "world" },
			Timestamp = DateTimeOffset.UtcNow
		});

		// Act
		processor.Process(session, new AssistantMessageEvent
		{
			Data = new AssistantMessageData { MessageId = messageId, Content = "Hello world" },
			Timestamp = DateTimeOffset.UtcNow
		});

		// Assert — streaming flag cleared, final content set
		session.Messages.Count.ShouldBe(1);
		session.Messages[0].IsStreaming.ShouldBeFalse();
		session.Messages[0].IsComplete.ShouldBeTrue();
		session.Messages[0].Content.ShouldBe("Hello world");
		session.StreamingMessages.ShouldBeEmpty();
	}

	[Fact]
	public void Handle_WithNoStreamingMessage_AddsDirectly()
	{
		// Arrange
		SessionModel session = CreateSession();
		SessionEventProcessor processor = CreateProcessor();

		// Act
		processor.Process(session, new AssistantMessageEvent
		{
			Data = new AssistantMessageData { MessageId = "msg1", Content = "Direct response" },
			Timestamp = DateTimeOffset.UtcNow
		});

		// Assert
		session.Messages.Count.ShouldBe(1);
		session.Messages[0].Content.ShouldBe("Direct response");
		session.Messages[0].IsUser.ShouldBeFalse();
		session.Messages[0].IsComplete.ShouldBeTrue();
	}

	[Fact]
	public void Handle_DuringActiveGroup_GoesToThinkingPanel()
	{
		// Arrange
		SessionModel session = CreateSession();
		SessionEventProcessor processor = CreateProcessor();

		// Open a working group
		processor.Process(session, new ToolExecutionStartEvent
		{
			Data = new ToolExecutionStartData { ToolCallId = "tc1", ToolName = "read_file" },
			Timestamp = DateTimeOffset.UtcNow
		});

		// Act — assistant message arrives while group is open
		processor.Process(session, new AssistantMessageEvent
		{
			Data = new AssistantMessageData { MessageId = "msg1", Content = "Intermediate thought" },
			Timestamp = DateTimeOffset.UtcNow
		});

		// Assert — message goes to thinking events, not chat messages
		session.Messages.ShouldBeEmpty();
		session.ActiveWorkingGroup!.GetEventsSnapshot()
			.ShouldContain(e => e.Message == "Intermediate thought");
	}
}
