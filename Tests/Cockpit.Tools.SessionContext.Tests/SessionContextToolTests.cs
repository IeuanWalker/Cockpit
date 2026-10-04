using Cockpit.Tooling;
using Cockpit.Tools.SessionContext.Sources;
using Cockpit.Tools.SessionContext.State;
using Shouldly;

namespace Cockpit.Tools.SessionContext.Tests;

public sealed class SessionContextToolTests
{
	[Fact]
	public async Task Instances_keep_independent_presentation_state_over_shared_source()
	{
		FakeSource source = new();
		SessionContextToolSession session = new(Session(), source);
		SessionContextToolInstance first = (SessionContextToolInstance)await session.CreateInstanceAsync(Context("one"), TestContext.Current.CancellationToken);
		SessionContextToolInstance second = (SessionContextToolInstance)await session.CreateInstanceAsync(Context("two"), TestContext.Current.CancellationToken);

		first.SetFilter("agents");
		first.SetSectionExpanded("skills", false);
		second.SetFilter("commands");

		first.State.FilterText.ShouldBe("agents");
		second.State.FilterText.ShouldBe("commands");
		second.State.SectionExpansion.ContainsKey("skills").ShouldBeFalse();
		source.SubscriptionCount.ShouldBe(2);
		await session.DisposeAsync();
	}

	[Fact]
	public async Task Checkpoint_round_trips_cached_content_and_stable_keys()
	{
		FakeSource source = new();
		SessionContextToolInstance original = new(Context("one"), source);
		await original.RestoreAsync(null, TestContext.Current.CancellationToken);
		original.Select("agents", "reviewer");
		await original.RefreshAsync(TestContext.Current.CancellationToken);
		ToolCheckpoint checkpoint = await original.CaptureAsync(WriteContext("one"), TestContext.Current.CancellationToken);

		SessionContextToolInstance restored = new(Context("two"), source);
		ToolRestoreResult result = await restored.RestoreAsync(ReadContext(checkpoint, "one"), TestContext.Current.CancellationToken);

		result.Status.ShouldBe(ToolRestoreStatus.Restored);
		restored.State.SelectedItem.ShouldBe(new SessionContextSelection("agents", "reviewer"));
		restored.Snapshot!.Sections.Single().Items.Single().Key.ShouldBe("reviewer");
		restored.IsShowingCachedSnapshot.ShouldBeTrue();
	}

	[Fact]
	public async Task First_live_refresh_replaces_a_cached_snapshot_with_a_higher_generation()
	{
		FakeSource source = new() { Generation = 500 };
		SessionContextToolInstance original = new(Context("one"), source);
		await original.RestoreAsync(null, TestContext.Current.CancellationToken);
		await original.RefreshAsync(TestContext.Current.CancellationToken);

		ToolCheckpoint checkpoint = await original.CaptureAsync(WriteContext("one"), TestContext.Current.CancellationToken);
		SessionContextToolInstance restored = new(Context("two"), source);
		await restored.RestoreAsync(ReadContext(checkpoint, "two"), TestContext.Current.CancellationToken);

		source.Label = "Live reviewer";
		source.Generation = 1;
		await restored.RefreshAsync(TestContext.Current.CancellationToken);

		restored.Snapshot!.Sections.Single().Items.Single().Label.ShouldBe("Live reviewer");
		restored.IsShowingCachedSnapshot.ShouldBeFalse();
	}

	[Fact]
	public async Task Version_one_state_keeps_presentation_but_discards_legacy_cached_snapshot()
	{
		FakeSource source = new();
		SessionContextToolInstance original = new(Context("one"), source);
		await original.RestoreAsync(null, TestContext.Current.CancellationToken);
		original.SetSectionExpanded("skills", true);
		await original.RefreshAsync(TestContext.Current.CancellationToken);
		ToolCheckpoint current = await original.CaptureAsync(WriteContext("one"), TestContext.Current.CancellationToken);
		ToolCheckpoint legacy = current with { SchemaVersion = 1 };

		SessionContextToolInstance restored = new(Context("two"), source);
		ToolRestoreResult result = await restored.RestoreAsync(ReadContext(legacy, "two"), TestContext.Current.CancellationToken);

		result.Status.ShouldBe(ToolRestoreStatus.Restored);
		restored.State.SectionExpansion["skills"].ShouldBeTrue();
		restored.Snapshot.ShouldBeNull();
		restored.IsShowingCachedSnapshot.ShouldBeFalse();
	}

