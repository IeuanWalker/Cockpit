using System.Diagnostics.CodeAnalysis;
using Cockpit.Features.Sessions.Models;

namespace Cockpit.Components.Pages.SessionsPanel;

enum SessionListSection
{
	Chats,
	Projects,
	Recents
}

abstract record SessionListRow
{
	public abstract string Key { get; }
}

sealed record SessionListSectionHeaderRow(SessionListSection Section, string Name, bool IsExpanded) : SessionListRow
{
	public override string Key => $"section:{Section}";
}

sealed record SessionListSessionRow(SessionModel Session, int IndentLevel, string ScopeId) : SessionListRow
{
	public override string Key => $"session:{ScopeId}:{Session.Id}";
}

sealed record SessionListProjectHeaderRow(
	string GroupId,
	string Name,
	bool IsExpanded,
	string? CreateSessionPath,
	int SessionCount,
	string? Repository) : SessionListRow
{
	public override string Key => $"project:{GroupId}";
}

sealed record SessionListShowMoreRow(
	string GroupId,
	int IndentLevel,
	int VisibleCount,
	int TotalCount) : SessionListRow
{
	public override string Key => $"show-more:{GroupId}";
	public bool HasMore => VisibleCount < TotalCount;
	public bool CanShowLess => VisibleCount > SessionListProjection.InitialSessionLimit;
}

sealed record SessionListProjectionOptions(
	string? ActiveSessionId,
	string SearchText,
	IReadOnlySet<string> FilterCwds,
	IReadOnlySet<string> FilterRepos,
	IReadOnlySet<SessionListSection> ExpandedSections,
	IReadOnlySet<string> ExpandedProjectGroups,
	IReadOnlyDictionary<string, int> SessionLimits,
	int RecentSessionLimit);

static class SessionListProjection
{
	internal const int InitialSessionLimit = 5;
	internal const int SessionPageSize = 10;

	public static IReadOnlyList<SessionListRow> Build(IEnumerable<SessionModel> sessions, SessionListProjectionOptions options)
	{
		List<SessionModel> sortedSessions = [.. sessions.OrderByDescending(session => session.LastActivity)];
		bool isSearchActive = !string.IsNullOrWhiteSpace(options.SearchText) || options.FilterCwds.Count > 0 || options.FilterRepos.Count > 0;

		if(isSearchActive)
		{
			return [.. Filter(sortedSessions, options).Select(session => new SessionListSessionRow(session, 0, "search"))];
		}

		return BuildSectionRows(sortedSessions, options);
	}

	static IEnumerable<SessionModel> Filter(IEnumerable<SessionModel> sessions, SessionListProjectionOptions options)
	{
		if(!string.IsNullOrWhiteSpace(options.SearchText))
		{
			sessions = sessions.Where(session => session.Title.Contains(options.SearchText, StringComparison.OrdinalIgnoreCase));
		}

		if(options.FilterCwds.Count > 0)
		{
			sessions = sessions.Where(session => options.FilterCwds.Contains(NormalizePath(session.Context.CurrentWorkingDirectory ?? string.Empty)));
		}

		if(options.FilterRepos.Count > 0)
		{
			sessions = sessions.Where(session => options.FilterRepos.Contains(session.Context.Repository ?? string.Empty));
		}

		return sessions;
	}

	static IReadOnlyList<SessionListRow> BuildSectionRows(IReadOnlyList<SessionModel> sortedSessions, SessionListProjectionOptions options)
	{
		List<SessionListRow> rows = [];
		List<SessionModel> chatSessions = [.. sortedSessions.Where(IsChatSession)];
		List<ProjectSessionGroup> projectGroups = BuildProjectGroups(sortedSessions);

		bool chatsExpanded = options.ExpandedSections.Contains(SessionListSection.Chats);
		rows.Add(new SessionListSectionHeaderRow(SessionListSection.Chats, "Chats", chatsExpanded));
		if(chatsExpanded)
		{
			AddLimitedSessions(rows, chatSessions, "chats", 1, options);
		}

		bool projectsExpanded = options.ExpandedSections.Contains(SessionListSection.Projects);
		rows.Add(new SessionListSectionHeaderRow(SessionListSection.Projects, "Projects", projectsExpanded));
		if(projectsExpanded)
		{
			AddProjectRows(rows, projectGroups, options);
		}

		bool recentsExpanded = options.ExpandedSections.Contains(SessionListSection.Recents);
		rows.Add(new SessionListSectionHeaderRow(SessionListSection.Recents, "Recents", recentsExpanded));
		if(recentsExpanded)
		{
			foreach(SessionModel session in sortedSessions.Take(options.RecentSessionLimit))
			{
				rows.Add(new SessionListSessionRow(session, 1, "recents"));
			}
		}

		return rows;
	}

