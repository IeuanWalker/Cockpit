using Cockpit.Features.UIState;

using Microsoft.AspNetCore.Components;
using Microsoft.JSInterop;

namespace Cockpit.Components.Pages.ContextPanel;

public partial class ContextPanel : ComponentBase, IDisposable
{
	readonly IUIStateFeature _uiStateFeature;
	readonly IJSRuntime _jsRuntime;

	public ContextPanel(IUIStateFeature uiStateFeature, IJSRuntime jsRuntime)
	{
		_uiStateFeature = uiStateFeature;
		_jsRuntime = jsRuntime;
	}

	DotNetObjectReference<ContextPanel>? _dotNetHelper;

	protected override void OnInitialized()
	{
		_uiStateFeature.OnStateChanged += OnStateChanged;
	}

	protected override async Task OnAfterRenderAsync(bool firstRender)
	{
		if(firstRender)
		{
			_dotNetHelper = DotNetObjectReference.Create(this);
			await _jsRuntime.InvokeVoidAsync("cockpit.initializeResize", "rightResizeHandle", "rightSidebar", "right", _dotNetHelper);
		}
	}

	void OnStateChanged()
	{
		InvokeAsync(StateHasChanged);
	}

	[JSInvokable]
	public void OnResize(int width)
	{
		_uiStateFeature.SetRightSidebarWidth(width);
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
			_uiStateFeature.OnStateChanged -= OnStateChanged;
			_dotNetHelper?.Dispose();
		}
	}
}
