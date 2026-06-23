using Cockpit.Components.Popups;
using Cockpit.Features.Sessions;
using Cockpit.Features.UIState;
using Microsoft.AspNetCore.Components;
using Microsoft.JSInterop;

namespace Cockpit.Components.Pages.SessionsPanel;

public partial class SessionPanel : ComponentBase, IDisposable
{
	readonly IUIStateFeature _uiStateFeature;
	readonly SessionFeature _sessionFeature;
	readonly IJSRuntime _jsRuntime;

	public SessionPanel(
		IUIStateFeature uiStateFeature,
		SessionFeature sessionFeature,
		IJSRuntime jsRuntime)
	{
		_uiStateFeature = uiStateFeature;
		_sessionFeature = sessionFeature;
		_jsRuntime = jsRuntime;
	}

	DotNetObjectReference<SessionPanel>? _dotNetHelper;
	CreateSessionPopup? _createSessionPopup;
	SessionList? _sessionList;
	DeleteSessionPopup? _deletePopup;

	protected override void OnInitialized()
	{
		_sessionFeature.OnStateChanged += OnStateChanged;
		_uiStateFeature.OnStateChanged += OnStateChanged;
	}

	void OnStateChanged()
	{
		InvokeAsync(StateHasChanged);
	}

	bool _isLoadingSessions = true;

	protected override async Task OnInitializedAsync()
	{
		_isLoadingSessions = true;
		await _sessionFeature.LoadExistingSessions();
		_isLoadingSessions = false;
	}

	bool _isRefreshingSessions = false;
	bool _showSearch = false;
	bool _isGroupByPanelOpen = false;

	void ToggleSearch()
	{
		_showSearch = !_showSearch;
		if(_showSearch)
		{
			_ = _sessionList?.FocusSearchAsync();
		}
	}

	async Task ToggleGroupByPanel()
	{
		if(_sessionList is null)
		{
			return;
		}

		await _sessionList.ToggleGroupByPanelFromHeader();
	}

	Task OnGroupByPanelOpenChanged(bool isOpen)
	{
		_isGroupByPanelOpen = isOpen;
		return InvokeAsync(StateHasChanged);
	}

	async Task RefreshSessions()
	{
		if(_isRefreshingSessions)
		{
			return;
		}

		_isRefreshingSessions = true;
		try
		{
			Task loadTask = _sessionFeature.RefreshExistingSessions();
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
		_uiStateFeature.SetLeftSidebarWidth(width);
	}

	void CreateNewSession()
	{
		_createSessionPopup?.Open();
	}

	async Task CreateSessionFromPathAsync(string? path)
	{
		if(_createSessionPopup is null)
		{
			return;
		}

		await _createSessionPopup.OpenAndCreateFromPathAsync(path);
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
			_sessionFeature.OnStateChanged -= OnStateChanged;
			_uiStateFeature.OnStateChanged -= OnStateChanged;
			_dotNetHelper?.Dispose();
		}
	}
}
