using Cockpit.Features.Sessions.Models;

namespace Cockpit.Components.Pages.SessionsPanel;

internal enum SessionListViewMode
{
	Project,
	Updated
}

internal abstract record SessionListRow
{
	public abstract string Key { get; }
}

internal sealed record SessionListSessionRow(SessionModel Session, bool IsIndented) : SessionListRow
{
	public override string Key => $"session:{Session.Id}";
}

internal sealed record SessionListProjectHeaderRow(
	string GroupId,
	string Name,
	bool IsQuickChat,
	bool IsExpanded,
	string? CreateSessionPath) : SessionListRow
{
	public override string Key => $"project:{GroupId}";
}

internal sealed record SessionListShowMoreRow(string GroupId, bool IsExpanded) : SessionListRow
{
	public override string Key => $"show-more:{GroupId}";
}

internal sealed record SessionListProjectionOptions(
	SessionListViewMode ViewMode,
	string? ActiveSessionId,
	string SearchText,
	IReadOnlySet<string> FilterCwds,
	IReadOnlySet<string> FilterRepos,
	IReadOnlySet<string> ExpandedProjectGroups,
	IReadOnlySet<string> ExpandedProjectSessionGroups);

internal static class SessionListProjection
{
	const int CollapsedSessionLimit = 5;
	const int ExpandedSessionLimit = 15;

	public static IReadOnlyList<SessionListRow> Build(
		IEnumerable<SessionModel> sessions,
		SessionListProjectionOptions options)
	{
		List<SessionModel> sortedSessions = [.. sessions.OrderByDescending(session => session.LastActivity)];
		bool isSearchActive = !string.IsNullOrWhiteSpace(options.SearchText) ||
			options.FilterCwds.Count > 0 ||
			options.FilterRepos.Count > 0;

		if(isSearchActive)
		{
			return [.. Filter(sortedSessions, options)
				.Select(session => new SessionListSessionRow(session, false))];
		}

		if(options.ViewMode == SessionListViewMode.Updated)
		{
			return [.. sortedSessions.Select(session => new SessionListSessionRow(session, false))];
		}

		return BuildProjectRows(sortedSessions, options);
	}

	static IEnumerable<SessionModel> Filter(
		IEnumerable<SessionModel> sessions,
		SessionListProjectionOptions options)
	{
		if(!string.IsNullOrWhiteSpace(options.SearchText))
		{
			sessions = sessions.Where(session =>
				session.Title.Contains(options.SearchText, StringComparison.OrdinalIgnoreCase));
		}

		if(options.FilterCwds.Count > 0)
		{
			sessions = sessions.Where(session => options.FilterCwds.Contains(
				NormalizePath(session.Context.CurrentWorkingDirectory ?? string.Empty)));
		}

		if(options.FilterRepos.Count > 0)
		{
			sessions = sessions.Where(session =>
				options.FilterRepos.Contains(session.Context.Repository ?? string.Empty));
		}

		return sessions;
	}

	static IReadOnlyList<SessionListRow> BuildProjectRows(
		IReadOnlyList<SessionModel> sortedSessions,
		SessionListProjectionOptions options)
	{
		List<ProjectSessionGroup> groups = BuildProjectGroups(sortedSessions);
		List<SessionListRow> rows = [];

		foreach(ProjectSessionGroup group in groups)
		{
			bool containsActiveSession = options.ActiveSessionId is not null &&
				group.Sessions.Any(session => session.Id == options.ActiveSessionId);
			bool isExpanded = options.ExpandedProjectGroups.Contains(group.Id) || containsActiveSession;
			rows.Add(new SessionListProjectHeaderRow(
				group.Id,
				group.Name,
				group.IsQuickChat,
				isExpanded,
				GetCreateSessionPath(group)));

			if(!isExpanded)
			{
				continue;
			}

			int limit = options.ExpandedProjectSessionGroups.Contains(group.Id)
				? ExpandedSessionLimit
				: CollapsedSessionLimit;
			List<SessionModel> visibleSessions = [.. group.Sessions.Take(limit)];
			SessionModel? activeSession = options.ActiveSessionId is null
				? null
				: group.Sessions.FirstOrDefault(session => session.Id == options.ActiveSessionId);
			if(activeSession is not null && visibleSessions.All(session => session.Id != activeSession.Id))
			{
				visibleSessions.Add(activeSession);
			}

			foreach(SessionModel session in visibleSessions.OrderByDescending(session => session.LastActivity))
			{
				rows.Add(new SessionListSessionRow(session, true));
			}

			if(group.Sessions.Count > CollapsedSessionLimit)
			{
				rows.Add(new SessionListShowMoreRow(
					group.Id,
					options.ExpandedProjectSessionGroups.Contains(group.Id)));
			}
		}

		return rows;
	}

	static List<ProjectSessionGroup> BuildProjectGroups(IReadOnlyList<SessionModel> sortedSessions)
	{
		List<ProjectSessionGroup> groups = [];
		List<SessionModel> quickChatSessions = [.. sortedSessions.Where(IsQuickChatSession)];
		if(quickChatSessions.Count > 0)
		{
			groups.Add(new ProjectSessionGroup(
				"quick-chat",
				"Quick chat",
				true,
				quickChatSessions,
				quickChatSessions[0].LastActivity));
		}

		IEnumerable<ProjectSessionGroup> projectGroups = sortedSessions
			.Where(session => !IsQuickChatSession(session))
			.GroupBy(GetProjectGroupKey, StringComparer.OrdinalIgnoreCase)
			.Select(group =>
			{
				List<SessionModel> groupSessions = [.. group.OrderByDescending(session => session.LastActivity)];
				SessionModel latestSession = groupSessions[0];
				return new ProjectSessionGroup(
					group.Key,
					GetProjectGroupDisplayName(latestSession),
					false,
					groupSessions,
					latestSession.LastActivity);
			})
			.OrderByDescending(group => group.LastActivity);

		groups.AddRange(projectGroups);
		return groups;
	}

	internal static string NormalizePath(string path) =>
		string.IsNullOrEmpty(path)
			? string.Empty
			: path.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);

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
		return string.IsNullOrWhiteSpace(gitRoot)
			? NormalizePath(session.Context.CurrentWorkingDirectory ?? string.Empty)
			: gitRoot;
	}

	static string? GetCreateSessionPath(ProjectSessionGroup group)
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

	sealed record ProjectSessionGroup(
		string Id,
		string Name,
		bool IsQuickChat,
		IReadOnlyList<SessionModel> Sessions,
		DateTime LastActivity);
}
