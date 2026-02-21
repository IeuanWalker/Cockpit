using Microsoft.AspNetCore.Components;

namespace Cockpit.Components.Controls;

public partial class PopupBase
{
	[Parameter] public string Title { get; set; } = string.Empty;
	[Parameter] public RenderFragment? TitleArea { get; set; }
	[Parameter] public RenderFragment? HeaderLeft { get; set; }
	[Parameter] public RenderFragment? HeaderRight { get; set; }
	[Parameter] public RenderFragment? Content { get; set; }
	[Parameter] public RenderFragment? Footer { get; set; }

	bool _show;

	public void Open()
	{
		_show = true;
		StateHasChanged();
	}

	public void Close()
	{
		_show = false;
		StateHasChanged();
	}
}