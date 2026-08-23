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
	readonly PinnedItemsFeature _pinnedItemsFeature;
	readonly IJSRuntime _jsRuntime;

	public SessionPanel(
		IUIStateFeature uiStateFeature,
		SessionFeature sessionFeature,
		PinnedItemsFeature pinnedItemsFeature,
		IJSRuntime jsRuntime)
	{
		_uiStateFeature = uiStateFeature;
		_sessionFeature = sessionFeature;
		_pinnedItemsFeature = pinnedItemsFeature;
		_jsRuntime = jsRuntime;
	}

	DotNetObjectReference<SessionPanel>? _dotNetHelper;
	CreateSessionPopup? _createSessionPopup;
	SessionList? _sessionList;
	DeleteSessionPopup? _deletePopup;

	protected override void OnInitialized()
	{
		_sessionFeature.OnSessionStateChanged += OnSessionStateChanged;
		_uiStateFeature.OnStateChanged += OnStateChanged;
	}

	void OnSessionStateChanged(SessionStateChange change)
	{
		if(SessionPanelStateChangeFilter.RequiresPinReconciliation(change))
		{
			_ = ReconcilePinsAsync();
		}

		if(SessionPanelStateChangeFilter.IsRelevant(change))
		{
			OnStateChanged();
		}
	}

	void OnStateChanged()
	{
		InvokeAsync(StateHasChanged);
	}

	bool _isLoadingSessions = true;

	protected override async Task OnInitializedAsync()
	{
		_isLoadingSessions = true;
		Task initializePinsTask = _pinnedItemsFeature.InitializeAsync();
		await _sessionFeature.LoadExistingSessions();
		await initializePinsTask;
		await ReconcilePinsAsync();
		_isLoadingSessions = false;
	}

	async Task ReconcilePinsAsync()
	{
		if(!_sessionFeature.HasSuccessfullyLoadedExistingSessions)
		{
			return;
		}

		SessionListProjectionSource source = SessionListProjection.CreateSource(_sessionFeature.Sessions);
		HashSet<string> sessionIds = _sessionFeature.Sessions
			.Select(session => session.Id)
			.ToHashSet(StringComparer.Ordinal);
		await _pinnedItemsFeature.ReconcileAsync(
			sessionIds,
			source.ProjectGroupIds,
			source.ProjectGroupIdAliases);
	}

	bool _isRefreshingSessions = false;
	bool _showSearch = false;

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
			_sessionFeature.OnSessionStateChanged -= OnSessionStateChanged;
			_uiStateFeature.OnStateChanged -= OnStateChanged;
			_dotNetHelper?.Dispose();
		}
	}
}

static class SessionPanelStateChangeFilter
{
	const SessionChangeKind relevantChanges = SessionChangeKind.SessionCollection | SessionChangeKind.CurrentSession;
	const SessionChangeKind pinReconciliationChanges = SessionChangeKind.SessionCollection | SessionChangeKind.SessionSummary;

	public static bool IsRelevant(SessionStateChange change) => (change.Kind & relevantChanges) != 0;

	public static bool RequiresPinReconciliation(SessionStateChange change) =>
		(change.Kind & pinReconciliationChanges) != 0;
}
