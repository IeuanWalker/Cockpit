using Cockpit.Services;
using Microsoft.AspNetCore.Components;
using Microsoft.JSInterop;

namespace Cockpit.Components.Pages.ContextPanel;

public partial class ContextPanel : ComponentBase, IDisposable
{
	DotNetObjectReference<ContextPanel>? _dotNetHelper;
	[Inject] UIStateService _uiState { get; set; } = default!;
	[Inject] IJSRuntime _jsRuntime { get; set; } = default!;

	protected override void OnInitialized()
	{
		_uiState.OnStateChanged += OnStateChanged;
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
		_uiState.SetRightSidebarWidth(width);
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
			_uiState.OnStateChanged -= OnStateChanged;
			_dotNetHelper?.Dispose();
		}
	}
}
