using Cockpit.Features.SessionEvents.Models;
using Cockpit.Features.Sessions.Models;
using GitHub.Copilot;
using Shouldly;

namespace Cockpit.UnitTests.Features.Sessions;

public sealed class SessionConversationStateTests
{
	static SessionModel CreateSession() => new()
	{
		Id = "conversation-session",
		Title = "Conversation state",
		CreatedAt = DateTime.UtcNow,
		LastActivity = DateTime.UtcNow,
		Model = new ModelInfo { Id = "test", Name = "Test Model" },
		Context = new()
		{
			CurrentWorkingDirectory = string.Empty,
			WorkspacePath = null,
			GitRoot = null,
			Repository = null,
			Branch = null
		}
	};

	[Fact]
	public void ReplacingMessages_PublishesSnapshotThroughConversationState()
	{
		SessionModel session = CreateSession();
		ChatMessageModel message = new() { Id = "message", Content = "Hello", EventJson = null };

		session.Messages = [message];

		session.Conversation.Messages.ShouldBeSameAs(session.Messages);
		session.Conversation.MessagesSnapshot.ShouldBe([message]);
		session.MessagesSnapshot.ShouldBe([message]);
	}

	[Fact]
	public void CompatibilitySurface_ForwardsToSingleConversationState()
	{
		SessionModel session = CreateSession();
		ActivityGroupModel group = new();

		session.ActiveWorkingGroup = group;
		session.PendingMessageCount = 2;
		session.IsCompacting = true;
		session.AgentTurnCompleted = true;
		session.HasQueuedImmediateMessage = true;
		session.PendingTaskSummary = "Complete";

		session.Conversation.ActiveWorkingGroup.ShouldBeSameAs(group);
		session.Conversation.PendingMessageCount.ShouldBe(2);
		session.Conversation.IsCompacting.ShouldBeTrue();
		session.Conversation.AgentTurnCompleted.ShouldBeTrue();
		session.Conversation.HasQueuedImmediateMessage.ShouldBeTrue();
		session.Conversation.PendingTaskSummary.ShouldBe("Complete");
		(session.SessionEventLock == session.Conversation.SyncRoot).ShouldBeTrue();
	}
}
