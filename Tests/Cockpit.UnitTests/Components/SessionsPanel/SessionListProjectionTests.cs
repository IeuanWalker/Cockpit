using Cockpit.Components.Pages.SessionsPanel;
using Cockpit.Features.Sessions.Models;
using GitHub.Copilot;
using Shouldly;

namespace Cockpit.UnitTests.Components.SessionsPanel;

public class SessionListProjectionTests
{
	static readonly ModelInfo testModel = new() { Id = "test", Name = "Test" };
	static readonly DateTime baseline = new(2026, 7, 25, 12, 0, 0, DateTimeKind.Utc);

	[Fact]
	public void Search_SortsSessionsAndCreatesScopedStableKeys()
	{
		SessionModel older = CreateSession("older", baseline);
		SessionModel newer = CreateSession("newer", baseline.AddMinutes(1));

		IReadOnlyList<SessionListRow> rows = Build(
			[older, newer],
			searchText: "er",
			pinnedSessionIds: new HashSet<string>(["newer"], StringComparer.Ordinal));

		rows.Select(row => row.Key).ShouldBe(["session:search:newer", "session:search:older"]);
		rows.All(row => row is SessionListSessionRow { IndentLevel: 0 }).ShouldBeTrue();
	}

	[Fact]
	public void Sections_PutChatsBeforeProjectsAndRecents()
	{
		SessionModel project = CreateSession("project", baseline, "C:\\work\\Cockpit", "owner/Cockpit");
		SessionModel chat = CreateSession("chat", baseline.AddMinutes(-1), string.Empty);
		string projectId = ProjectId(project);
		HashSet<SessionListSection> expandedSections = [SessionListSection.Chats, SessionListSection.Projects];

		IReadOnlyList<SessionListRow> rows = Build(
			[project, chat],
			expandedSections: expandedSections,
			expandedProjectGroups: new HashSet<string>([projectId], SessionProjectIdentityResolver.ProjectIdComparer));

		rows.Select(row => row.Key).ShouldBe([
			"section:Chats",
			"session:chats:chat",
			"section:Projects",
			$"project:projects:{projectId}",
			$"session:projects:{projectId}:project",
			"section:Recents"
		]);
		rows.OfType<SessionListSessionRow>().Select(row => row.IndentLevel).ShouldBe([1, 2]);
		SessionListProjectHeaderRow projectHeader = rows.OfType<SessionListProjectHeaderRow>().Single();
		projectHeader.CreateSessionPath.ShouldBe("C:\\work\\Cockpit");
		projectHeader.SessionCount.ShouldBe(1);
	}

	[Fact]
	public void PinnedSection_IsHiddenWhenThereAreNoPinsOrOnlyUnresolvedPins()
	{
		SessionModel session = CreateSession("session", baseline, "C:\\work\\Cockpit", "owner/Cockpit");

		IReadOnlyList<SessionListRow> withoutPins = Build([session]);
		IReadOnlyList<SessionListRow> withUnresolvedPins = Build(
			[session],
			pinnedSessionIds: new HashSet<string>(["missing-session"], StringComparer.Ordinal),
			pinnedProjectIds: new HashSet<string>(["repo:missing/project"], SessionProjectIdentityResolver.ProjectIdComparer));

		withoutPins.OfType<SessionListSectionHeaderRow>().Select(row => row.Section)
			.ShouldBe([SessionListSection.Chats, SessionListSection.Projects, SessionListSection.Recents]);
		withUnresolvedPins.OfType<SessionListSectionHeaderRow>().Select(row => row.Section)
			.ShouldBe([SessionListSection.Chats, SessionListSection.Projects, SessionListSection.Recents]);
	}

	[Fact]
	public void ExpandedPinnedSection_RendersProjectsBeforeIndividualSessionsAndAppearsFirst()
	{
		SessionModel older = CreateSession("older", baseline, "C:\\work\\Cockpit", "owner/Cockpit");
		SessionModel newer = CreateSession("newer", baseline.AddMinutes(1), "C:\\work\\Cockpit", "owner/Cockpit");
		string projectId = ProjectId(older);

		IReadOnlyList<SessionListRow> rows = Build(
			[older, newer],
			expandedSections: new HashSet<SessionListSection>([SessionListSection.Pinned]),
			expandedProjectGroups: new HashSet<string>([projectId], SessionProjectIdentityResolver.ProjectIdComparer),
			pinnedSessionIds: new HashSet<string>(["older"], StringComparer.Ordinal),
			pinnedProjectIds: new HashSet<string>([projectId], SessionProjectIdentityResolver.ProjectIdComparer));

		rows.Select(row => row.Key).ShouldBe([
			"section:Pinned",
			$"project:pinned:{projectId}",
			$"session:pinned:{projectId}:newer",
			$"session:pinned:{projectId}:older",
			"section:Chats",
			"section:Projects",
			"section:Recents"
		]);
		rows.OfType<SessionListSectionHeaderRow>().First().Name.ShouldBe("Pinned");
	}

