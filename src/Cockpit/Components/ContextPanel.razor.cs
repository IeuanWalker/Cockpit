using Cockpit.Services;
using Microsoft.AspNetCore.Components;
using Microsoft.JSInterop;

namespace Cockpit.Components;

public partial class ContextPanel : ComponentBase, IDisposable
{
	DotNetObjectReference<ContextPanel>? _dotNetHelper;

	protected override void OnInitialized()
	{
		UIState.OnStateChanged += StateHasChanged;
		ContextService.OnContextChanged += StateHasChanged;

		// Initialize files dropdown as open
		UIState.SetDropdownOpen("files", true);
	}

	protected override async Task OnAfterRenderAsync(bool firstRender)
	{
		if(firstRender)
		{
			_dotNetHelper = DotNetObjectReference.Create(this);
			await JSRuntime.InvokeVoidAsync("cockpit.initializeResize", "rightResizeHandle", "rightSidebar", "right", _dotNetHelper);
		}
	}

	[JSInvokable]
	public void OnResize(int width)
	{
		UIState.SetRightSidebarWidth(width);
	}

	public void Dispose()
	{
		Dispose(true);
		GC.SuppressFinalize(this);
	}

	protected virtual void Dispose(bool disposing)
	{
		if(disposing)
		{
			UIState.OnStateChanged -= StateHasChanged;
			ContextService.OnContextChanged -= StateHasChanged;
			_dotNetHelper?.Dispose();
		}
	}
}