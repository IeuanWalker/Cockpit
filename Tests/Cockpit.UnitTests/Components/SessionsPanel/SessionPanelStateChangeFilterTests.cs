using Cockpit.Components.Pages.SessionsPanel;
using Cockpit.Features.Sessions;
using Shouldly;

namespace Cockpit.UnitTests.Components.SessionsPanel;

public sealed class SessionPanelStateChangeFilterTests
{
	[Theory]
	[InlineData(SessionChangeKind.SessionCollection)]
	[InlineData(SessionChangeKind.CurrentSession)]
	[InlineData(SessionChangeKind.SessionCollection | SessionChangeKind.SessionSummary)]
	public void IsRelevant_ForShellChanges_ReturnsTrue(SessionChangeKind kind)
	{
		SessionPanelStateChangeFilter.IsRelevant(new SessionStateChange("session", kind)).ShouldBeTrue();
	}

	[Theory]
	[InlineData(SessionChangeKind.None)]
	[InlineData(SessionChangeKind.ConversationContent)]
	[InlineData(SessionChangeKind.ConversationStructure)]
	[InlineData(SessionChangeKind.SessionSummary)]
	[InlineData(SessionChangeKind.ConversationContent | SessionChangeKind.SessionSummary)]
	public void IsRelevant_ForConversationAndRowOnlyChanges_ReturnsFalse(SessionChangeKind kind)
	{
		SessionPanelStateChangeFilter.IsRelevant(new SessionStateChange("session", kind)).ShouldBeFalse();
	}
}