	[Fact]
	public void PinnedProject_IsRemovedFromProjectsAndRecents()
	{
		List<SessionModel> sessions = [.. Enumerable.Range(0, 7)
			.Select(index => CreateSession(
				$"session-{index}",
				baseline.AddMinutes(-index),
				"C:\\work\\Cockpit",
				"owner/Cockpit"))];
		string projectId = ProjectId(sessions[0]);

		IReadOnlyList<SessionListRow> rows = Build(
			sessions,
			expandedSections: new HashSet<SessionListSection>([SessionListSection.Pinned, SessionListSection.Projects, SessionListSection.Recents]),
			expandedProjectGroups: new HashSet<string>([projectId], SessionProjectIdentityResolver.ProjectIdComparer),
			pinnedProjectIds: new HashSet<string>([projectId], SessionProjectIdentityResolver.ProjectIdComparer));

		rows.OfType<SessionListProjectHeaderRow>().Select(row => row.Key)
			.ShouldBe([$"project:pinned:{projectId}"]);
		rows.OfType<SessionListShowMoreRow>().Select(row => row.Key)
			.ShouldBe([$"show-more:pinned:{projectId}"]);
		rows.OfType<SessionListRecentsRow>().Single().Sessions.ShouldBeEmpty();
	}

	[Fact]
	public void PinnedSession_IsRemovedFromItsProjectAndRecents()
	{
		SessionModel pinned = CreateSession("pinned", baseline.AddMinutes(1), "C:\\work\\Cockpit", "owner/Cockpit");
		SessionModel regular = CreateSession("regular", baseline, "C:\\work\\Cockpit", "owner/Cockpit");
		string projectId = ProjectId(pinned);

		IReadOnlyList<SessionListRow> rows = Build(
			[pinned, regular],
			expandedSections: new HashSet<SessionListSection>([SessionListSection.Pinned, SessionListSection.Projects, SessionListSection.Recents]),
			expandedProjectGroups: new HashSet<string>([projectId], SessionProjectIdentityResolver.ProjectIdComparer),
			pinnedSessionIds: new HashSet<string>([pinned.Id], StringComparer.Ordinal));

		rows.OfType<SessionListSessionRow>().Select(row => row.Key).ShouldBe([
			"session:pinned-sessions:pinned",
			$"session:projects:{projectId}:regular"
		]);
		rows.OfType<SessionListProjectHeaderRow>().Single().SessionCount.ShouldBe(1);
		rows.OfType<SessionListRecentsRow>().Single().Sessions.Select(session => session.Id).ShouldBe([regular.Id]);
	}

	[Fact]
	public void PinnedChat_IsRemovedFromChatsAndRecents()
	{
		SessionModel pinned = CreateSession("pinned", baseline.AddMinutes(1), string.Empty);
		SessionModel regular = CreateSession("regular", baseline, string.Empty);

		IReadOnlyList<SessionListRow> rows = Build(
			[pinned, regular],
			expandedSections: new HashSet<SessionListSection>([SessionListSection.Pinned, SessionListSection.Chats, SessionListSection.Recents]),
			pinnedSessionIds: new HashSet<string>([pinned.Id], StringComparer.Ordinal));

		rows.OfType<SessionListSessionRow>().Select(row => row.Key).ShouldBe([
			"session:pinned-sessions:pinned",
			"session:chats:regular"
		]);
		rows.OfType<SessionListRecentsRow>().Single().Sessions.Select(session => session.Id).ShouldBe([regular.Id]);
	}

