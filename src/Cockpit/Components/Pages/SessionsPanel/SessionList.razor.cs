using Cockpit.Features.Sessions;
using Cockpit.Features.Sessions.Models;
using Cockpit.Features.Timestamp;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Web;

namespace Cockpit.Components.Pages.SessionsPanel;

public partial class SessionList : ComponentBase, IDisposable
{
	[Parameter] public DeleteSessionPopup? DeletePopup { get; set; }
	[Parameter] public bool ShowSearch { get; set; }
	[Parameter] public EventCallback<string?> OnCreateSessionFromPath { get; set; }

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
	bool _projectExpansionInitialized;
	ICollection<SessionListRow> _rows = [];
	SessionListProjectionSource? _projectionSource;
	bool _projectionSourceDirty = true;
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
				if(_projectionSourceDirty || _projectionSource is null)
				{
					_projectionSource = SessionListProjection.CreateSource(_sessionFeature.Sessions);
					_projectionSourceDirty = false;
					InitializeDefaultProjectExpansion(_projectionSource);
					PruneStaleProjectState(_projectionSource);
				}

				_rows = [.. SessionListProjection.Build(
					_projectionSource,
					new SessionListProjectionOptions(
						_searchText,
						_filterCwds,
						_filterRepos,
						_expandedSections,
						_expandedProjectGroups,
						_sessionLimits))];
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

	void InitializeDefaultProjectExpansion(SessionListProjectionSource source)
	{
		if(_projectExpansionInitialized)
		{
			return;
		}

		_projectExpansionInitialized = true;
		string? mostRecentProjectGroupId = source.MostRecentProjectGroupId;
		if(mostRecentProjectGroupId is not null)
		{
			_expandedProjectGroups.Add(mostRecentProjectGroupId);
		}
	}

	void PruneStaleProjectState(SessionListProjectionSource source)
	{
		IReadOnlySet<string> projectGroupIds = source.ProjectGroupIds;
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
		int currentLimit = _sessionLimits.GetValueOrDefault(groupId, SessionListProjection.initialSessionLimit);
		_sessionLimits[groupId] = currentLimit + SessionListProjection.sessionPageSize;
		InvalidateProjection();
	}

	void ShowLessSessions(string groupId)
	{
		_sessionLimits.Remove(groupId);
		InvalidateProjection();
	}

	Task CreateSessionFromGroup(SessionListProjectHeaderRow group) => OnCreateSessionFromPath.InvokeAsync(group.CreateSessionPath);

	protected override void OnInitialized()
	{
		_sessionFeature.OnSessionStateChanged += OnSessionStateChanged;
		_timestampFeature.OnTick += OnTimestampTick;
	}

	void OnSessionStateChanged(SessionStateChange change)
	{
		const SessionChangeKind sourceChanges = SessionChangeKind.SessionSummary | SessionChangeKind.SessionCollection;
		const SessionChangeKind projectionChanges = sourceChanges | SessionChangeKind.CurrentSession;
		const SessionChangeKind itemChanges = SessionChangeKind.ConversationContent |
			SessionChangeKind.ConversationStructure |
			SessionChangeKind.ConversationReset |
			SessionChangeKind.WorkingState;
		if((change.Kind & (projectionChanges | itemChanges)) == 0)
		{
			return;
		}

		if((change.Kind & sourceChanges) != 0)
		{
			InvalidateProjectionSource();
		}
		else if((change.Kind & SessionChangeKind.CurrentSession) != 0)
		{
			InvalidateProjection();
		}
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

	void InvalidateProjectionSource()
	{
		_projectionSourceDirty = true;
		_projectionDirty = true;
	}

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
