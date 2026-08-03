using Cockpit.Components.Pages.ChatPanel;
using Cockpit.Features.Sessions;
using Shouldly;

namespace Cockpit.UnitTests.Components.ChatPanel;

public sealed class ChatPanelStateChangeFilterTests
{
	[Fact]
	public void WorkingState_ForCurrentSession_IsHandled()
	{
		SessionStateChange change = new("current", SessionChangeKind.WorkingState);

		ChatPanelStateChangeFilter.IsRelevant("current", change).ShouldBeTrue();
	}

	[Fact]
	public void WorkingState_ForBackgroundSession_IsIgnored()
	{
		SessionStateChange change = new("background", SessionChangeKind.WorkingState);

		ChatPanelStateChangeFilter.IsRelevant("current", change).ShouldBeFalse();
	}

	[Fact]
	public void ConversationContent_Alone_IsIgnored()
	{
		SessionStateChange change = new("current", SessionChangeKind.ConversationContent);

		ChatPanelStateChangeFilter.IsRelevant("current", change).ShouldBeFalse();
	}
}
