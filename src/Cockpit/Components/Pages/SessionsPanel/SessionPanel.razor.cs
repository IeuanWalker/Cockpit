using Cockpit.Components.Popups;
using Cockpit.Features.Sessions;
using Cockpit.Features.Sessions.Models;
using Cockpit.Features.Timestamp;
using Cockpit.Features.UIState;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.JSInterop;

namespace Cockpit.Components.Pages.SessionsPanel;

public partial class SessionPanel : ComponentBase, IDisposable
{
	readonly IUIStateFeature _uiStateFeature;
	readonly SessionFeature _sessionFeature;
	readonly IJSRuntime _jsRuntime;
	readonly ITimestampFeature _timestampFeature;

	public SessionPanel(
		IUIStateFeature uiStateFeature,
		SessionFeature sessionFeature,
		IJSRuntime jsRuntime,
		ITimestampFeature timestampFeature)
	{
		_uiStateFeature = uiStateFeature;
		_sessionFeature = sessionFeature;
		_jsRuntime = jsRuntime;
		_timestampFeature = timestampFeature;
	}

	DotNetObjectReference<SessionPanel>? _dotNetHelper;
	CreateSessionPopup? _createSessionPopup;
	SessionList? _sessionList;
	DeleteSessionPopup? _deletePopup;

	protected override void OnInitialized()
	{
		_sessionFeature.OnStateChanged += OnStateChanged;
		_uiStateFeature.OnStateChanged += OnStateChanged;
		_timestampFeature.OnTick += OnStateChanged;
	}

	void OnStateChanged()
	{
		InvokeAsync(() =>
		{
			RefreshPastSessions();
			StateHasChanged();
		});
	}

	bool _isLoadingSessions = true;

	protected override async Task OnInitializedAsync()
	{
		_isLoadingSessions = true;
		await _sessionFeature.LoadExistingSessions();
		RefreshPastSessions();
		_isLoadingSessions = false;
	}

	bool _isRefreshingSessions = false;
	bool _showSearch = false;
	bool _pastSessionsExpanded = false;

	bool IsSearchActive => _sessionList?.IsSearchActive ?? false;

	List<SessionModel> _pastSessions = [];

	void RefreshPastSessions()
	{
		DateTime now = DateTime.UtcNow;
		_pastSessions = [.. _sessionFeature.Sessions
			.Where(s => (now - s.LastActivity).TotalDays > 7)
			.OrderByDescending(s => s.LastActivity)];
	}

	string GetTimeAgo(DateTime dateTime) => _timestampFeature.FormatRelative(dateTime);

	void ToggleSearch()
	{
		_showSearch = !_showSearch;
		if(_showSearch)
		{
			_ = _sessionList?.FocusSearchAsync();
		}
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
			RefreshPastSessions();
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

	void ShowPastDeleteDialog(SessionModel session, MouseEventArgs _)
	{
		_deletePopup?.Open(session.Id);
	}

	async Task SelectPastSession(SessionModel session)
	{
		await _sessionFeature.LoadSession(session.Id);
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
			_timestampFeature.OnTick -= OnStateChanged;
			_dotNetHelper?.Dispose();
		}
	}
}
