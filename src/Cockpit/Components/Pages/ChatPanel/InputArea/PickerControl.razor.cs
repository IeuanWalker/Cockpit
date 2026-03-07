using Microsoft.AspNetCore.Components;

namespace Cockpit.Components.Pages.ChatPanel.InputArea;

public partial class PickerControl : ComponentBase
{
	[Parameter] public string Title { get; set; } = string.Empty;
	[Parameter] public RenderFragment? SelectedContent { get; set; }
	[Parameter] public bool IsDisabled { get; set; }
	[Parameter] public string? TooltipText { get; set; }
	[Parameter] public RenderFragment? DropdownContent { get; set; }
	[Parameter] public string MinWidth { get; set; } = "120px";
	[Parameter] public string DropdownMinWidth { get; set; } = "200px";
	[Parameter] public string DropdownMaxHeight { get; set; } = "300px";

	bool _isOpen;

	public void Close()
	{
		_isOpen = false;
		StateHasChanged();
	}

	void Toggle() => _isOpen = !_isOpen;
}
