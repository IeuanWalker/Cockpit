using Cockpit.Components.Popups;
using Cockpit.Features.Sessions;
using Cockpit.Features.Sessions.Models;
using Cockpit.Features.Timestamp;
using Cockpit.Features.UIState;
using Humanizer;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.JSInterop;

namespace Cockpit.Components.Pages.SessionsPanel;

public partial class SessionPanel : ComponentBase, IDisposable
{
	readonly UIStateFeature _uiStateFeature;
	readonly SessionFeature _sessionFeature;
	readonly IJSRuntime _jsRuntime;
	readonly TimestampFeature _timestampFeature;

	public SessionPanel(
		UIStateFeature uiStateFeature,
		SessionFeature sessionFeature,
		IJSRuntime jsRuntime,
		TimestampFeature timestampFeature)
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
		RefreshPastSessions();
		InvokeAsync(StateHasChanged);
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

	static string GetTimeAgo(DateTime dateTime) => dateTime.Humanize();

	void ToggleSearch() => _showSearch = !_showSearch;

	async Task RefreshSessions()
	{
		if(_isRefreshingSessions)
		{
			return;
		}

		_isRefreshingSessions = true;
		try
		{
			Task loadTask = _sessionFeature.LoadExistingSessions();
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
		try
		{
			_createSessionPopup?.Open();
		}
		catch(Exception ex)
		{
			Console.Error.WriteLine($"Failed to open directory dialog: {ex.Message}");
		}
	}

	void ShowPastDeleteDialog(SessionModel session, MouseEventArgs _)
	{
		_deletePopup?.Open(session.Id);
		StateHasChanged();
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
