using Cockpit.Features.AppSettings;
using Cockpit.Features.Sessions;
using Cockpit.Features.Sessions.Models;
using Cockpit.Features.Timestamp;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Web;

namespace Cockpit.Components.Pages.SessionsPanel;

public partial class SessionList : ComponentBase, IDisposable
{
	const float VirtualRowHeight = 48;
	const int VirtualOverscanCount = 5;

	[Parameter] public DeleteSessionPopup? DeletePopup { get; set; }
	[Parameter] public bool ShowSearch { get; set; }
	[Parameter] public EventCallback<string?> OnCreateSessionFromPath { get; set; }
	[Parameter] public EventCallback<bool> OnGroupByPanelOpenChanged { get; set; }

	readonly ITimestampFeature _timestampFeature;
	readonly SessionFeature _sessionFeature;
	readonly IAppSettingsFeature _appSettingsFeature;

	public SessionList(
		ITimestampFeature timestampFeature,
		SessionFeature sessionFeature,
		IAppSettingsFeature appSettingsFeature)
	{
		_timestampFeature = timestampFeature;
		_sessionFeature = sessionFeature;
		_appSettingsFeature = appSettingsFeature;
	}

	string _searchText = string.Empty;
	ElementReference _sessionSearch;
	bool _focusSearchRequested;
	bool _showFilterPanel;
	bool _showGroupByPanel;
	SessionListViewMode _groupByMode = SessionListViewMode.Project;
	readonly HashSet<string> _filterCwds = new(StringComparer.OrdinalIgnoreCase);
	readonly HashSet<string> _filterRepos = new(StringComparer.OrdinalIgnoreCase);
	readonly HashSet<string> _expandedCwdGroups = new(StringComparer.OrdinalIgnoreCase);
	readonly HashSet<string> _expandedProjectGroups = new(StringComparer.OrdinalIgnoreCase);
	readonly HashSet<string> _expandedProjectSessionGroups = new(StringComparer.OrdinalIgnoreCase);
	ICollection<SessionListRow> _rows = [];
	bool _projectionDirty = true;
	long _lastTimestampMinute = -1;

	string SearchText
	{
		get => _searchText;
		set
		{
			if(_searchText == value)
			{
				return;
			}

			_searchText = value;
			InvalidateProjection();
		}
	}

	ICollection<SessionListRow> Rows
	{
		get
		{
			if(_projectionDirty)
			{
				_rows = [.. SessionListProjection.Build(
					_sessionFeature.Sessions,
					new SessionListProjectionOptions(
						_groupByMode,
						_sessionFeature.CurrentSession?.Id,
						_searchText,
						_filterCwds,
						_filterRepos,
						_expandedProjectGroups,
						_expandedProjectSessionGroups))];
				_projectionDirty = false;
			}

			return _rows;
		}
	}

	protected override void OnParametersSet()
	{
		if(!ShowSearch &&
		   (!string.IsNullOrEmpty(_searchText) || _filterCwds.Count > 0 || _filterRepos.Count > 0))
		{
			_searchText = string.Empty;
			_focusSearchRequested = false;
			_filterCwds.Clear();
			_filterRepos.Clear();
			_showFilterPanel = false;
			InvalidateProjection();
		}
	}

	protected override async Task OnAfterRenderAsync(bool firstRender)
	{
		if(_focusSearchRequested && ShowSearch)
		{
			_focusSearchRequested = false;
			await _sessionSearch.FocusAsync();
		}
	}

	bool IsSearchActive => ShowSearch &&
		(!string.IsNullOrWhiteSpace(_searchText) || _filterCwds.Count > 0 || _filterRepos.Count > 0);
	bool HasActiveFilters => _filterCwds.Count > 0 || _filterRepos.Count > 0;
	bool IsGroupingByProject => _groupByMode == SessionListViewMode.Project;

	IEnumerable<string> UniqueCwds => _sessionFeature.Sessions
		.Select(session => SessionListProjection.NormalizePath(session.Context.CurrentWorkingDirectory ?? string.Empty))
		.Where(path => !string.IsNullOrEmpty(path))
		.Distinct(StringComparer.OrdinalIgnoreCase)
		.OrderBy(path => path, StringComparer.OrdinalIgnoreCase);

	IEnumerable<IGrouping<string, string>> GroupedCwds => UniqueCwds
		.GroupBy(
			path =>
			{
				string name = Path.GetFileName(path);
				return string.IsNullOrEmpty(name) ? path : name;
			},
			StringComparer.OrdinalIgnoreCase)
		.OrderBy(group => group.Key, StringComparer.OrdinalIgnoreCase);

	IEnumerable<string> UniqueRepos => _sessionFeature.Sessions
		.Select(session => session.Context.Repository ?? string.Empty)
		.Where(repository => !string.IsNullOrEmpty(repository))
		.Distinct(StringComparer.OrdinalIgnoreCase)
		.OrderBy(repository => repository, StringComparer.OrdinalIgnoreCase);