	[SuppressMessage("Performance", "CA1859:Use concrete types when possible for improved performance", Justification = "The helper also accepts the complete sorted session list.")]
	static void AddLimitedSessions(
		List<SessionListRow> rows,
		IReadOnlyList<SessionModel> sessions,
		string groupId,
		int indentLevel,
		SessionListProjectionOptions options)
	{
		int limit = options.SessionLimits.GetValueOrDefault(groupId, InitialSessionLimit);
		List<SessionModel> visibleSessions = [.. sessions.Take(limit)];

		foreach(SessionModel session in visibleSessions.OrderByDescending(session => session.LastActivity))
		{
			rows.Add(new SessionListSessionRow(session, indentLevel, groupId));
		}

		if(sessions.Count > InitialSessionLimit)
		{
			rows.Add(new SessionListShowMoreRow(groupId, indentLevel, visibleSessions.Count, sessions.Count));
		}
	}

	static void AddProjectRows(List<SessionListRow> rows, IEnumerable<ProjectSessionGroup> groups, SessionListProjectionOptions options)
	{
		foreach(ProjectSessionGroup group in groups)
		{
			bool isExpanded = options.ExpandedProjectGroups.Contains(group.Id);
			rows.Add(new SessionListProjectHeaderRow(group.Id, group.Name, isExpanded, group.RootPath, group.Sessions.Count, group.Repository));

			if(isExpanded)
			{
				AddLimitedSessions(rows, group.Sessions, group.Id, 2, options);
			}
		}
	}

	static List<ProjectSessionGroup> BuildProjectGroups(IReadOnlyList<SessionModel> sortedSessions)
	{
		List<ProjectSessionEntry> projectSessions = [.. sortedSessions
			.Where(session => !IsChatSession(session))
			.Select(session => new ProjectSessionEntry(session, SessionProjectIdentityResolver.Resolve(session)))
			.Where(entry => entry.Identity is not null)];

		int[] parents = [.. Enumerable.Range(0, projectSessions.Count)];
		Dictionary<string, int> roots = new(SessionProjectIdentityResolver.ProjectIdComparer);
		Dictionary<string, int> repositories = new(SessionProjectIdentityResolver.ProjectIdComparer);
		for(int index = 0; index < projectSessions.Count; index++)
		{
			SessionProjectIdentity identity = projectSessions[index].Identity!;
			UnionWithExisting(index, identity.RootId, roots, parents);
			if(identity.RepositoryId is not null)
			{
				UnionWithExisting(index, identity.RepositoryId, repositories, parents);
			}
		}

		List<ProjectSessionGroup> groups = [.. projectSessions
			.Select((entry, index) => (Entry: entry, Group: Find(index, parents)))
			.GroupBy(value => value.Group)
			.Select(group =>
			{
				List<ProjectSessionEntry> entries = [.. group.Select(value => value.Entry).OrderByDescending(entry => entry.Session.LastActivity)];
				string? repositoryId = entries
					.Select(entry => entry.Identity!.RepositoryId)
					.Where(value => value is not null)
					.Order(StringComparer.Ordinal)
					.FirstOrDefault();
				string groupId = repositoryId ?? entries
					.Select(entry => entry.Identity!.RootId)
					.Order(StringComparer.Ordinal)
					.First();
				string baseName = repositoryId is null
					? entries[0].Identity!.BaseName
					: entries.First(entry => SessionProjectIdentityResolver.ProjectIdComparer.Equals(
						entry.Identity!.RepositoryId,
						repositoryId)).Identity!.BaseName;
				SessionProjectIdentity identity = entries
					.Select(entry => entry.Identity!)
					.OrderByDescending(value => SessionProjectIdentityResolver.PathComparer.Equals(
						SessionProjectIdentityResolver.GetBaseName(value.RootPath),
						baseName))
					.ThenBy(value => value.RootPath, SessionProjectIdentityResolver.PathComparer)
					.ThenBy(value => value.RootPath, StringComparer.Ordinal)
					.First();
				string? repository = entries
					.Select(entry => entry.Session.Context.Repository)
					.FirstOrDefault(value => !string.IsNullOrWhiteSpace(value)) ?? identity.Repository;

				return new ProjectSessionGroup(
					groupId,
					baseName,
					identity.RootPath,
					repository,
					[.. entries.Select(entry => entry.Session)],
					entries[0].Session.LastActivity);
			})];

		DisambiguateProjectNames(groups);
		return [.. groups.OrderByDescending(group => group.LastActivity)];
	}

	static void UnionWithExisting(int index, string key, Dictionary<string, int> owners, int[] parents)
	{
		if(owners.TryGetValue(key, out int existing))
		{
			Union(index, existing, parents);
		}
		else
		{
			owners[key] = index;
		}
	}