	[Fact]
	public void CollapsedProject_DoesNotRenderItsSessions()
	{
		List<SessionModel> sessions = [.. Enumerable.Range(0, 7)
			.Select(index => CreateSession(
				$"session-{index}",
				baseline.AddMinutes(-index),
				"C:\\work\\Cockpit",
				"Cockpit"))];

		IReadOnlyList<SessionListRow> rows = Build(
			sessions,
			expandedSections: new HashSet<SessionListSection>([SessionListSection.Projects]));

		rows.OfType<SessionListSectionHeaderRow>().Single(row => row.Section == SessionListSection.Projects).IsExpanded.ShouldBeTrue();
		rows.OfType<SessionListProjectHeaderRow>().Single().IsExpanded.ShouldBeFalse();
		rows.OfType<SessionListSessionRow>().ShouldBeEmpty();
		rows.OfType<SessionListShowMoreRow>().ShouldBeEmpty();
	}

	[Fact]
	public void ProjectPagination_ShowsConfiguredPageAndKeepsBothControlsAvailable()
	{
		List<SessionModel> sessions = [.. Enumerable.Range(0, 20)
			.Select(index => CreateSession(
				$"session-{index}",
				baseline.AddMinutes(-index),
				"C:\\work\\Cockpit",
				"Cockpit"))];
		Dictionary<string, int> sessionLimits = new(StringComparer.OrdinalIgnoreCase)
		{
			[ProjectId(sessions[0])] = 15
		};
		string projectId = ProjectId(sessions[0]);

		IReadOnlyList<SessionListRow> rows = Build(
			sessions,
			expandedSections: new HashSet<SessionListSection>([SessionListSection.Projects]),
			expandedProjectGroups: new HashSet<string>([projectId], SessionProjectIdentityResolver.ProjectIdComparer),
			sessionLimits: sessionLimits);

		rows.OfType<SessionListSessionRow>().Count().ShouldBe(15);
		SessionListShowMoreRow showMore = rows.OfType<SessionListShowMoreRow>().Single();
		showMore.HasMore.ShouldBeTrue();
		showMore.CanShowLess.ShouldBeTrue();
		showMore.VisibleCount.ShouldBe(15);
		showMore.TotalCount.ShouldBe(20);
	}

	[Fact]
	public void RecentsSection_ProducesOneVirtualizableCollectionInActivityOrder()
	{
		List<SessionModel> sessions = [.. Enumerable.Range(0, 100)
			.Select(index => CreateSession($"session-{index}", baseline.AddMinutes(-index)))];

		IReadOnlyList<SessionListRow> rows = Build(sessions, expandedSections: new HashSet<SessionListSection>([SessionListSection.Recents]));

		SessionListRecentsRow recents = rows.OfType<SessionListRecentsRow>().Single();
		recents.Sessions.Count.ShouldBe(100);
		recents.Sessions.Select(session => session.Id).ShouldBe(
			Enumerable.Range(0, 100).Select(index => $"session-{index}"));
		rows.OfType<SessionListSessionRow>().ShouldBeEmpty();
		rows.OfType<SessionListShowMoreRow>().ShouldBeEmpty();
	}

	[Fact]
	public void CollapsedRecentsSection_DoesNotProduceAVirtualizableCollection()
	{
		List<SessionModel> sessions = [.. Enumerable.Range(0, 20)
			.Select(index => CreateSession($"session-{index}", baseline.AddMinutes(-index)))];

		IReadOnlyList<SessionListRow> rows = Build(sessions);

		rows.OfType<SessionListRecentsRow>().ShouldBeEmpty();
	}

	[Fact]
	public void MostRecentProjectGroup_UsesTheProjectWithTheLatestSession()
	{
		SessionModel older = CreateSession("older", baseline, "C:\\work\\Older", "owner/Older");
		SessionModel newer = CreateSession("newer", baseline.AddMinutes(1), "C:\\work\\Newer", "owner/Newer");
		SessionModel chat = CreateSession("chat", baseline.AddMinutes(2), string.Empty);

		SessionListProjectionSource source = SessionListProjection.CreateSource([older, newer, chat]);

		source.MostRecentProjectGroupId.ShouldBe(ProjectId(newer));
		source.ProjectGroupIds.ShouldBe(new HashSet<string>([ProjectId(older), ProjectId(newer)], SessionProjectIdentityResolver.ProjectIdComparer), ignoreOrder: true);
	}

	[Fact]
	public void Source_CanBeReusedAcrossSearchAndDisplayOptionChanges()
	{
		SessionModel first = CreateSession("first", baseline, "C:\\work\\First", "owner/First");
		SessionModel second = CreateSession("second", baseline.AddMinutes(1), "C:\\work\\Second", "owner/Second");
		SessionListProjectionSource source = SessionListProjection.CreateSource([first, second]);

		IReadOnlyList<SessionListRow> searchRows = Build(source, searchText: "first");
		IReadOnlyList<SessionListRow> sectionRows = Build(
			source,
			expandedSections: new HashSet<SessionListSection>([SessionListSection.Projects]));

		searchRows.Select(row => row.Key).ShouldBe(["session:search:first"]);
		sectionRows.OfType<SessionListProjectHeaderRow>().Select(row => row.GroupId)
			.ShouldBe([ProjectId(second), ProjectId(first)]);
	}

