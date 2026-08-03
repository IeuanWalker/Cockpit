using Cockpit.Components.Pages.ChatPanel;
using Cockpit.Features.SessionEvents.Models;
using Cockpit.Features.Sessions;
using Shouldly;

namespace Cockpit.UnitTests.Components.ChatPanel;

public sealed class MessageWindowStateTests
{
	static ChatMessageModel Message(bool isUser, bool wasSentLocally = false) => new()
	{
		IsUser = isUser,
		WasSentLocally = wasSentLocally,
		EventJson = null
	};

	[Fact]
	public void Reset_OpensBoundedWindowAtTail()
	{
		MessageWindowState state = new();

		state.Reset(10_000);

		state.StartIndex.ShouldBe(9_775);
		state.EndIndex.ShouldBe(10_000);
		state.Count.ShouldBe(225);
		state.IncludesTail.ShouldBeTrue();
	}

	[Fact]
	public void MoveOlder_ShiftsWithOverlapAndRemainsBounded()
	{
		MessageWindowState state = new();
		state.Reset(1_000);

		state.MoveOlder().ShouldBeTrue();

		state.StartIndex.ShouldBe(725);
		state.EndIndex.ShouldBe(950);
		state.Count.ShouldBe(225);
		state.IncludesTail.ShouldBeFalse();
	}

	[Fact]
	public void MovingAcrossBothBounds_ClampsCleanly()
	{
		MessageWindowState state = new(maximumSize: 200, shiftSize: 50);
		state.Reset(275);

		state.MoveOlder().ShouldBeTrue();
		state.MoveOlder().ShouldBeTrue();
		state.MoveOlder().ShouldBeFalse();
		state.StartIndex.ShouldBe(0);
		state.EndIndex.ShouldBe(200);

		state.MoveNewer().ShouldBeTrue();
		state.MoveNewer().ShouldBeTrue();
		state.MoveNewer().ShouldBeFalse();
		state.StartIndex.ShouldBe(75);
		state.EndIndex.ShouldBe(275);
		state.IncludesTail.ShouldBeTrue();
	}

	[Fact]
	public void Synchronize_AtTailFollowsNewMessages()
	{
		MessageWindowState state = new();
		state.Reset(500);

		state.Synchronize(503);

		state.EndIndex.ShouldBe(503);
		state.Count.ShouldBe(225);
		state.UnseenMessageCount.ShouldBe(0);
	}

	[Fact]
	public void Synchronize_WhileBrowsingHistoryKeepsWindowAndTracksUnseenMessages()
	{
		MessageWindowState state = new();
		state.Reset(500);
		state.MoveOlder();
		int start = state.StartIndex;
		int end = state.EndIndex;

		state.Synchronize(507);

		state.StartIndex.ShouldBe(start);
		state.EndIndex.ShouldBe(end);
		state.UnseenMessageCount.ShouldBe(7);
	}

	[Fact]
	public void Synchronize_ClearThenRepopulateResetsHistoryWindowToVisibleTail()
	{
		MessageWindowState state = new(maximumSize: 10, shiftSize: 5);
		state.Reset(20);
		state.MoveOlder();
		state.Synchronize(23);

		state.Synchronize(0);

		state.StartIndex.ShouldBe(0);
		state.EndIndex.ShouldBe(0);
		state.IsFollowingTail.ShouldBeTrue();
		state.UnseenMessageCount.ShouldBe(0);

		state.Synchronize(3);

		state.StartIndex.ShouldBe(0);
		state.EndIndex.ShouldBe(3);
		state.IncludesTail.ShouldBeTrue();
		state.IsFollowingTail.ShouldBeTrue();
		state.UnseenMessageCount.ShouldBe(0);
	}

