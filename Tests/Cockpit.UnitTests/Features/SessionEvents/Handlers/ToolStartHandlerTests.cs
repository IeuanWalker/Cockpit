using Cockpit.Features.SessionEvents;
using Cockpit.Features.SessionEvents.Models;
using Cockpit.Features.Sessions.Models;
using GitHub.Copilot.SDK;
using Microsoft.Extensions.Logging.Abstractions;
using Shouldly;

namespace Cockpit.UnitTests.Features.SessionEvents.Handlers;

public class ToolStartHandlerTests
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
	public void Handle_CreatesActivityGroup()
	{
		// Arrange
		SessionModel session = CreateSession();
		SessionEventProcessor processor = CreateProcessor();
		ToolExecutionStartEvent evt = new()
		{
			Data = new ToolExecutionStartData { ToolCallId = "tc1", ToolName = "read_file" },
			Timestamp = DateTimeOffset.UtcNow
		};

		// Act
		processor.Process(session, evt);

		// Assert
		session.ActiveWorkingGroup.ShouldNotBeNull();
		session.ActiveWorkingGroup.Status.ShouldBe(GroupStatusEnum.Running);
		session.ActiveWorkingGroup.Tools.Count().ShouldBe(1);
		session.ActiveWorkingGroup.Tools.First().ToolName.ShouldBe("read_file");
		session.ActiveWorkingGroup.Tools.First().ToolCallId.ShouldBe("tc1");
	}

	[Fact]
	public void Handle_ReusesExistingGroup()
	{
		// Arrange
		SessionModel session = CreateSession();
		SessionEventProcessor processor = CreateProcessor();
		ActivityGroupModel existing = new() { Status = GroupStatusEnum.Running };
		session.ActiveWorkingGroup = existing;

		ToolExecutionStartEvent evt = new()
		{
			Data = new ToolExecutionStartData { ToolCallId = "tc2", ToolName = "write_file" },
			Timestamp = DateTimeOffset.UtcNow
		};

		// Act
		processor.Process(session, evt);

		// Assert
		session.ActiveWorkingGroup.ShouldBeSameAs(existing);
		session.ActiveWorkingGroup.Tools.Count().ShouldBe(1);
	}

	[Fact]
	public void Handle_WithParentCallId_NestsUnderParent()
	{
		// Arrange
		SessionModel session = CreateSession();
		SessionEventProcessor processor = CreateProcessor();

		// First: start a parent tool
		processor.Process(session, new ToolExecutionStartEvent
		{
			Data = new ToolExecutionStartData { ToolCallId = "parent1", ToolName = "subagent" },
			Timestamp = DateTimeOffset.UtcNow
		});

		// Act: start a child tool
		processor.Process(session, new ToolExecutionStartEvent
		{
			Data = new ToolExecutionStartData
			{
				ToolCallId = "child1",
				ToolName = "read_file",
				ParentToolCallId = "parent1"
			},
			Timestamp = DateTimeOffset.UtcNow
		});

		// Assert — only 1 top-level tool; child is nested
		session.ActiveWorkingGroup!.Tools.Count().ShouldBe(1);
		ToolExecutionModel parent = session.ActiveWorkingGroup.Tools.First();
		parent.GetChildrenSnapshot().Count.ShouldBe(1);
		parent.GetChildrenSnapshot()[0].ToolCallId.ShouldBe("child1");
	}

	[Fact]
	public void Handle_TracksInitialMessageId()
	{
		// Arrange
		SessionModel session = CreateSession();
		SessionEventProcessor processor = CreateProcessor();

		// Simulate a turn: user message then assistant message
		session.Messages.Add(new ChatMessageModel { IsUser = true, Content = "Do something" });
		ChatMessageModel assistantMsg = new() { IsUser = false, Type = MessageTypeEnum.Text, Content = "Sure" };
		session.Messages.Add(assistantMsg);

		ToolExecutionStartEvent evt = new()
		{
			Data = new ToolExecutionStartData { ToolCallId = "tc1", ToolName = "read_file" },
			Timestamp = DateTimeOffset.UtcNow
		};

		// Act
		processor.Process(session, evt);

		// Assert
		session.ActiveWorkingGroup!.InitialMessageId.ShouldBe(assistantMsg.Id);
	}
}