	[Fact]
	public void Projects_WithTheSameNameInDifferentLocations_RemainSeparateAndAreDisambiguated()
	{
		string firstRoot = ProjectPath("ClientA", "App");
		string secondRoot = ProjectPath("ClientB", "App");
		SessionModel first = CreateSession("first", baseline, firstRoot, "owner-a/App");
		SessionModel second = CreateSession("second", baseline.AddMinutes(1), secondRoot, "owner-b/App");

		IReadOnlyList<SessionListRow> rows = Build(
			[first, second],
			expandedSections: new HashSet<SessionListSection>([SessionListSection.Projects]));

		SessionListProjectHeaderRow[] projects = [.. rows.OfType<SessionListProjectHeaderRow>()];
		projects.Length.ShouldBe(2);
		projects.Select(project => project.GroupId).Distinct(SessionProjectIdentityResolver.ProjectIdComparer).Count().ShouldBe(2);
		projects.Select(project => project.Name).ShouldContain("App - ClientA");
		projects.Select(project => project.Name).ShouldContain("App - ClientB");
		projects.Select(project => project.CreateSessionPath).ShouldContain(firstRoot);
		projects.Select(project => project.CreateSessionPath).ShouldContain(secondRoot);
	}

	[Fact]
	public void Projects_WithTheSameGitRootAndDifferentWorkingSubdirectories_AreCombined()
	{
		string root = ProjectPath("Cockpit");
		SessionModel first = CreateSession("first", baseline, Path.Combine(root, "src"), "owner/Cockpit", gitRoot: root);
		SessionModel second = CreateSession("second", baseline.AddMinutes(1), Path.Combine(root, "Tests"), null, gitRoot: root);
		string projectId = ProjectId(first);

		IReadOnlyList<SessionListRow> rows = Build(
			[first, second],
			expandedSections: new HashSet<SessionListSection>([SessionListSection.Projects]),
			expandedProjectGroups: new HashSet<string>([projectId], SessionProjectIdentityResolver.ProjectIdComparer));

		SessionListProjectHeaderRow project = rows.OfType<SessionListProjectHeaderRow>().Single();
		project.CreateSessionPath.ShouldBe(root);
		project.Repository.ShouldBe("owner/Cockpit");
		project.SessionCount.ShouldBe(2);
		rows.OfType<SessionListSessionRow>().Select(row => row.Session.Id).ShouldBe(["second", "first"]);
	}

	[Fact]
	public void Projects_MainCheckoutAndWorktreeOfTheSameRepositoryAreCombined()
	{
		string checkoutRoot = ProjectPath("Github-Mine", "Cockpit");
		string worktreeRoot = ProjectPath("copilot-worktrees", "Cockpit", "ieuanwalker-symmetrical-system");
		SessionModel checkout = CreateSession("checkout", baseline, checkoutRoot, "IeuanWalker/Cockpit", gitRoot: checkoutRoot);
		SessionModel worktree = CreateSession("worktree", baseline.AddMinutes(1), worktreeRoot, "IeuanWalker/Cockpit", gitRoot: worktreeRoot);

		IReadOnlyList<SessionListRow> rows = Build(
			[checkout, worktree],
			expandedSections: new HashSet<SessionListSection>([SessionListSection.Projects]));

		SessionListProjectHeaderRow project = rows.OfType<SessionListProjectHeaderRow>().Single();
		project.Name.ShouldBe("Cockpit");
		project.CreateSessionPath.ShouldBe(checkoutRoot);
		project.Repository.ShouldBe("IeuanWalker/Cockpit");
		project.SessionCount.ShouldBe(2);
	}