	[Fact]
	public void CoalescedReplayResetWithFirstMessage_ResetsHistoricalWindowAgainstNonzeroSnapshot()
	{
		MessageWindowState state = new(maximumSize: 10, shiftSize: 5);
		state.Reset(20);
		state.MoveOlder();
		state.Synchronize(23);
		state.IsFollowingTail.ShouldBeFalse();

		SessionChangeKind coalescedKind = SessionChangeKind.ConversationReset | SessionChangeKind.ConversationStructure | SessionChangeKind.ConversationContent;
		if(ChatMessageWindowUpdatePolicy.RequiresConversationReset(coalescedKind))
		{
			// The empty snapshot was never observed: the first replay message is already present.
			state.Reset(totalCount: 1);
		}
		else
		{
			state.Synchronize(totalCount: 1);
		}

		state.StartIndex.ShouldBe(0);
		state.EndIndex.ShouldBe(1);
		state.IncludesTail.ShouldBeTrue();
		state.IsFollowingTail.ShouldBeTrue();
		state.UnseenMessageCount.ShouldBe(0);
	}

	[Fact]
	public void SameSessionHistoryReload_ResetsHistoricalWindowToReplacementSnapshotTail()
	{
		MessageWindowState state = new(maximumSize: 10, shiftSize: 5);
		state.Reset(30);
		state.MoveOlder();
		state.IsFollowingTail.ShouldBeFalse();

		SessionChangeKind coalescedKind = SessionChangeKind.CurrentSession | SessionChangeKind.ConversationReset | SessionChangeKind.ConversationStructure;
		if(ChatMessageWindowUpdatePolicy.RequiresConversationReset(coalescedKind))
		{
			state.Reset(totalCount: 18);
		}

		state.StartIndex.ShouldBe(8);
		state.EndIndex.ShouldBe(18);
		state.IncludesTail.ShouldBeTrue();
		state.IsFollowingTail.ShouldBeTrue();
		state.UnseenMessageCount.ShouldBe(0);
	}

	[Fact]
	public void Synchronize_NonzeroShrinkPreservesHistoricalBrowsingState()
	{
		MessageWindowState state = new(maximumSize: 10, shiftSize: 5);
		state.Reset(20);
		state.MoveOlder();

		state.Synchronize(14);

		state.StartIndex.ShouldBe(5);
		state.EndIndex.ShouldBe(14);
		state.IsFollowingTail.ShouldBeFalse();
		state.UnseenMessageCount.ShouldBe(0);
	}

	[Fact]
	public void Synchronize_WhenScrolledUpInsideTailWindowDoesNotForceFollow()
	{
		MessageWindowState state = new();
		state.Reset(100);
		state.SetFollowingTail(false);

		state.Synchronize(103);

		state.StartIndex.ShouldBe(0);
		state.EndIndex.ShouldBe(100);
		state.IncludesTail.ShouldBeFalse();
		state.UnseenMessageCount.ShouldBe(3);
	}

	[Fact]
	public void MoveToTail_ClearsUnseenMessages()
	{
		MessageWindowState state = new();
		state.Reset(500);
		state.MoveOlder();
		state.Synchronize(504);

		state.MoveToTail();

		state.EndIndex.ShouldBe(504);
		state.UnseenMessageCount.ShouldBe(0);
		state.IncludesTail.ShouldBeTrue();
	}

	[Fact]
	public void Reset_ForNewSessionClearsPriorWindowState()
	{
		MessageWindowState state = new();
		state.Reset(1_000);
		state.MoveOlder();
		state.Synchronize(1_005);

		state.Reset(12);

		state.StartIndex.ShouldBe(0);
		state.EndIndex.ShouldBe(12);
		state.UnseenMessageCount.ShouldBe(0);
		state.IncludesTail.ShouldBeTrue();
	}

	[Fact]
	public void NewlySentLocalUserMessage_CanForceHistoryWindowToTail()
	{
		MessageWindowState state = new(maximumSize: 10, shiftSize: 5);
		List<ChatMessageModel> messages = [.. Enumerable.Range(0, 20).Select(_ => Message(false))];
		state.Reset(messages.Count);
		state.MoveOlder();
		HashSet<ChatMessageModel> observedLocalMessages = [];
		ChatMessageWindowUpdatePolicy.ResetObservedLocallySentMessages(observedLocalMessages, messages);

		messages.Add(Message(true, wasSentLocally: true));
		bool revealLocalSend = ChatMessageWindowUpdatePolicy.ObserveNewLocallySentMessages(observedLocalMessages, messages);
		state.Synchronize(messages.Count);
		if(revealLocalSend)
		{
			state.MoveToTail();
		}

		revealLocalSend.ShouldBeTrue();
		observedLocalMessages.ShouldContain(messages[^1]);
		state.IncludesTail.ShouldBeTrue();
		state.IsFollowingTail.ShouldBeTrue();
		state.UnseenMessageCount.ShouldBe(0);
	}

