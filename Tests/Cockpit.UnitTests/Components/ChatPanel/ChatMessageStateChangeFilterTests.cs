using Cockpit.Components.Pages.ChatPanel;
using Cockpit.Features.Sessions;
using Shouldly;

namespace Cockpit.UnitTests.Components.ChatPanel;

public sealed class ChatMessageStateChangeFilterTests
{
	[Fact]
	public void ConversationReset_IsNotImpliedByGeneralAllStateInvalidation()
	{
		(SessionChangeKind.All & SessionChangeKind.ConversationReset).ShouldBe(SessionChangeKind.None);
		ChatMessageWindowUpdatePolicy.RequiresConversationReset(SessionChangeKind.All).ShouldBeFalse();
	}

	[Fact]
	public void IsRelevant_BackgroundSessionChangeIsIgnored()
	{
		SessionStateChange change = new("background", SessionChangeKind.ConversationContent);

		ChatMessageStateChangeFilter.IsRelevant("current", "current", change).ShouldBeFalse();
	}

	[Theory]
	[InlineData(SessionChangeKind.ConversationContent)]
	[InlineData(SessionChangeKind.ConversationStructure)]
	[InlineData(SessionChangeKind.ConversationReset)]
	[InlineData(SessionChangeKind.ConversationReset | SessionChangeKind.ConversationStructure)]
	[InlineData(SessionChangeKind.ConversationContent | SessionChangeKind.SessionSummary)]
	public void IsRelevant_CurrentConversationChangesAreHandled(SessionChangeKind kind)
	{
		SessionStateChange change = new("current", kind);

		ChatMessageStateChangeFilter.IsRelevant("current", "current", change).ShouldBeTrue();
	}

	[Theory]
	[InlineData(SessionChangeKind.SessionSummary)]
	[InlineData(SessionChangeKind.SessionCollection)]
	[InlineData(SessionChangeKind.CurrentSession)]
	public void IsRelevant_NonConversationChangeWithoutTransitionIsIgnored(SessionChangeKind kind)
	{
		SessionStateChange change = new("current", kind);

		ChatMessageStateChangeFilter.IsRelevant("current", "current", change).ShouldBeFalse();
	}

	[Fact]
	public void IsRelevant_GlobalConversationChangeIsHandled()
	{
		SessionStateChange change = new(null, SessionChangeKind.ConversationContent);

		ChatMessageStateChangeFilter.IsRelevant("current", "current", change).ShouldBeTrue();
	}

	[Fact]
	public void IsRelevant_ActualCurrentSessionTransitionIsHandled()
	{
		SessionStateChange change = new("current", SessionChangeKind.CurrentSession | SessionChangeKind.SessionSummary);

		ChatMessageStateChangeFilter.IsRelevant("current", "previous", change).ShouldBeTrue();
	}

	[Fact]
	public void IsRelevant_SameSessionReloadCoalescedWithCurrentSessionNotificationIsHandledAsReset()
	{
		SessionChangeKind kind = SessionChangeKind.CurrentSession
			| SessionChangeKind.ConversationReset
			| SessionChangeKind.ConversationStructure;
		SessionStateChange change = new("current", kind);

		ChatMessageStateChangeFilter.IsRelevant("current", "current", change).ShouldBeTrue();
		ChatMessageWindowUpdatePolicy.RequiresConversationReset(kind).ShouldBeTrue();
	}

	[Fact]
	public void IsRelevant_IdChangeWithoutCurrentSessionKindIsIgnoredUntilTransitionNotificationArrives()
	{
		SessionStateChange change = new("background", SessionChangeKind.ConversationContent);

		ChatMessageStateChangeFilter.IsRelevant("current", "previous", change).ShouldBeFalse();
	}
}
