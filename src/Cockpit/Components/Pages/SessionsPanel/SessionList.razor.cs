using Cockpit.Features.Sessions;
using Cockpit.Features.Sessions.Models;
using Cockpit.Features.Timestamp;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.JSInterop;

namespace Cockpit.Components.Pages.SessionsPanel;

public partial class SessionList : ComponentBase, IDisposable
{
	[Parameter] public DeleteSessionPopup? DeletePopup { get; set; }
	[Parameter] public bool ShowSearch { get; set; }
	[Parameter] public EventCallback<string?> OnCreateSessionFromPath { get; set; }
	[Inject] public required IJSRuntime JSRuntime { get; set; }

	readonly ITimestampFeature _timestampFeature;
	readonly SessionFeature _sessionFeature;

	public SessionList(
		ITimestampFeature timestampFeature,
		SessionFeature sessionFeature)
	{
		_timestampFeature = timestampFeature;
		_sessionFeature = sessionFeature;
	}

	string _searchText = string.Empty;
	ElementReference _sessionSearch;
	bool _focusSearchRequested;
	bool _showFilterPanel;
	readonly HashSet<string> _filterCwds = new(StringComparer.OrdinalIgnoreCase);
	readonly HashSet<string> _filterRepos = new(StringComparer.OrdinalIgnoreCase);
	readonly HashSet<string> _expandedCwdGroups = new(StringComparer.OrdinalIgnoreCase);
	readonly HashSet<SessionListSection> _expandedSections = [SessionListSection.Projects];
	readonly HashSet<string> _expandedProjectGroups = new(SessionProjectIdentityResolver.ProjectIdComparer);
	readonly Dictionary<string, int> _sessionLimits = new(SessionProjectIdentityResolver.ProjectIdComparer);
	int _recentSessionLimit = SessionListProjection.InitialSessionLimit;
	bool _projectExpansionInitialized;
	bool _loadingMoreRecents;
	ElementReference _recentsLoadMoreSentinel;
	DotNetObjectReference<SessionList>? _dotNetReference;
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
				InitializeDefaultProjectExpansion();
				PruneStaleProjectState();
				_rows = [.. SessionListProjection.Build(
					_sessionFeature.Sessions,
					new SessionListProjectionOptions(
						_sessionFeature.CurrentSession?.Id,
						_searchText,
						_filterCwds,
						_filterRepos,
						_expandedSections,
						_expandedProjectGroups,
						_sessionLimits,
						_recentSessionLimit))];
				_projectionDirty = false;
			}

			return _rows;
		}
	}

	protected override void OnParametersSet()
	{
		if(!ShowSearch && (!string.IsNullOrEmpty(_searchText) || _filterCwds.Count > 0 || _filterRepos.Count > 0))
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

		_dotNetReference ??= DotNetObjectReference.Create(this);
		if(HasMoreRecents)
		{
			await JSRuntime.InvokeVoidAsync("cockpit.observeSessionListLoadMore", _recentsLoadMoreSentinel, _dotNetReference);
		}
		else
		{
			await JSRuntime.InvokeVoidAsync("cockpit.cleanupSessionListLoadMore");
		}
	}

	bool IsSearchActive => ShowSearch && (!string.IsNullOrWhiteSpace(_searchText) || _filterCwds.Count > 0 || _filterRepos.Count > 0);
	bool HasActiveFilters => _filterCwds.Count > 0 || _filterRepos.Count > 0;

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

	void ToggleFilterPanel()
	{
		_showFilterPanel = !_showFilterPanel;
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

	void InitializeDefaultProjectExpansion()
	{
		if(_projectExpansionInitialized)
		{
			return;
		}

		_projectExpansionInitialized = true;
		string? mostRecentProjectGroupId = SessionListProjection.GetMostRecentProjectGroupId(_sessionFeature.Sessions);
		if(mostRecentProjectGroupId is not null)
		{
			_expandedProjectGroups.Add(mostRecentProjectGroupId);
		}
	}

	void PruneStaleProjectState()
	{
		IReadOnlySet<string> projectGroupIds = SessionListProjection.GetProjectGroupIds(_sessionFeature.Sessions);
		_expandedProjectGroups.RemoveWhere(groupId => !projectGroupIds.Contains(groupId));

		foreach(string groupId in _sessionLimits.Keys
			.Where(groupId => !string.Equals(groupId, "chats", StringComparison.OrdinalIgnoreCase) && !projectGroupIds.Contains(groupId))
			.ToList())
		{
			_sessionLimits.Remove(groupId);
		}
	}

	void ToggleSectionExpand(SessionListSection section)
	{
		if(!_expandedSections.Remove(section))
		{
			_expandedSections.Add(section);
		}

		InvalidateProjection();
	}

	void ShowMoreSessions(string groupId)
	{
		int currentLimit = _sessionLimits.GetValueOrDefault(groupId, SessionListProjection.InitialSessionLimit);
		_sessionLimits[groupId] = currentLimit + SessionListProjection.SessionPageSize;
		InvalidateProjection();
	}

	void ShowLessSessions(string groupId)
	{
		_sessionLimits.Remove(groupId);
		InvalidateProjection();
	}

	bool HasMoreRecents => !IsSearchActive &&
		_expandedSections.Contains(SessionListSection.Recents) &&
		_recentSessionLimit < _sessionFeature.Sessions.Count;

	[JSInvokable]
	public Task LoadMoreRecents()
	{
		if(_loadingMoreRecents || !HasMoreRecents)
		{
			return Task.CompletedTask;
		}

		_loadingMoreRecents = true;
		_recentSessionLimit = Math.Min(
			_recentSessionLimit + SessionListProjection.SessionPageSize,
			_sessionFeature.Sessions.Count);
		InvalidateProjection();
		_loadingMoreRecents = false;
		return InvokeAsync(StateHasChanged);
	}

	Task CreateSessionFromGroup(SessionListProjectHeaderRow group) => OnCreateSessionFromPath.InvokeAsync(group.CreateSessionPath);

	protected override void OnInitialized()
	{
		_sessionFeature.OnSessionStateChanged += OnSessionStateChanged;
		_timestampFeature.OnTick += OnTimestampTick;
	}

	void OnSessionStateChanged(SessionStateChange change)
	{
		const SessionChangeKind projectionChanges = SessionChangeKind.SessionSummary | SessionChangeKind.SessionCollection | SessionChangeKind.CurrentSession;
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
			_dotNetReference?.Dispose();
			_ = JSRuntime.InvokeVoidAsync("cockpit.cleanupSessionListLoadMore");
		}
	}
}
