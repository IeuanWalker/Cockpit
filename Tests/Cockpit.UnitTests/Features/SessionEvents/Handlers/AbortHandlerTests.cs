using Cockpit.Features.SessionEvents;
using Cockpit.Features.SessionEvents.Models;
using Cockpit.Features.Sessions.Models;
using GitHub.Copilot.SDK;
using Microsoft.Extensions.Logging.Abstractions;
using Shouldly;

namespace Cockpit.UnitTests.Features.SessionEvents.Handlers;

public class AbortHandlerTests
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
	public void Abort_WithActiveGroup_FinalizesGroupWithErrorStatus()
	{
		// Arrange
		SessionModel session = CreateSession();
		SessionEventProcessor processor = CreateProcessor();
		session.Messages.Add(new ChatMessageModel { IsUser = true, EventJson = null });

		processor.Process(session, new ToolExecutionStartEvent
		{
			Data = new ToolExecutionStartData { ToolCallId = "tc1", ToolName = "bash" },
			Timestamp = DateTimeOffset.UtcNow
		});

		// Act
		processor.Process(session, new AbortEvent { Data = new AbortData { Reason = "" }, Timestamp = DateTimeOffset.UtcNow });

		// Assert
		session.ActiveWorkingGroup.ShouldBeNull();
		ChatMessageModel? activityMsg = session.Messages.FirstOrDefault(m => m.Type == MessageTypeEnum.ActivityGroup);
		activityMsg.ShouldNotBeNull();
		activityMsg!.ActivityGroup!.Status.ShouldBe(GroupStatusEnum.Error);
	}

	[Fact]
	public void Abort_ClearsPendingMessages()
	{
		// Arrange
		SessionModel session = CreateSession();
		SessionEventProcessor processor = CreateProcessor();

		// Add pending user messages
		session.Messages.Add(new ChatMessageModel { IsUser = true, IsPending = true, IsComplete = true, Content = "Pending 1", EventJson = null });
		session.Messages.Add(new ChatMessageModel { IsUser = true, IsPending = true, IsComplete = true, Content = "Pending 2", EventJson = null });

		// Act
		processor.Process(session, new AbortEvent { Data = new AbortData { Reason = "" }, Timestamp = DateTimeOffset.UtcNow });

		// Assert — all pending messages cleared
		session.Messages.ShouldNotContain(m => m.IsPending);
	}

	[Fact]
	public void Abort_WithNoGroup_SetsIdleAndClearsPending()
	{
		// Arrange
		SessionModel session = CreateSession();
		SessionEventProcessor processor = CreateProcessor();
		session.Messages.Add(new ChatMessageModel { IsUser = true, IsPending = true, IsComplete = true, Content = "Queued", EventJson = null });

		// Act
		processor.Process(session, new AbortEvent { Data = new AbortData { Reason = "" }, Timestamp = DateTimeOffset.UtcNow });

		// Assert
		session.Status.ShouldBe(SessionStatusEnum.Idle);
		session.Messages.ShouldNotContain(m => m.IsPending);
	}

	[Fact]
	public void Abort_GroupHasAbortedMessage()
	{
		// Arrange
		SessionModel session = CreateSession();
		SessionEventProcessor processor = CreateProcessor();
		session.Messages.Add(new ChatMessageModel { IsUser = true, EventJson = null });

		processor.Process(session, new ToolExecutionStartEvent
		{
			Data = new ToolExecutionStartData { ToolCallId = "tc1", ToolName = "bash" },
			Timestamp = DateTimeOffset.UtcNow
		});

		// Act
		processor.Process(session, new AbortEvent { Data = new AbortData { Reason = "" }, Timestamp = DateTimeOffset.UtcNow });

		// Assert — an "Session aborted" thinking event is appended to the group
		ChatMessageModel? activityMsg = session.Messages.FirstOrDefault(m => m.Type == MessageTypeEnum.ActivityGroup);
		activityMsg.ShouldNotBeNull();
		List<ThinkingEventModel> events = activityMsg!.ActivityGroup!.GetEventsSnapshot();
		events.ShouldContain(e => e.Type == ThinkingEventTypeEnum.Message && e.Message == "Session aborted");
	}
}
