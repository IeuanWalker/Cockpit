using Cockpit.Components.Popups;
using Cockpit.Features.Sessions;
using Cockpit.Features.UIState;
using Microsoft.AspNetCore.Components;
using Microsoft.JSInterop;

namespace Cockpit.Components.Pages.SessionsPanel;

public partial class SessionPanel : ComponentBase, IDisposable
{
	[Inject] UIStateFeature _uiState { get; set; } = default!;
	[Inject] SessionFeature _sessionManager { get; set; } = default!;
	[Inject] IJSRuntime _jsRuntime { get; set; } = default!;

	DotNetObjectReference<SessionPanel>? _dotNetHelper;
	CreateSessionPopup? _createSessionPopup;

	protected override void OnInitialized()
	{
		_sessionManager.OnStateChanged += OnStateChanged;
		_uiState.OnStateChanged += OnStateChanged;
	}

	void OnStateChanged()
	{
		InvokeAsync(StateHasChanged);
	}

	bool _isLoadingSessions = true;

	protected override async Task OnInitializedAsync()
	{
		_isLoadingSessions = true;
		await _sessionManager.LoadExistingSessions();
		_isLoadingSessions = false;
	}

	bool _isRefreshingSessions = false;
	async Task RefreshSessions()
	{
		if(_isRefreshingSessions)
		{
			return;
		}

		_isRefreshingSessions = true;
		try
		{
			Task loadTask = _sessionManager.LoadExistingSessions();
			Task delayTask = Task.Delay(1000);
			await Task.WhenAll(loadTask, delayTask);
		}
		finally
		{
			_isRefreshingSessions = false;
		}
	}

	protected override async Task OnAfterRenderAsync(bool firstRender)
	{
		if(firstRender)
		{
			_dotNetHelper = DotNetObjectReference.Create(this);
			await _jsRuntime.InvokeVoidAsync("cockpit.initializeResize", "leftResizeHandle", "leftSidebar", "left", _dotNetHelper);
		}
	}

	[JSInvokable]
	public void OnResize(int width)
	{
		_uiState.SetLeftSidebarWidth(width);
	}

	void CreateNewSession()
	{
		try
		{
			_createSessionPopup?.Open();
		}
		catch(Exception ex)
		{
			Console.Error.WriteLine($"Failed to open directory dialog: {ex.Message}");
		}
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
			_sessionManager.OnStateChanged -= OnStateChanged;
			_uiState.OnStateChanged -= OnStateChanged;
			_dotNetHelper?.Dispose();
		}
	}
}
