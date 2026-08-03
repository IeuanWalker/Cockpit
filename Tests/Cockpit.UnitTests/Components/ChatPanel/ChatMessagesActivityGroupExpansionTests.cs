using Cockpit.Components.Pages.ChatPanel;
using Cockpit.Features.SessionEvents.Models;
using Shouldly;

namespace Cockpit.UnitTests.Components.ChatPanel;

public sealed class ChatMessagesActivityGroupExpansionTests
{
	[Fact]
	public void SetActivityGroupExpanded_PreservesOtherExpandedGroups()
	{
		ActivityGroupModel first = new();
		ActivityGroupModel second = new();

		ChatMessages.SetActivityGroupExpanded(first, true);
		ChatMessages.SetActivityGroupExpanded(second, true);

		first.IsExpanded.ShouldBeTrue();
		second.IsExpanded.ShouldBeTrue();
	}

	[Fact]
	public void SetActivityGroupExpanded_CollapsesOnlySelectedGroup()
	{
		ActivityGroupModel first = new() { IsExpanded = true };
		ActivityGroupModel second = new() { IsExpanded = true };

		ChatMessages.SetActivityGroupExpanded(first, false);

		first.IsExpanded.ShouldBeFalse();
		second.IsExpanded.ShouldBeTrue();
	}
}
