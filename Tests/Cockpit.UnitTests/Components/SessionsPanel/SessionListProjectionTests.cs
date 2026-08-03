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
	public void UpdatedView_SortsSessionsAndCreatesStableKeys()
	{
		SessionModel older = CreateSession("older", baseline);
		SessionModel newer = CreateSession("newer", baseline.AddMinutes(1));

		IReadOnlyList<SessionListRow> rows = Build([older, newer], SessionListViewMode.Updated);

		rows.Select(row => row.Key).ShouldBe(["session:newer", "session:older"]);
		rows.All(row => row is SessionListSessionRow { IsIndented: false }).ShouldBeTrue();
	}

	[Fact]
	public void ProjectView_PutsQuickChatFirstAndFlattensExpandedGroup()
	{
		SessionModel project = CreateSession("project", baseline, "C:\\work\\Cockpit", "owner/Cockpit");
		SessionModel quickChat = CreateSession("quick", baseline.AddMinutes(-1), string.Empty);

		IReadOnlyList<SessionListRow> rows = Build(
			[project, quickChat],
			SessionListViewMode.Project,
			expandedGroups: new HashSet<string>(["quick-chat", "name:Cockpit"], StringComparer.OrdinalIgnoreCase));

		rows.Select(row => row.Key).ShouldBe([
			"project:quick-chat",
			"session:quick",
			"project:name:Cockpit",
			"session:project"
		]);
		rows[1].ShouldBeOfType<SessionListSessionRow>().IsIndented.ShouldBeTrue();
	}

	[Fact]
	public void ProjectView_IncludesActiveSessionBeyondCollapsedLimit()
	{
		List<SessionModel> sessions = [.. Enumerable.Range(0, 7)
			.Select(index => CreateSession(
				$"session-{index}",
				baseline.AddMinutes(-index),
				"C:\\work\\Cockpit",
				"Cockpit"))];

		IReadOnlyList<SessionListRow> rows = Build(
			sessions,
			SessionListViewMode.Project,
			activeSessionId: "session-6");

		SessionListProjectHeaderRow header = rows[0].ShouldBeOfType<SessionListProjectHeaderRow>();
		header.IsExpanded.ShouldBeTrue();
		rows.OfType<SessionListSessionRow>().Select(row => row.Session.Id).ShouldBe([
			"session-0", "session-1", "session-2", "session-3", "session-4", "session-6"
		]);
		rows[^1].ShouldBeOfType<SessionListShowMoreRow>().IsExpanded.ShouldBeFalse();
	}

	[Fact]
	public void ProjectView_ExpandedLimitShowsFifteenSessionsAndShowLessRow()
	{
		List<SessionModel> sessions = [.. Enumerable.Range(0, 20)
			.Select(index => CreateSession(
				$"session-{index}",
				baseline.AddMinutes(-index),
				"C:\\work\\Cockpit",
				"Cockpit"))];
		HashSet<string> groups = new(["name:Cockpit"], StringComparer.OrdinalIgnoreCase);

		IReadOnlyList<SessionListRow> rows = Build(
			sessions,
			SessionListViewMode.Project,
			expandedGroups: groups,
			expandedSessionGroups: groups);

		rows.OfType<SessionListSessionRow>().Count().ShouldBe(15);
		rows[^1].ShouldBeOfType<SessionListShowMoreRow>().IsExpanded.ShouldBeTrue();
	}

	[Fact]
	public void ActiveSearch_FiltersByTitleCwdAndRepository()
	{
		SessionModel match = CreateSession("match", baseline, "C:\\work\\Cockpit\\", "owner/Cockpit", "Performance work");
		SessionModel wrongTitle = CreateSession("title", baseline, "C:\\work\\Cockpit", "owner/Cockpit", "Other");
		SessionModel wrongRepo = CreateSession("repo", baseline, "C:\\work\\Cockpit", "owner/Other", "Performance work");

		IReadOnlyList<SessionListRow> rows = SessionListProjection.Build(
			[match, wrongTitle, wrongRepo],
			new SessionListProjectionOptions(
				SessionListViewMode.Project,
				null,
				"performance",
				new HashSet<string>(["C:\\work\\Cockpit"], StringComparer.OrdinalIgnoreCase),
				new HashSet<string>(["owner/Cockpit"], StringComparer.OrdinalIgnoreCase),
				new HashSet<string>(StringComparer.OrdinalIgnoreCase),
				new HashSet<string>(StringComparer.OrdinalIgnoreCase)));

		rows.Select(row => row.Key).ShouldBe(["session:match"]);
	}

	static IReadOnlyList<SessionListRow> Build(
		IEnumerable<SessionModel> sessions,
		SessionListViewMode mode,
		string? activeSessionId = null,
		IReadOnlySet<string>? expandedGroups = null,
		IReadOnlySet<string>? expandedSessionGroups = null) =>
		SessionListProjection.Build(
			sessions,
			new SessionListProjectionOptions(
				mode,
				activeSessionId,
				string.Empty,
				new HashSet<string>(StringComparer.OrdinalIgnoreCase),
				new HashSet<string>(StringComparer.OrdinalIgnoreCase),
				expandedGroups ?? new HashSet<string>(StringComparer.OrdinalIgnoreCase),
				expandedSessionGroups ?? new HashSet<string>(StringComparer.OrdinalIgnoreCase)));

	static SessionModel CreateSession(
		string id,
		DateTime lastActivity,
		string? cwd = "",
		string? repository = null,
		string? title = null) => new()
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
			GitRoot = null,
			Repository = repository,
			Branch = null
		}
	};
}