	static int Find(int index, int[] parents)
	{
		while(parents[index] != index)
		{
			parents[index] = parents[parents[index]];
			index = parents[index];
		}

		return index;
	}

	static void Union(int first, int second, int[] parents)
	{
		int firstRoot = Find(first, parents);
		int secondRoot = Find(second, parents);
		if(firstRoot != secondRoot)
		{
			parents[firstRoot] = secondRoot;
		}
	}

	static void DisambiguateProjectNames(List<ProjectSessionGroup> groups)
	{
		foreach(IGrouping<string, ProjectSessionGroup> duplicateNames in groups
			.GroupBy(group => group.BaseName, StringComparer.OrdinalIgnoreCase)
			.Where(group => group.Count() > 1))
		{
			List<ProjectSessionGroup> duplicateGroups = [.. duplicateNames];
			Dictionary<ProjectSessionGroup, IReadOnlyList<string>> parentLabels = duplicateGroups.ToDictionary(
				group => group,
				group => GetParentLabels(group.RootPath));

			HashSet<ProjectSessionGroup> unresolved = [.. duplicateGroups];
			HashSet<string> usedLabels = new(StringComparer.OrdinalIgnoreCase);
			int depth = 0;
			while(unresolved.Count > 0)
			{
				Dictionary<ProjectSessionGroup, string> candidates = unresolved.ToDictionary(
					group => group,
					group => depth < parentLabels[group].Count ? parentLabels[group][depth] : group.RootPath);

				List<ProjectSessionGroup> resolvedThisRound = [.. unresolved.Where(group =>
					!usedLabels.Contains(candidates[group]) &&
					candidates.Values.Count(candidate => string.Equals(candidate, candidates[group], StringComparison.OrdinalIgnoreCase)) == 1)];

				foreach(ProjectSessionGroup group in resolvedThisRound)
				{
					group.Name = $"{group.BaseName} — {candidates[group]}";
					usedLabels.Add(candidates[group]);
					unresolved.Remove(group);
				}

				if(resolvedThisRound.Count == 0 && unresolved.All(group => depth >= parentLabels[group].Count))
				{
					foreach(ProjectSessionGroup group in unresolved)
					{
						group.Name = $"{group.BaseName} — {group.RootPath}";
					}

					break;
				}

				depth++;
			}
		}
	}

	static IReadOnlyList<string> GetParentLabels(string rootPath)
	{
		List<string> labels = [];
		string? currentPath;
		try
		{
			currentPath = Path.GetDirectoryName(rootPath);
		}
		catch(ArgumentException)
		{
			return labels;
		}

		string label = string.Empty;
		while(!string.IsNullOrWhiteSpace(currentPath))
		{
			string trimmedPath = currentPath.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
			string segment = Path.GetFileName(trimmedPath);
			if(string.IsNullOrWhiteSpace(segment))
			{
				segment = currentPath;
			}

			label = string.IsNullOrEmpty(label) ? segment : Path.Combine(segment, label);
			labels.Add(label);

			string? nextPath = Path.GetDirectoryName(trimmedPath);
			if(string.Equals(nextPath, currentPath, StringComparison.Ordinal))
			{
				break;
			}

			currentPath = nextPath;
		}

		return labels;
	}

	internal static string? GetMostRecentProjectGroupId(IEnumerable<SessionModel> sessions)
		=> BuildProjectGroups([.. sessions.OrderByDescending(session => session.LastActivity)]).FirstOrDefault()?.Id;

	internal static IReadOnlySet<string> GetProjectGroupIds(IEnumerable<SessionModel> sessions)
		=> BuildProjectGroups([.. sessions.OrderByDescending(session => session.LastActivity)])
			.Select(group => group.Id)
			.ToHashSet(SessionProjectIdentityResolver.ProjectIdComparer);

	internal static string NormalizePath(string path) => string.IsNullOrWhiteSpace(path)
		? string.Empty
		: SessionProjectIdentityResolver.NormalizePath(path);

	static bool IsChatSession(SessionModel session) => string.IsNullOrWhiteSpace(session.Context.CurrentWorkingDirectory);

	sealed record ProjectSessionEntry(SessionModel Session, SessionProjectIdentity? Identity);

	sealed class ProjectSessionGroup(
		string id,
		string baseName,
		string rootPath,
		string? repository,
		IReadOnlyList<SessionModel> sessions,
		DateTime lastActivity)
	{
		public string Id { get; } = id;
		public string BaseName { get; } = baseName;
		public string RootPath { get; } = rootPath;
		public string? Repository { get; } = repository;
		public IReadOnlyList<SessionModel> Sessions { get; } = sessions;
		public DateTime LastActivity { get; } = lastActivity;
		public string Name { get; set; } = baseName;
	}
}
