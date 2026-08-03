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

		session.Conversation.AddMessage(second);

		firstSnapshot.ShouldBe([first]);
		session.Conversation.MessagesSnapshot.ShouldBe([first]);

		session.Conversation.PublishMessagesSnapshot();

		firstSnapshot.ShouldBe([first]);
		session.Conversation.MessagesSnapshot.ShouldBe([first, second]);
	}

	[Fact]
	public void ContentMutation_RetainsSnapshotIdentity()
	{
		SessionModel session = CreateSession();
		ChatMessageModel message = new() { Id = "message", Content = "Initial", EventJson = null };
		session.Conversation.ReplaceMessages([message]);
		ImmutableArray<ChatMessageModel> snapshot = session.Conversation.MessagesSnapshot;

		message.Content = "Streaming update";

		session.Conversation.PublishMessagesSnapshotIfChanged().ShouldBeFalse();
		session.Conversation.MessagesSnapshot.Equals(snapshot).ShouldBeTrue();
		session.Conversation.MessagesSnapshot[0].Content.ShouldBe("Streaming update");
	}

	[Fact]
	public void ControlledStructuralMutation_ReplacesSnapshotIdentity()
	{
		SessionModel session = CreateSession();
		ChatMessageModel first = new() { Id = "first", Content = "First", EventJson = null };
		ChatMessageModel second = new() { Id = "second", Content = "Second", EventJson = null };
		session.Conversation.ReplaceMessages([first]);
		ImmutableArray<ChatMessageModel> snapshot = session.Conversation.MessagesSnapshot;

		session.Conversation.AddMessage(second);

		session.Conversation.PublishMessagesSnapshotIfChanged().ShouldBeTrue();
		session.Conversation.MessagesSnapshot.Equals(snapshot).ShouldBeFalse();
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
	public void ClearMessages_ReleasesRetainedListCapacity()
	{
		SessionModel session = CreateSession();
		for(int i = 0; i < 256; i++)
		{
			session.Conversation.AddMessage(new ChatMessageModel
			{
				Id = $"message-{i}",
				Content = "Message",
				EventJson = null
			});
		}
		session.Conversation.PublishMessagesSnapshot();
		session.Conversation.RetainedMessageCapacity.ShouldBeGreaterThan(0);

		session.Conversation.ClearMessages();

		session.Conversation.Messages.ShouldBeEmpty();
		session.Conversation.RetainedMessageCapacity.ShouldBe(0);
		session.Conversation.MessagesSnapshot.ShouldBeEmpty();
	}

	[Fact]
	public void Messages_ExposeReadOnlyContractsAndRejectStructuralMutation()
	{
		SessionModel session = CreateSession();
		ChatMessageModel message = new() { Id = "message", Content = "Hello", EventJson = null };
		session.Conversation.AddMessage(message);

		typeof(SessionConversationState).GetProperty(nameof(SessionConversationState.Messages))!.PropertyType.ShouldBe(typeof(IReadOnlyList<ChatMessageModel>));
		typeof(SessionModel).GetProperty(nameof(SessionModel.Messages))!.PropertyType.ShouldBe(typeof(IReadOnlyList<ChatMessageModel>));

		IList<ChatMessageModel> runtimeView = session.Messages.ShouldBeAssignableTo<IList<ChatMessageModel>>();
		Should.Throw<NotSupportedException>(() => runtimeView.Add(new ChatMessageModel { EventJson = null }));
		session.Messages.ShouldBe([message]);
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
