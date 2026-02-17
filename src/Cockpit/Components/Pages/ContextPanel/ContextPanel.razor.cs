using Cockpit.Services;
using Microsoft.AspNetCore.Components;
using Microsoft.JSInterop;

namespace Cockpit.Components.Pages.ContextPanel;

public partial class ContextPanel : ComponentBase, IDisposable
{
	DotNetObjectReference<ContextPanel>? _dotNetHelper;
	[Inject] UIStateService UIState { get; set; } = default!;
	[Inject] IJSRuntime JSRuntime { get; set; } = default!;


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
			_dotNetHelper?.Dispose();
		}
	}
}
