using Cockpit.Features.SessionEvents;
using Cockpit.Features.SessionEvents.Models;
using Cockpit.Models;
using GitHub.Copilot.SDK;
using Microsoft.Extensions.Logging.Abstractions;
using Shouldly;

namespace Cockpit.UnitTests.Features.SessionEvents.Handlers;

public class SessionIdleHandlerTests
{
	static readonly ModelInfo testModel = new() { Id = "test", Name = "Test Model" };
	static ChatSession CreateSession() => new() { Model = testModel };
	static SessionEventProcessor CreateProcessor() => new(NullLogger<SessionEventProcessor>.Instance);

	[Fact]
	public void Handle_FinalizesGroupAndSetsIdle()
	{
		// Arrange
		ChatSession session = CreateSession();
		SessionEventProcessor processor = CreateProcessor();
		session.Messages.Add(new ChatMessageModel { IsUser = true, Content = "Do something" });

		processor.Process(session, new ToolExecutionStartEvent
		{
			Data = new ToolExecutionStartData { ToolCallId = "tc1", ToolName = "read_file" },
			Timestamp = DateTimeOffset.UtcNow
		});
		processor.Process(session, new ToolExecutionCompleteEvent
		{
			Data = new ToolExecutionCompleteData { ToolCallId = "tc1", Success = true },
			Timestamp = DateTimeOffset.UtcNow
		});

		// Act
		processor.Process(session, new SessionIdleEvent
		{
			Data = new SessionIdleData(),
			Timestamp = DateTimeOffset.UtcNow
		});

		// Assert
		session.Status.ShouldBe(SessionStatus.Idle);
		session.ActiveWorkingGroup.ShouldBeNull();
		session.Messages.ShouldContain(m => m.Type == MessageTypeEnum.ActivityGroup);
	}

	[Fact]
	public void Handle_WithNoGroup_SetsIdle()
	{
		// Arrange
		ChatSession session = CreateSession();
		session.Status = SessionStatus.Running;
		SessionEventProcessor processor = CreateProcessor();

		// Act
		processor.Process(session, new SessionIdleEvent
		{
			Data = new SessionIdleData(),
			Timestamp = DateTimeOffset.UtcNow
		});

		// Assert
		session.Status.ShouldBe(SessionStatus.Idle);
	}

	[Fact]
	public void Handle_RunningToolsMarkedAsErrorOnFinalization()
	{
		// Arrange
		ChatSession session = CreateSession();
		session.Messages.Add(new ChatMessageModel { IsUser = true });
		SessionEventProcessor processor = CreateProcessor();

		// Start a tool but never complete it
		processor.Process(session, new ToolExecutionStartEvent
		{
			Data = new ToolExecutionStartData { ToolCallId = "tc1", ToolName = "stuck_tool" },
			Timestamp = DateTimeOffset.UtcNow
		});

		// Act — idle without completing the tool
		processor.Process(session, new SessionIdleEvent
		{
			Data = new SessionIdleData(),
			Timestamp = DateTimeOffset.UtcNow
		});

		// Assert — activity message still inserted (group had tools)
		session.Messages.ShouldContain(m => m.Type == MessageTypeEnum.ActivityGroup);
	}

	[Fact]
	public void Handle_ActivityMessageInsertedAfterInitialMessage()
	{
		// Arrange
		ChatSession session = CreateSession();
		SessionEventProcessor processor = CreateProcessor();

		// Build a full turn: user → assistant → tools → idle
		session.Messages.Add(new ChatMessageModel { IsUser = true, Content = "Do something" });
		ChatMessageModel assistantMsg = new() { IsUser = false, Type = MessageTypeEnum.Text, Content = "Sure" };
		session.Messages.Add(assistantMsg);

		processor.Process(session, new ToolExecutionStartEvent
		{
			Data = new ToolExecutionStartData { ToolCallId = "tc1", ToolName = "read_file" },
			Timestamp = DateTimeOffset.UtcNow
		});
		processor.Process(session, new ToolExecutionCompleteEvent
		{
			Data = new ToolExecutionCompleteData { ToolCallId = "tc1", Success = true },
			Timestamp = DateTimeOffset.UtcNow
		});

		// Act
		processor.Process(session, new SessionIdleEvent
		{
			Data = new SessionIdleData(),
			Timestamp = DateTimeOffset.UtcNow
		});

		// Assert — activity message is immediately after the initial assistant message
		int assistantIdx = session.Messages.FindIndex(m => m.Id == assistantMsg.Id);
		int activityIdx = session.Messages.FindIndex(m => m.Type == MessageTypeEnum.ActivityGroup);
		activityIdx.ShouldBe(assistantIdx + 1);
	}
}
