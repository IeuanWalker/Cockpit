using Cockpit.Components;
using Cockpit.Features.Sessions;
using Shouldly;

namespace Cockpit.UnitTests.Components;

public sealed class MainStateChangeFilterTests
{
	[Fact]
	public void CurrentSessionTransition_IsHandled()
	{
		SessionStateChange change = new("next", SessionChangeKind.CurrentSession);

		MainStateChangeFilter.IsCurrentSessionTransition("next", "previous", change).ShouldBeTrue();
	}

	[Theory]
	[InlineData(SessionChangeKind.ConversationContent, "next", "previous")]
	[InlineData(SessionChangeKind.ConversationStructure, "next", "next")]
	[InlineData(SessionChangeKind.SessionSummary, "next", "next")]
	[InlineData(SessionChangeKind.CurrentSession, "next", "next")]
	public void NonTransitionChange_IsIgnored(
		SessionChangeKind kind,
		string currentSessionId,
		string previousSessionId)
	{
		SessionStateChange change = new(currentSessionId, kind);

		MainStateChangeFilter.IsCurrentSessionTransition(currentSessionId, previousSessionId, change).ShouldBeFalse();
	}
}
