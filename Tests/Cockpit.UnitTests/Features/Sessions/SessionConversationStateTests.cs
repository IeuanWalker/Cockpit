using System.Collections.Immutable;
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

		session.Conversation.ReplaceMessages([message]);

		session.Conversation.Messages.ShouldBeSameAs(session.Messages);
		session.Conversation.MessagesSnapshot.ShouldBe([message]);
		session.MessagesSnapshot.ShouldBe([message]);
	}

	[Fact]
	public void PublishedSnapshot_RemainsStableUntilNextExplicitPublication()
	{
		SessionModel session = CreateSession();
		ChatMessageModel first = new() { Id = "first", Content = "First", EventJson = null };
		ChatMessageModel second = new() { Id = "second", Content = "Second", EventJson = null };
		session.Conversation.ReplaceMessages([first]);
		ImmutableArray<ChatMessageModel> firstSnapshot = session.Conversation.MessagesSnapshot;

		session.Conversation.Messages.Add(second);

		firstSnapshot.ShouldBe([first]);
		session.Conversation.MessagesSnapshot.ShouldBe([first]);

		session.Conversation.PublishMessagesSnapshot();

		firstSnapshot.ShouldBe([first]);
		session.Conversation.MessagesSnapshot.ShouldBe([first, second]);
	}

	[Fact]
	public void ReplacingMessages_DoesNotRetainCallersMutableCollection()
	{
		SessionModel session = CreateSession();
		ChatMessageModel message = new() { Id = "message", Content = "Hello", EventJson = null };
		List<ChatMessageModel> source = [message];

		session.Conversation.ReplaceMessages(source);
		source.Clear();

		session.Conversation.Messages.ShouldBe([message]);
		session.Conversation.MessagesSnapshot.ShouldBe([message]);
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
