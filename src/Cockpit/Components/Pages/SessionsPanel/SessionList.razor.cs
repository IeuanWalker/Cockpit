using Cockpit.Features.Sessions;
using Cockpit.Features.Sessions.Models;
using Cockpit.Features.Timestamp;
using Cockpit.Features.UIState;
using Cockpit.Features.AppSettings;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Web;

namespace Cockpit.Components.Pages.SessionsPanel;

public partial class SessionList : ComponentBase, IDisposable
{
	[Parameter] public DeleteSessionPopup? DeletePopup { get; set; }
	[Parameter] public bool ShowSearch { get; set; }
	[Parameter] public EventCallback<string?> OnCreateSessionFromPath { get; set; }

	readonly ITimestampFeature _timestampFeature;
	readonly IUIStateFeature _uiStateFeature;
	readonly SessionFeature _sessionFeature;
	readonly IAppSettingsFeature _appSettingsFeature;

	public SessionList(
		ITimestampFeature timestampFeature,
		IUIStateFeature uiStateFeature,
		SessionFeature sessionFeature,
		IAppSettingsFeature appSettingsFeature)
	{
		_timestampFeature = timestampFeature;
		_uiStateFeature = uiStateFeature;
		_sessionFeature = sessionFeature;
		_appSettingsFeature = appSettingsFeature;
	}

	string _searchText = string.Empty;
	ElementReference _sessionSearch;
	bool _focusSearchRequested;
	bool _showFilterPanel = false;
	bool _showGroupByPanel = false;
	GroupByModeEnum _groupByMode = GroupByModeEnum.Project;
	readonly HashSet<string> _filterCwds = new(StringComparer.OrdinalIgnoreCase);
	readonly HashSet<string> _filterRepos = new(StringComparer.OrdinalIgnoreCase);
	readonly HashSet<string> _expandedCwdGroups = new(StringComparer.OrdinalIgnoreCase);
	readonly HashSet<string> _expandedProjectGroups = new(StringComparer.OrdinalIgnoreCase);
	readonly HashSet<string> _expandedProjectSessionGroups = new(StringComparer.OrdinalIgnoreCase);

