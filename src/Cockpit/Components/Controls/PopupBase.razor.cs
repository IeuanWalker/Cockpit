using Microsoft.AspNetCore.Components;

namespace Cockpit.Components.Controls;

public partial class PopupBase
{
	[Parameter] public bool Show { get; set; }
	[Parameter] public EventCallback OnClose { get; set; }
	[Parameter] public string Title { get; set; } = string.Empty;
	[Parameter] public RenderFragment? HeaderLeft { get; set; }
	[Parameter] public RenderFragment? HeaderRight { get; set; }
	[Parameter] public RenderFragment? Content { get; set; }

	async Task Close()
	{
		Show = false;
		await OnClose.InvokeAsync();
	}
}