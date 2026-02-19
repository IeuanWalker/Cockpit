using Cockpit.Features.SessionEvents;
using Cockpit.Features.SessionEvents.Models;
using Cockpit.Features.SessionEvents.Models.Enums;
using Cockpit.Models;
using GitHub.Copilot.SDK;
using Microsoft.Extensions.Logging.Abstractions;
using Shouldly;

namespace Cockpit.UnitTests.Features.SessionEvents;

/// <summary>
/// Tests for processor-level orchestration logic: safety-net group finalization,
/// LastActivity tracking, FinalizeOpenGroup, and exception isolation.
/// Handler-specific behaviour lives in the corresponding *HandlerTests files.
/// </summary>
public class SessionEventProcessorTests
{
	static readonly ModelInfo testModel = new() { Id = "test", Name = "Test Model" };
	static ChatSession CreateSession() => new() { Model = testModel };
	static SessionEventProcessor CreateProcessor() => new(NullLogger<SessionEventProcessor>.Instance);

	[Fact]
	public void Process_UserMessage_WhenGroupOpen_FinalizesGroupFirst()
	{
		// Arrange
		ChatSession session = CreateSession();
		SessionEventProcessor processor = CreateProcessor();

		ActivityGroupModel group = new() { Status = GroupStatusEnum.Running };
		group.AddEvent(new ThinkingEventModel
		{
			Type = ThinkingEventTypeEnum.Tool,
			Tool = new ToolExecutionModel { ToolName = "read_file", ToolCallId = "tc1", Status = ToolStatusEnum.Success }
		});
		session.ActiveWorkingGroup = group;

		UserMessageEvent evt = new()
		{
			Data = new UserMessageData { Content = "Follow-up" },
			Timestamp = DateTimeOffset.UtcNow
		};

		// Act
		processor.Process(session, evt);

		// Assert — group was finalized before the new message was appended
		session.ActiveWorkingGroup.ShouldBeNull();
		session.Messages.ShouldContain(m => m.IsUser && m.Content == "Follow-up");
	}

	[Fact]
	public void Process_UserMessage_UpdatesLastActivity()
	{
		// Arrange
		ChatSession session = CreateSession();
		DateTime beforeTest = DateTime.Now.AddSeconds(-1);
		session.LastActivity = beforeTest;
		SessionEventProcessor processor = CreateProcessor();

		// Act
		processor.Process(session, new UserMessageEvent
		{
			Data = new UserMessageData { Content = "Hi" },
			Timestamp = DateTimeOffset.UtcNow
		});

		// Assert
		session.LastActivity.ShouldBeGreaterThan(beforeTest);
	}

	[Fact]
	public void Process_AssistantTurnEnd_UpdatesLastActivity()
	{
		// Arrange
		ChatSession session = CreateSession();
		DateTime beforeTest = DateTime.Now.AddSeconds(-1);
		session.LastActivity = beforeTest;
		SessionEventProcessor processor = CreateProcessor();

		// Act
		processor.Process(session, new AssistantTurnEndEvent
		{
			Data = new AssistantTurnEndData { TurnId = "t1" },
			Timestamp = DateTimeOffset.UtcNow
		});

		// Assert
		session.LastActivity.ShouldBeGreaterThan(beforeTest);
	}

	[Fact]
	public void Process_InformationalEvent_DoesNotUpdateLastActivity()
	{
		// Arrange
		ChatSession session = CreateSession();
		DateTime fixedTime = new(2020, 1, 1);
		session.LastActivity = fixedTime;
		SessionEventProcessor processor = CreateProcessor();

		// Act
		processor.Process(session, new SystemMessageEvent
		{
			Data = new SystemMessageData { Role = SystemMessageDataRole.System, Content = "init" },
			Timestamp = DateTimeOffset.UtcNow
		});

		// Assert — informational event must not touch LastActivity
		session.LastActivity.ShouldBe(fixedTime);
	}

	[Fact]
	public void FinalizeOpenGroup_WithOpenGroup_FinalizesIt()
	{
		// Arrange
		ChatSession session = CreateSession();
		SessionEventProcessor processor = CreateProcessor();
		session.Messages.Add(new ChatMessageModel { IsUser = true });

		processor.Process(session, new ToolExecutionStartEvent
		{
			Data = new ToolExecutionStartData { ToolCallId = "tc1", ToolName = "read_file" },
			Timestamp = DateTimeOffset.UtcNow
		});

		// Act
		processor.FinalizeOpenGroup(session);

		// Assert
		session.ActiveWorkingGroup.ShouldBeNull();
		session.Status.ShouldBe(SessionStatus.Idle);
	}

	[Fact]
	public void FinalizeOpenGroup_WithNoGroup_DoesNotThrow()
	{
		// Arrange
		ChatSession session = CreateSession();
		SessionEventProcessor processor = CreateProcessor();

		// Act & Assert
		Should.NotThrow(() => processor.FinalizeOpenGroup(session));
	}

	[Fact]
	public void Process_WhenNoMatchingGroup_DoesNotPropagate()
	{
		// ToolComplete with no active group — handler exits early; processor must not throw
		ChatSession session = CreateSession();
		SessionEventProcessor processor = CreateProcessor();

		Should.NotThrow(() => processor.Process(session, new ToolExecutionCompleteEvent
		{
			Data = new ToolExecutionCompleteData { ToolCallId = "nonexistent", Success = true },
			Timestamp = DateTimeOffset.UtcNow
		}));
	}
}