	static WorkspaceSessionInfo Session() => new(new("runtime"), new("storage"), 4, "windows", "D:/repo", "D:/repo", "connected", new HashSet<string>());
	static WorkspaceToolInstanceContext Context(string id) => new(new(id), Session(), new FakeHost(), default);
	static ToolStateWriteContext WriteContext(string id) => new(new(id), (_, _, _) => throw new NotSupportedException());
	static ToolStateReadContext ReadContext(ToolCheckpoint checkpoint, string id) => new(checkpoint.SchemaVersion, checkpoint.MutationVersion,
		new(id), checkpoint.Metadata, checkpoint.Assets, (_, _) => throw new NotSupportedException());

	sealed class FakeSource : ISessionContextToolSource
	{
		public int SubscriptionCount { get; private set; }
		public string Label { get; set; } = "Reviewer";
		public long Generation { get; set; } = 1;
		public ValueTask<SessionContextSnapshot?> GetSnapshotAsync(string key, long epoch, CancellationToken token) => ValueTask.FromResult<SessionContextSnapshot?>(new()
		{
			RuntimeSessionKey = key,
			IdentityEpoch = epoch,
			Generation = Generation,
			CapturedAt = DateTimeOffset.UtcNow,
			Sections = [new() { SectionId = "agents", DisplayName = "Agents", Items = [new() { Key = "reviewer", Label = Label, Kind = "agent" }] }]
		});
		public IDisposable Subscribe(string key, long epoch, Action<SessionContextSourceChange> changed) { SubscriptionCount++; return new Subscription(); }
		public ValueTask RefreshSectionAsync(string key, long epoch, string section, CancellationToken token) => ValueTask.CompletedTask;
		public ValueTask<bool> AddItemToMessageAsync(string key, long epoch, string section, string item, CancellationToken token) => ValueTask.FromResult(true);
		public ValueTask<SessionContextCommandResult> ExecuteAsync(string key, long epoch, SessionContextCommandRequest request, CancellationToken token) =>
			ValueTask.FromResult(SessionContextCommandResult.Success());
		sealed class Subscription : IDisposable { public void Dispose() { } }
	}

	sealed class FakeHost : IWorkspaceToolHost
	{
		public ValueTask<WorkspaceHostCommandResult> ActivateSelfAsync(CancellationToken c) => ValueTask.FromResult(WorkspaceHostCommandResult.Applied());
		public ValueTask<WorkspaceHostCommandResult> CloseSelfAsync(CancellationToken c) => ValueTask.FromResult(WorkspaceHostCommandResult.Applied());
		public ValueTask<WorkspaceHostCommandResult> OpenNewToolAsync(string id, CancellationToken c) => ValueTask.FromResult(WorkspaceHostCommandResult.Applied());
		public ValueTask<WorkspaceHostCommandResult> SetTitleAsync(string? t, CancellationToken c) => ValueTask.FromResult(WorkspaceHostCommandResult.Applied());
		public ValueTask<WorkspaceHostCommandResult> RequestLayoutNotificationAsync(CancellationToken c) => ValueTask.FromResult(WorkspaceHostCommandResult.Applied());
		public ValueTask<WorkspaceHostCommandResult> AppendToComposerAsync(string value, CancellationToken c) => ValueTask.FromResult(WorkspaceHostCommandResult.Applied());
		public ValueTask<WorkspaceHostCommandResult> RevealFileAsync(string path, CancellationToken c) => ValueTask.FromResult(WorkspaceHostCommandResult.Applied());
	}
}