	protected override void OnParametersSet()
	{
		if(!ShowSearch)
		{
			_searchText = string.Empty;
			_focusSearchRequested = false;
			_filterCwds.Clear();
			_filterRepos.Clear();
			_showFilterPanel = false;
			_showGroupByPanel = false;
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

	public bool IsSearchActive => ShowSearch && (!string.IsNullOrWhiteSpace(_searchText) || _filterCwds.Count > 0 || _filterRepos.Count > 0);
	bool HasActiveFilters => _filterCwds.Count > 0 || _filterRepos.Count > 0;
	bool IsGroupingByProject => _groupByMode == GroupByModeEnum.Project;

	IEnumerable<SessionModel> AllSessionsSorted => _sessionFeature.Sessions.OrderByDescending(x => x.LastActivity);
	IEnumerable<SessionModel> RecentSessions => AllSessionsSorted;
	IReadOnlyList<ProjectSessionGroupModel> ProjectSessionGroups => BuildProjectSessionGroups();

	IEnumerable<SessionModel> FilteredSessions
	{
		get
		{
			IEnumerable<SessionModel> sessions = AllSessionsSorted;
			if(!string.IsNullOrWhiteSpace(_searchText))
			{
				sessions = sessions.Where(s => s.Title.Contains(_searchText, StringComparison.OrdinalIgnoreCase));
			}

			if(_filterCwds.Count > 0)
			{
				sessions = sessions.Where(s => _filterCwds.Contains(NormalizePath(s.Context.CurrentWorkingDirectory ?? string.Empty)));
			}

			if(_filterRepos.Count > 0)
			{
				sessions = sessions.Where(s => _filterRepos.Contains(s.Context.Repository ?? string.Empty));
			}

			return sessions;
		}
	}

	IEnumerable<string> UniqueCwds => _sessionFeature.Sessions
		.Select(s => NormalizePath(s.Context.CurrentWorkingDirectory ?? string.Empty))
		.Where(s => !string.IsNullOrEmpty(s))
		.Distinct(StringComparer.OrdinalIgnoreCase)
		.OrderBy(s => s, StringComparer.OrdinalIgnoreCase);

	IEnumerable<IGrouping<string, string>> GroupedCwds => UniqueCwds
		.GroupBy(
			cwd =>
			{
				string name = Path.GetFileName(cwd);
				return string.IsNullOrEmpty(name) ? cwd : name;
			},
			StringComparer.OrdinalIgnoreCase)
		.OrderBy(g => g.Key, StringComparer.OrdinalIgnoreCase);

	IEnumerable<string> UniqueRepos => _sessionFeature.Sessions
		.Select(s => s.Context.Repository ?? string.Empty)
		.Where(s => !string.IsNullOrEmpty(s))
		.Distinct(StringComparer.OrdinalIgnoreCase)
		.OrderBy(s => s, StringComparer.OrdinalIgnoreCase);

	static string NormalizePath(string path) =>
		string.IsNullOrEmpty(path)
			? string.Empty
			: path.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);

	void ToggleFilterPanel()
	{
		_showFilterPanel = !_showFilterPanel;
		if(_showFilterPanel)
		{
			_showGroupByPanel = false;
		}
	}

	void ToggleGroupByPanel()
	{
		_showGroupByPanel = !_showGroupByPanel;
		if(_showGroupByPanel)
		{
			_showFilterPanel = false;
		}
	}

	void SetGroupByMode(GroupByModeEnum mode)
	{
		_groupByMode = mode;
		_appSettingsFeature.SessionListGroupBy = mode.ToString();
		_showGroupByPanel = false;
	}

	public Task FocusSearchAsync()
	{
		_focusSearchRequested = true;
		return InvokeAsync(StateHasChanged);
	}

	Task ClearAndFocusSearch()
	{
		_searchText = string.Empty;
		_focusSearchRequested = true;
		return InvokeAsync(StateHasChanged);
	}

	void ToggleCwdFilter(string cwd)
	{
		if(!_filterCwds.Remove(cwd))
		{
			_filterCwds.Add(cwd);
		}
	}

	void ToggleCwdGroup(IGrouping<string, string> group)
	{
		List<string> items = [.. group];
		bool allSelected = items.All(cwd => _filterCwds.Contains(cwd));
		if(allSelected)
		{
			foreach(string cwd in items)
			{
				_filterCwds.Remove(cwd);
			}
		}
		else
		{
			foreach(string cwd in items)
			{
				_filterCwds.Add(cwd);
			}
		}
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
	}

	void ToggleProjectGroupExpand(string groupId)
	{
		if(!_expandedProjectGroups.Remove(groupId))
		{
			_expandedProjectGroups.Add(groupId);
		}
	}

	bool IsProjectGroupExpanded(ProjectSessionGroupModel group) =>
		_expandedProjectGroups.Contains(group.Id) || GroupContainsActiveSession(group);

	bool ShowProjectGroupToggle(ProjectSessionGroupModel group) => group.Sessions.Count > 5;

	bool GroupContainsActiveSession(ProjectSessionGroupModel group)
	{
		SessionModel? activeSession = _sessionFeature.CurrentSession;
		return activeSession is not null && group.Sessions.Any(session => session.Id == activeSession.Id);
	}

	IEnumerable<SessionModel> VisibleSessionsForGroup(ProjectSessionGroupModel group)
	{
		int limit = IsProjectSessionLimitExpanded(group.Id) ? 15 : 5;
		List<SessionModel> visibleSessions = [.. group.Sessions.Take(limit)];
		SessionModel? activeSession = _sessionFeature.CurrentSession;
		if(activeSession is not null &&
		   group.Sessions.Any(session => session.Id == activeSession.Id) &&
		   !visibleSessions.Any(session => session.Id == activeSession.Id))
		{
			visibleSessions.Add(activeSession);
		}

		return visibleSessions.OrderByDescending(session => session.LastActivity);
	}

	void ToggleProjectSessionLimitExpand(string groupId)
	{
		if(!_expandedProjectSessionGroups.Remove(groupId))
		{
			_expandedProjectSessionGroups.Add(groupId);
		}
	}

	bool IsProjectSessionLimitExpanded(string groupId) => _expandedProjectSessionGroups.Contains(groupId);

	Task CreateSessionFromGroup(ProjectSessionGroupModel group)
	{
		string? path = GetCreateSessionPath(group);
		return OnCreateSessionFromPath.InvokeAsync(path);
	}

	List<ProjectSessionGroupModel> BuildProjectSessionGroups()
	{
		List<SessionModel> allSessions = [.. AllSessionsSorted];
		List<ProjectSessionGroupModel> groups = [];

		List<SessionModel> quickChatSessions = [.. allSessions.Where(IsQuickChatSession)];
		if(quickChatSessions.Count > 0)
		{
			groups.Add(new ProjectSessionGroupModel(
				"quick-chat",
				"Quick chat",
				true,
				quickChatSessions,
				quickChatSessions[0].LastActivity));
		}

		IEnumerable<IGrouping<string, SessionModel>> projectGroups = allSessions
			.Where(session => !IsQuickChatSession(session))
			.GroupBy(GetProjectGroupKey, StringComparer.OrdinalIgnoreCase);

		List<ProjectSessionGroupModel> orderedProjectGroups = [.. projectGroups
			.Select(group =>
			{
				List<SessionModel> sessions = [.. group.OrderByDescending(session => session.LastActivity)];
				SessionModel latestSession = sessions[0];
				return new ProjectSessionGroupModel(
					group.Key,
					GetProjectGroupDisplayName(latestSession),
					false,
					sessions,
					latestSession.LastActivity);
			})
			.OrderByDescending(group => group.LastActivity)];

		groups.AddRange(orderedProjectGroups);
		return groups;
	}

	static bool IsQuickChatSession(SessionModel session) =>
		string.IsNullOrWhiteSpace(session.Context.CurrentWorkingDirectory);

	static string GetProjectGroupKey(SessionModel session)
	{
		string repositoryName = GetRepositoryLeafName(session.Context.Repository);
		string preferredPath = GetPreferredProjectPath(session);
		string preferredFolder = GetFolderNameFromPath(preferredPath);
		if(!string.IsNullOrWhiteSpace(repositoryName))
		{
			return $"name:{repositoryName}";
		}

		if(!string.IsNullOrWhiteSpace(preferredFolder))
		{
			return $"name:{preferredFolder}";
		}

		return $"path:{preferredPath}";
	}

	static string GetProjectGroupDisplayName(SessionModel session)
	{
		string repositoryName = GetRepositoryLeafName(session.Context.Repository);
		if(!string.IsNullOrWhiteSpace(repositoryName))
		{
			return repositoryName;
		}

		string preferredPath = GetPreferredProjectPath(session);
		string folderName = GetFolderNameFromPath(preferredPath);
		return string.IsNullOrWhiteSpace(folderName) ? preferredPath : folderName;
	}

	static string GetPreferredProjectPath(SessionModel session)
	{
		string gitRoot = NormalizePath(session.Context.GitRoot ?? string.Empty);
		if(!string.IsNullOrWhiteSpace(gitRoot))
		{
			return gitRoot;
		}

		return NormalizePath(session.Context.CurrentWorkingDirectory ?? string.Empty);
	}

	static string? GetCreateSessionPath(ProjectSessionGroupModel group)
	{
		SessionModel? mostRecentSession = group.Sessions.FirstOrDefault();
		if(mostRecentSession is null)
		{
			return null;
		}

		string path = GetPreferredProjectPath(mostRecentSession);
		return string.IsNullOrWhiteSpace(path) ? null : path;
	}

	static string GetFolderNameFromPath(string normalizedPath)
	{
		if(string.IsNullOrWhiteSpace(normalizedPath))
		{
			return string.Empty;
		}

		string folderName = Path.GetFileName(normalizedPath);
		return string.IsNullOrWhiteSpace(folderName) ? normalizedPath : folderName;
	}

	static string GetRepositoryLeafName(string? repository)
	{
		if(string.IsNullOrWhiteSpace(repository))
		{
			return string.Empty;
		}

		string normalized = repository.Trim().Replace('\\', '/');
		string[] segments = normalized.Split('/', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
		return segments.Length == 0 ? string.Empty : segments[^1];
	}

	protected override void OnInitialized()
	{
		_groupByMode = ParseGroupByMode(_appSettingsFeature.SessionListGroupBy);
		_sessionFeature.OnStateChanged += OnStateChanged;
		_uiStateFeature.OnStateChanged += OnStateChanged;
		_timestampFeature.OnTick += OnStateChanged;
	}

	static GroupByModeEnum ParseGroupByMode(string? storedValue) =>
		Enum.TryParse(storedValue, true, out GroupByModeEnum parsedMode) && Enum.IsDefined(parsedMode)
			? parsedMode
			: GroupByModeEnum.Project;

	void OnStateChanged()
	{
		InvokeAsync(StateHasChanged);
	}

	async Task SelectSession(SessionModel session)
	{
		await _sessionFeature.LoadSession(session.Id);
	}

	string GetTimeAgo(DateTime dateTime)
	{
		return _timestampFeature.FormatRelative(dateTime);
	}

	void ShowDeleteDialog(SessionModel session, MouseEventArgs _)
	{
		DeletePopup?.Open(session.Id);
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
		}
	}

	enum GroupByModeEnum
	{
		Project,
		Updated
	}

	sealed record ProjectSessionGroupModel(
		string Id,
		string Name,
		bool IsQuickChat,
		IReadOnlyList<SessionModel> Sessions,
		DateTime LastActivity);
}
