using Microsoft.AspNetCore.Components;
using Microsoft.JSInterop;

namespace Cockpit.Components.Controls;

public partial class PopupBase
{
	[Parameter] public string Title { get; set; } = string.Empty;
	[Parameter] public RenderFragment? TitleArea { get; set; }
	[Parameter] public RenderFragment? HeaderLeft { get; set; }
	[Parameter] public RenderFragment? HeaderRight { get; set; }
	[Parameter] public RenderFragment? Content { get; set; }
	[Parameter] public RenderFragment? Footer { get; set; }
	[Parameter] public string PopupStyle { get; set; } = string.Empty;
	[Parameter] public string ContentClass { get; set; } = "flex-1 min-h-0 overflow-y-auto scrollbar-thin";

	readonly IJSRuntime _jsRuntime;

	public PopupBase(IJSRuntime jsRuntime)
	{
		_jsRuntime = jsRuntime;
	}

	ElementReference _dialog;
	readonly string _titleId = $"popup-title-{Guid.NewGuid():N}";
	bool _show;
	bool _openRequested;

	public void Open()
	{
		_show = true;
		_openRequested = true;
		StateHasChanged();
	}

	public void Close()
	{
		_ = _jsRuntime.InvokeVoidAsync("cockpit.closeDialog", _dialog).AsTask();
	}

	protected override async Task OnAfterRenderAsync(bool firstRender)
	{
		if(_openRequested)
		{
			_openRequested = false;
			await _jsRuntime.InvokeVoidAsync("cockpit.openDialog", _dialog);
		}
	}

	void HandleDialogClose()
	{
		_openRequested = false;
		if(_show)
		{
			_show = false;
			StateHasChanged();
		}
	}
}
