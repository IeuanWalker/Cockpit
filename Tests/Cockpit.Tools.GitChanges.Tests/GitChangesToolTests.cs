using Cockpit.Tooling;
using Cockpit.Tools.GitChanges.Diff;
using Cockpit.Tools.GitChanges.Sources;
using Shouldly;

namespace Cockpit.Tools.GitChanges.Tests;

public sealed class GitChangesToolTests
{
	[Fact]
	public async Task Multiple_instances_keep_independent_file_and_view_choices()
	{
		FakeSource source = new();
		GitChangesToolSession session = new(Session(), source, new(true, null, source.Repository));
		GitChangesToolInstance first = (GitChangesToolInstance)await session.CreateInstanceAsync(Context("first"), TestContext.Current.CancellationToken);
		GitChangesToolInstance second = (GitChangesToolInstance)await session.CreateInstanceAsync(Context("second"), TestContext.Current.CancellationToken);
		await first.RefreshAsync(TestContext.Current.CancellationToken); await second.RefreshAsync(TestContext.Current.CancellationToken);

		first.SelectFile(first.Snapshot!.Files[0]); first.SetSplitView(true);
		second.SelectFile(second.Snapshot!.Files[1]); second.SetTreeView(true);

		first.DisplayedFile!.RelativePath.ShouldBe("src/a.cs");
		second.DisplayedFile!.RelativePath.ShouldBe("docs/b.md");
		first.State.SplitDiffView.ShouldBeTrue();
		second.State.SplitDiffView.ShouldBeFalse();
		source.SubscriptionCount.ShouldBe(2);
	}

	[Fact]
	public void Diff_parser_preserves_hunks_and_line_numbers()
	{
		ParsedDiff parsed = DiffParser.Parse("--- a/a.cs\n+++ b/a.cs\n@@ -2,2 +2,2 @@\n-old\n+new\n same");
		parsed.OldPath.ShouldBe("a.cs");
		parsed.NewPath.ShouldBe("a.cs");
		parsed.LinesAdded.ShouldBe(1);
		parsed.LinesRemoved.ShouldBe(1);
		parsed.Hunks.Single().Lines[0].OldLineNumber.ShouldBe(2);
		parsed.Hunks.Single().Lines[1].NewLineNumber.ShouldBe(2);
	}

	[Fact]
	public async Task Fresh_view_selects_the_first_file_and_tree_directories_start_expanded()
	{
		FakeSource source = new();
		GitChangesToolSession session = new(Session(), source, new(true, null, source.Repository));
		GitChangesToolInstance instance = (GitChangesToolInstance)await session.CreateInstanceAsync(Context("defaults"), TestContext.Current.CancellationToken);

		await instance.RefreshAsync(TestContext.Current.CancellationToken);

		instance.DisplayedFile!.RelativePath.ShouldBe("src/a.cs");
		instance.IsDirectoryExpanded("src").ShouldBeTrue();
		instance.ToggleDirectory("src");
		instance.IsDirectoryExpanded("src").ShouldBeFalse();
	}

	[Fact]
	public async Task Availability_is_decided_for_each_session_before_use()
	{
		FakeSource source = new() { Available = false };
		GitChangesWorkspaceTool tool = new(source);
		IWorkspaceToolSession session = await tool.CreateSessionAsync(new(Session() with { RepositoryRoot = null }, new Dictionary<string, object?>()), TestContext.Current.CancellationToken);
		session.IsAvailable.ShouldBeFalse();
		session.UnavailabilityReason.ShouldBe("No repository");
	}

	static WorkspaceSessionInfo Session() => new(new("runtime"), new("storage"), 2, "windows", "D:/repo", "D:/repo", "connected", new HashSet<string>());
	static WorkspaceToolInstanceContext Context(string id) => new(new(id), Session(), new FakeHost(), default);

	sealed class FakeSource : IGitChangesToolSource
	{
		public bool Available { get; init; } = true;
		public int SubscriptionCount { get; private set; }
		public GitRepositoryIdentity Repository { get; } = new("D:/repo", "origin", "main");
		public ValueTask<GitAvailability> GetAvailabilityAsync(string key, long epoch, string? root, CancellationToken token) =>
			ValueTask.FromResult(Available && root is not null ? new GitAvailability(true, null, Repository) : new GitAvailability(false, "No repository", null));
		public ValueTask<GitRepositorySnapshot?> GetSnapshotAsync(string key, long epoch, GitRepositoryBinding binding, CancellationToken token) =>
			ValueTask.FromResult<GitRepositorySnapshot?>(new()
			{
				RuntimeSessionKey = key,
				IdentityEpoch = epoch,
				Generation = 1,
				Repository = Repository,
				CapturedAt = DateTimeOffset.UtcNow,
				Files = [new() { RelativePath = "src/a.cs", Status = GitChangeStatus.Modified, Diff = "@@ -1 +1 @@\n-a\n+b" }, new() { RelativePath = "docs/b.md", Status = GitChangeStatus.Added, Diff = "+b" }]
			});
		public IDisposable Subscribe(string key, long epoch, Action<GitSourceChange> changed) { SubscriptionCount++; return new Subscription(); }
		public ValueTask<bool> RevealFileAsync(string key, long epoch, string root, string path, CancellationToken token) => ValueTask.FromResult(true);
		sealed class Subscription : IDisposable { public void Dispose() { } }
	}

	sealed class FakeHost : IWorkspaceToolHost
	{
		public ValueTask<WorkspaceHostCommandResult> ActivateSelfAsync(CancellationToken c) => Applied(); public ValueTask<WorkspaceHostCommandResult> CloseSelfAsync(CancellationToken c) => Applied();
		public ValueTask<WorkspaceHostCommandResult> OpenNewToolAsync(string id, CancellationToken c) => Applied(); public ValueTask<WorkspaceHostCommandResult> SetTitleAsync(string? t, CancellationToken c) => Applied();
		public ValueTask<WorkspaceHostCommandResult> RequestLayoutNotificationAsync(CancellationToken c) => Applied(); public ValueTask<WorkspaceHostCommandResult> AppendToComposerAsync(string value, CancellationToken c) => Applied();
		public ValueTask<WorkspaceHostCommandResult> RevealFileAsync(string path, CancellationToken c) => Applied(); static ValueTask<WorkspaceHostCommandResult> Applied() => ValueTask.FromResult(WorkspaceHostCommandResult.Applied());
	}
}