	[Theory]
	[InlineData(true)]
	[InlineData(false)]
	public void RemoteUserOrAssistantAppend_DoesNotForceHistoryWindowToTail(bool isUser)
	{
		MessageWindowState state = new(maximumSize: 10, shiftSize: 5);
		List<ChatMessageModel> messages = [.. Enumerable.Range(0, 20).Select(_ => Message(false))];
		state.Reset(messages.Count);
		state.MoveOlder();
		HashSet<ChatMessageModel> observedLocalMessages = [];
		ChatMessageWindowUpdatePolicy.ResetObservedLocallySentMessages(observedLocalMessages, messages);

		messages.Add(Message(isUser));
		bool revealLocalSend = ChatMessageWindowUpdatePolicy.ObserveNewLocallySentMessages(observedLocalMessages, messages);
		state.Synchronize(messages.Count);

		revealLocalSend.ShouldBeFalse();
		state.IncludesTail.ShouldBeFalse();
		state.IsFollowingTail.ShouldBeFalse();
		state.UnseenMessageCount.ShouldBe(1);
	}

	[Fact]
	public void AlreadyObservedLocalUserMessage_DoesNotForceTailAgainOnLaterUpdates()
	{
		ChatMessageModel local = Message(true, wasSentLocally: true);
		List<ChatMessageModel> messages = [Message(false), local, Message(false)];
		HashSet<ChatMessageModel> observedLocalMessages = [];
		ChatMessageWindowUpdatePolicy.ResetObservedLocallySentMessages(observedLocalMessages, messages);

		ChatMessageWindowUpdatePolicy.ObserveNewLocallySentMessages(observedLocalMessages, messages).ShouldBeFalse();
	}

	[Fact]
	public void RemovingLatestLocalMessage_DoesNotMistakeAnOlderLocalMessageForANewSend()
	{
		ChatMessageModel olderLocal = Message(true, wasSentLocally: true);
		ChatMessageModel latestLocal = Message(true, wasSentLocally: true);
		List<ChatMessageModel> messages = [olderLocal, Message(false), latestLocal];
		HashSet<ChatMessageModel> observedLocalMessages = [];
		ChatMessageWindowUpdatePolicy.ResetObservedLocallySentMessages(observedLocalMessages, messages);

		messages.Remove(latestLocal);

		ChatMessageWindowUpdatePolicy.ObserveNewLocallySentMessages(observedLocalMessages, messages).ShouldBeFalse();
	}

	[Theory]
	[InlineData(true, false)]
	[InlineData(false, true)]
	public void TailStateSync_PublishesChangedStateOnceForCurrentGeneration(
		bool publishedIncludesTail,
		bool currentIncludesTail)
	{
		MessageWindowTailStateSync sync = new();
		sync.MarkPublished(new(7, publishedIncludesTail));

		sync.TryCreateUpdate(true, 7, currentIncludesTail, out MessageWindowTailStateUpdate update).ShouldBeTrue();
		update.ShouldBe(new MessageWindowTailStateUpdate(7, currentIncludesTail));
		sync.MarkPublished(update);

		sync.TryCreateUpdate(true, 7, currentIncludesTail, out _).ShouldBeFalse();
	}

	[Fact]
	public void TailStateSync_RequiresRepublishForANewObserverGeneration()
	{
		MessageWindowTailStateSync sync = new();
		sync.MarkPublished(new(7, false));

		sync.TryCreateUpdate(true, 8, false, out MessageWindowTailStateUpdate update).ShouldBeTrue();
		update.ShouldBe(new MessageWindowTailStateUpdate(8, false));
	}

	[Fact]
	public void TailStateSync_DoesNotPublishBeforeObserversAreReady()
	{
		MessageWindowTailStateSync sync = new();

		sync.TryCreateUpdate(false, 3, false, out _).ShouldBeFalse();
	}
}
