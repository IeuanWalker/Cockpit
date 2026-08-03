using Cockpit.Features.Sessions;
using Shouldly;

namespace Cockpit.UnitTests.Features.Sessions;

public sealed class SessionStateChangeFilterTests
{
	const SessionChangeKind relevantKinds = SessionChangeKind.CurrentSession | SessionChangeKind.SessionSummary;

	[Fact]
	public void BackgroundSessionChange_IsIgnored()
	{
		SessionStateChange change = new("background", SessionChangeKind.SessionSummary);

		SessionStateChangeFilter.IsRelevantToCurrentSession("current", change, relevantKinds)
			.ShouldBeFalse();
	}

	[Fact]
	public void CurrentSessionChange_IsHandled()
	{
		SessionStateChange change = new("current", SessionChangeKind.SessionSummary);

		SessionStateChangeFilter.IsRelevantToCurrentSession("current", change, relevantKinds)
			.ShouldBeTrue();
	}

	[Fact]
	public void GlobalRelevantChange_IsHandled()
	{
		SessionStateChange change = new(null, SessionChangeKind.SessionSummary);

		SessionStateChangeFilter.IsRelevantToCurrentSession("current", change, relevantKinds)
			.ShouldBeTrue();
	}

	[Fact]
	public void IrrelevantKind_IsIgnoredEvenForCurrentSession()
	{
		SessionStateChange change = new("current", SessionChangeKind.ConversationContent);

		SessionStateChangeFilter.IsRelevantToCurrentSession("current", change, relevantKinds)
			.ShouldBeFalse();
	}

	[Fact]
	public void SelectionChange_IsHandledRegardlessOfNewSessionId()
	{
		SessionStateChange change = new("new-current", SessionChangeKind.CurrentSession);

		SessionStateChangeFilter.IsRelevantToCurrentSession(null, change, relevantKinds)
			.ShouldBeTrue();
	}
}