	[Fact]
	public void Projects_DuplicateNamesUseTheShortestUniqueParentSuffix()
	{
		string firstRoot = ProjectPath("ClientA", "Shared", "App");
		string secondRoot = ProjectPath("ClientB", "Shared", "App");
		SessionModel first = CreateSession("first", baseline, firstRoot);
		SessionModel second = CreateSession("second", baseline.AddMinutes(1), secondRoot);

		IReadOnlyList<SessionListRow> rows = Build(
			[first, second],
			expandedSections: new HashSet<SessionListSection>([SessionListSection.Projects]));

		string[] names = [.. rows.OfType<SessionListProjectHeaderRow>().Select(project => project.Name)];
		names.ShouldContain($"App - {Path.Combine("ClientA", "Shared")}");
		names.ShouldContain($"App - {Path.Combine("ClientB", "Shared")}");
	}

	[Fact]
	public void ActiveSearch_FiltersByTitleCwdAndRepository()
	{
		SessionModel match = CreateSession("match", baseline, "C:\\work\\Cockpit\\", "owner/Cockpit", "Performance work");
		SessionModel wrongTitle = CreateSession("title", baseline, "C:\\work\\Cockpit", "owner/Cockpit", "Other");
		SessionModel wrongRepo = CreateSession("repo", baseline, "C:\\work\\Cockpit", "owner/Other", "Performance work");

		IReadOnlyList<SessionListRow> rows = SessionListProjection.Build(
			SessionListProjection.CreateSource([match, wrongTitle, wrongRepo]),
			new SessionListProjectionOptions(
				"performance",
				new HashSet<string>(["C:\\work\\Cockpit"], StringComparer.OrdinalIgnoreCase),
				new HashSet<string>(["owner/Cockpit"], StringComparer.OrdinalIgnoreCase),
				new HashSet<SessionListSection>(),
				new HashSet<string>(StringComparer.OrdinalIgnoreCase),
				new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase),
				new HashSet<string>(StringComparer.Ordinal),
				new HashSet<string>(SessionProjectIdentityResolver.ProjectIdComparer)));

		rows.Select(row => row.Key).ShouldBe(["session:search:match"]);
	}

	static IReadOnlyList<SessionListRow> Build(
		IEnumerable<SessionModel> sessions,
		string searchText = "",
		IReadOnlySet<SessionListSection>? expandedSections = null,
		IReadOnlySet<string>? expandedProjectGroups = null,
		IReadOnlyDictionary<string, int>? sessionLimits = null,
		IReadOnlySet<string>? pinnedSessionIds = null,
		IReadOnlySet<string>? pinnedProjectIds = null) =>
		Build(
			SessionListProjection.CreateSource(sessions),
			searchText,
			expandedSections,
			expandedProjectGroups,
			sessionLimits,
			pinnedSessionIds,
			pinnedProjectIds);

	static IReadOnlyList<SessionListRow> Build(
		SessionListProjectionSource source,
		string searchText = "",
		IReadOnlySet<SessionListSection>? expandedSections = null,
		IReadOnlySet<string>? expandedProjectGroups = null,
		IReadOnlyDictionary<string, int>? sessionLimits = null,
		IReadOnlySet<string>? pinnedSessionIds = null,
		IReadOnlySet<string>? pinnedProjectIds = null) =>
		SessionListProjection.Build(
			source,
			new SessionListProjectionOptions(
				searchText,
				new HashSet<string>(StringComparer.OrdinalIgnoreCase),
				new HashSet<string>(StringComparer.OrdinalIgnoreCase),
				expandedSections ?? new HashSet<SessionListSection>(),
				expandedProjectGroups ?? new HashSet<string>(StringComparer.OrdinalIgnoreCase),
				sessionLimits ?? new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase),
				pinnedSessionIds ?? new HashSet<string>(StringComparer.Ordinal),
				pinnedProjectIds ?? new HashSet<string>(SessionProjectIdentityResolver.ProjectIdComparer)));

	static SessionModel CreateSession(
		string id,
		DateTime lastActivity,
		string? cwd = "",
		string? repository = null,
		string? title = null,
		string? gitRoot = null) => new()
		{
			Id = id,
			Title = title ?? id,
			CreatedAt = lastActivity,
			LastActivity = lastActivity,
			Model = testModel,
			Context = new Cockpit.Features.Sessions.Models.SessionContext
			{
				CurrentWorkingDirectory = cwd,
				WorkspacePath = null,
				GitRoot = gitRoot,
				Repository = repository,
				Branch = null
			}
		};

	static string ProjectId(SessionModel session) => SessionProjectIdentityResolver.Resolve(session).ShouldNotBeNull().Id;

	static string ProjectPath(params string[] segments) => Path.GetFullPath(Path.Combine([Path.GetTempPath(), "CockpitProjectTests", .. segments]));
}
