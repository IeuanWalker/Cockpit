using Cockpit.Features.SessionEvents;
using Cockpit.Models;
using GitHub.Copilot.SDK;
using Microsoft.Extensions.Logging.Abstractions;
using Shouldly;

namespace Cockpit.UnitTests.Features.SessionEvents.Handlers;

public class AssistantMessageDeltaHandlerTests
{
	static readonly ModelInfo testModel = new() { Id = "test", Name = "Test Model" };
	static ChatSession CreateSession() => new()
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
	public void Handle_CreatesStreamingMessage()
	{
		// Arrange
		ChatSession session = CreateSession();
		SessionEventProcessor processor = CreateProcessor();

		// Act
		processor.Process(session, new AssistantMessageDeltaEvent
		{
			Data = new AssistantMessageDeltaData { MessageId = "msg1", DeltaContent = "Hello " },
			Timestamp = DateTimeOffset.UtcNow
		});

		// Assert
		session.Messages.Count.ShouldBe(1);
		session.Messages[0].IsStreaming.ShouldBeTrue();
		session.Messages[0].Content.ShouldBe("Hello ");
	}

	[Fact]
	public void Handle_AccumulatesContentAcrossDeltas()
	{
		// Arrange
		ChatSession session = CreateSession();
		SessionEventProcessor processor = CreateProcessor();
		const string messageId = "msg1";

		// Act
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

		// Assert — single message with concatenated content
		session.Messages.Count.ShouldBe(1);
		session.Messages[0].Content.ShouldBe("Hello world");
	}

	[Fact]
	public void Handle_DuringActiveGroup_DoesNotAddToMessages()
	{
		// Arrange
		ChatSession session = CreateSession();
		SessionEventProcessor processor = CreateProcessor();

		// Open a working group first
		processor.Process(session, new ToolExecutionStartEvent
		{
			Data = new ToolExecutionStartData { ToolCallId = "tc1", ToolName = "read_file" },
			Timestamp = DateTimeOffset.UtcNow
		});

		// Act — delta arrives while group is open
		processor.Process(session, new AssistantMessageDeltaEvent
		{
			Data = new AssistantMessageDeltaData { MessageId = "msg1", DeltaContent = "Thinking..." },
			Timestamp = DateTimeOffset.UtcNow
		});

		// Assert — not in chat messages; held in StreamingMessages only
		session.Messages.ShouldNotContain(m => m.IsStreaming);
		session.StreamingMessages.ShouldContainKey("msg1");
	}
}