	async Task ToggleFilterPanel()
	{
		_showFilterPanel = !_showFilterPanel;
		if(_showFilterPanel)
		{
			await SetGroupByPanelOpen(false);
		}
	}

	public Task ToggleGroupByPanelFromHeader() => SetGroupByPanelOpen(!_showGroupByPanel);

	public bool IsGroupByPanelOpen => _showGroupByPanel;

	async Task SetGroupByPanelOpen(bool isOpen)
	{
		_showGroupByPanel = isOpen;
		if(_showGroupByPanel)
		{
			_showFilterPanel = false;
		}

		await OnGroupByPanelOpenChanged.InvokeAsync(_showGroupByPanel);
	}

	async Task SetGroupByMode(SessionListViewMode mode)
	{
		if(_groupByMode != mode)
		{
			_groupByMode = mode;
			_appSettingsFeature.SessionListGroupBy = mode.ToString();
			InvalidateProjection();
		}

		await SetGroupByPanelOpen(false);
	}

	public Task FocusSearchAsync()
	{
		_focusSearchRequested = true;
		return InvokeAsync(StateHasChanged);
	}

	Task ClearAndFocusSearch()
	{
		SearchText = string.Empty;
		_focusSearchRequested = true;
		return InvokeAsync(StateHasChanged);
	}

	void ToggleCwdFilter(string cwd)
	{
		if(!_filterCwds.Remove(cwd))
		{
			_filterCwds.Add(cwd);
		}

		InvalidateProjection();
	}

	void ToggleCwdGroup(IGrouping<string, string> group)
	{
		List<string> items = [.. group];
		bool allSelected = items.All(cwd => _filterCwds.Contains(cwd));
		foreach(string cwd in items)
		{
			if(allSelected)
			{
				_filterCwds.Remove(cwd);
			}
			else
			{
				_filterCwds.Add(cwd);
			}
		}

		InvalidateProjection();
	}

	void ToggleCwdGroupExpand(string groupName)
	{
		if(!_expandedCwdGroups.Remove(groupName))
		{
			_expandedCwdGroups.Add(groupName);
		}
	}

	void ToggleRepoFilter(string repo)
	{
		if(!_filterRepos.Remove(repo))
		{
			_filterRepos.Add(repo);
		}

		InvalidateProjection();
	}

	void ToggleProjectGroupExpand(string groupId)
	{
		if(!_expandedProjectGroups.Remove(groupId))
		{
			_expandedProjectGroups.Add(groupId);
		}

		InvalidateProjection();
	}

	void ToggleProjectSessionLimitExpand(string groupId)
	{
		if(!_expandedProjectSessionGroups.Remove(groupId))
		{
			_expandedProjectSessionGroups.Add(groupId);
		}

		InvalidateProjection();
	}

	Task CreateSessionFromGroup(SessionListProjectHeaderRow group) =>
		OnCreateSessionFromPath.InvokeAsync(group.CreateSessionPath);

	protected override void OnInitialized()
	{
		_groupByMode = ParseGroupByMode(_appSettingsFeature.SessionListGroupBy);
		_sessionFeature.OnSessionStateChanged += OnSessionStateChanged;
		_timestampFeature.OnTick += OnTimestampTick;
	}

	static SessionListViewMode ParseGroupByMode(string? storedValue) =>
		Enum.TryParse(storedValue, true, out SessionListViewMode parsedMode) && Enum.IsDefined(parsedMode)
			? parsedMode
			: SessionListViewMode.Project;

	void OnSessionStateChanged(SessionStateChange change)
	{
		const SessionChangeKind projectionChanges =
			SessionChangeKind.SessionSummary |
			SessionChangeKind.SessionCollection |
			SessionChangeKind.CurrentSession;
		if((change.Kind & projectionChanges) == 0)
		{
			return;
		}

		InvalidateProjection();
		_ = InvokeAsync(StateHasChanged);
	}

	void OnTimestampTick()
	{
		long currentMinute = DateTime.UtcNow.Ticks / TimeSpan.TicksPerMinute;
		if(currentMinute == _lastTimestampMinute)
		{
			return;
		}

		_lastTimestampMinute = currentMinute;
		_ = InvokeAsync(StateHasChanged);
	}

	void InvalidateProjection() => _projectionDirty = true;

	async Task SelectSession(SessionModel session) => await _sessionFeature.LoadSession(session.Id);

	string GetTimeAgo(DateTime dateTime) => _timestampFeature.FormatRelative(dateTime);

	void ShowDeleteDialog(SessionModel session, MouseEventArgs _) => DeletePopup?.Open(session.Id);

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
			_timestampFeature.OnTick -= OnTimestampTick;
		}
	}
}
