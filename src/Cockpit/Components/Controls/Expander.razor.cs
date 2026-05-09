using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Web;

namespace Cockpit.Components.Controls;

public partial class Expander
{
	[Parameter] public RenderFragment? Title { get; set; }
	[Parameter] public RenderFragment? Icon { get; set; }
	[Parameter] public RenderFragment? Content { get; set; }
	[Parameter] public RenderFragment? HeaderRight { get; set; }

	public bool IsOpen { get; set; }

	void ToggleExpander(MouseEventArgs args)
	{
		IsOpen = !IsOpen;
	}
}