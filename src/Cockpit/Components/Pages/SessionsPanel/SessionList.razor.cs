using Cockpit.Features.Sessions;
using Cockpit.Features.Sessions.Models;
using Cockpit.Features.Timestamp;
using Cockpit.Features.UIState;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.JSInterop;

namespace Cockpit.Components.Pages.SessionsPanel;

public partial class SessionList : ComponentBase, IDisposable
{
	[Parameter] public DeleteSessionPopup? DeletePopup { get; set; }
	[Parameter] public bool ShowSearch { get; set; }

	readonly ITimestampFeature _timestampFeature;
	readonly IUIStateFeature _uiStateFeature;
	readonly SessionFeature _sessionFeature;
	readonly IJSRuntime _jsRuntime;

	public SessionList(
		ITimestampFeature timestampFeature,
		IUIStateFeature uiStateFeature,
		SessionFeature sessionFeature,
		IJSRuntime jsRuntime)
	{
		_timestampFeature = timestampFeature;
		_uiStateFeature = uiStateFeature;
		_sessionFeature = sessionFeature;
		_jsRuntime = jsRuntime;
	}

	string _searchText = string.Empty;
	bool _showFilterPanel = false;
	readonly HashSet<string> _filterCwds = new(StringComparer.OrdinalIgnoreCase);
	readonly HashSet<string> _filterRepos = new(StringComparer.OrdinalIgnoreCase);
	readonly HashSet<string> _expandedCwdGroups = new(StringComparer.OrdinalIgnoreCase);

	protected override void OnParametersSet()
	{
		if(!ShowSearch)
		{
			_searchText = string.Empty;
			_filterCwds.Clear();
			_filterRepos.Clear();
			_showFilterPanel = false;
		}
	}

	public bool IsSearchActive => ShowSearch && (!string.IsNullOrWhiteSpace(_searchText) || _filterCwds.Count > 0 || _filterRepos.Count > 0);
	bool HasActiveFilters => _filterCwds.Count > 0 || _filterRepos.Count > 0;

	IEnumerable<SessionModel> AllSessionsSorted => _sessionFeature.Sessions.OrderByDescending(x => x.LastActivity);
	IEnumerable<SessionModel> RecentSessions => AllSessionsSorted.Where(s => (DateTime.UtcNow - s.LastActivity).TotalDays <= 7);

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

	void ToggleFilterPanel() => _showFilterPanel = !_showFilterPanel;

	public async Task FocusSearchAsync()
	{
		// Ensure the search input is rendered (it only exists when ShowSearch=true) before trying to focus it.
		await InvokeAsync(StateHasChanged);
		await _jsRuntime.InvokeVoidAsync("cockpit.focusElement", "sessionSearch");
	}

	async Task ClearAndFocusSearch()
	{
		_searchText = string.Empty;
		await _jsRuntime.InvokeVoidAsync("cockpit.focusElement", "sessionSearch");
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

	protected override void OnInitialized()
	{
		_sessionFeature.OnStateChanged += OnStateChanged;
		_uiStateFeature.OnStateChanged += OnStateChanged;
		_timestampFeature.OnTick += OnStateChanged;
	}

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
}
