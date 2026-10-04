using System.Text;
using Cockpit.Features.Workspace;
using Cockpit.Features.Workspace.Storage;
using Shouldly;

namespace Cockpit.UnitTests.Features.Workspace;

public sealed class WorkspaceStorageTests
{
	[Fact]
	public async Task FileStore_CommitsAndLoadsACompleteGeneration()
	{
		string directory = CreateTestDirectory();
		try
		{
			FileWorkspaceGenerationStore store = new();
			await using IWorkspaceWriteLease lease = (await store.TryAcquireWriteLeaseAsync(directory, TestContext.Current.CancellationToken))!;
			byte[] content = Encoding.UTF8.GetBytes("checkpoint content");
			string contentId = await WorkspaceContentId.ComputeAsync(new MemoryStream(content), TestContext.Current.CancellationToken);
			WorkspaceState state = WorkspaceState.Empty("runtime", "storage", "root") with { Revision = 7, LayoutRevision = 4 };
			WorkspaceManifest manifest = WorkspaceManifest.FromState(state);

			await store.CommitAsync(
				lease,
				new WorkspaceGenerationCommit(
					"generation-1",
					manifest,
					[new WorkspaceObjectWrite(contentId, _ => ValueTask.FromResult<Stream>(new MemoryStream(content)))]),
				TestContext.Current.CancellationToken);

			WorkspaceLoadResult loaded = await store.LoadAsync(directory, TestContext.Current.CancellationToken);
			loaded.Status.ShouldBe(WorkspaceLoadStatus.Restored);
			loaded.GenerationId.ShouldBe("generation-1");
			loaded.Manifest!.Revision.ShouldBe(7);
			await using Stream savedContent = await store.OpenObjectReadAsync(directory, contentId, TestContext.Current.CancellationToken);
			using StreamReader reader = new(savedContent, Encoding.UTF8);
			(await reader.ReadToEndAsync(TestContext.Current.CancellationToken)).ShouldBe("checkpoint content");
		}
		finally
		{
			Directory.Delete(directory, recursive: true);
		}
	}

	[Fact]
	public async Task Coordinator_CapturesLatestStateAfterWaitingForSaveGate()
	{
		BlockingStore store = new();
		long revision = 1;
		WorkspacePersistenceCoordinator coordinator = (await WorkspacePersistenceCoordinator.TryCreateAsync(
			store,
			"memory",
			identityEpoch: 0,
			persistedRevision: 0,
			_ => ValueTask.FromResult(CreateSnapshot(revision)),
			TimeSpan.FromHours(1),
			TestContext.Current.CancellationToken))!;
		await using(coordinator)
		{
			Task<WorkspaceSaveResult> first = coordinator.FlushAsync(1, TestContext.Current.CancellationToken);
			await store.FirstCommitEntered.Task.WaitAsync(TestContext.Current.CancellationToken);
			revision = 2;
			Task<WorkspaceSaveResult> second = coordinator.FlushAsync(2, TestContext.Current.CancellationToken);
			store.ReleaseFirstCommit.TrySetResult();
			await first;
			WorkspaceSaveResult secondResult = await second;

			secondResult.Status.ShouldBe(WorkspaceSaveStatus.Saved);
			store.CommittedRevisions.ShouldBe([1L, 2L]);
			coordinator.PersistedRevision.ShouldBe(2);
		}
	}

	[Fact]
	public void StorageLocator_DoesNotEmbedRawSessionIdentityInFallbackPath()
	{
		WorkspaceStorageLocations locations = WorkspaceStorageLocator.ForSession(
			Path.GetTempPath(),
			"session/with/private-data",
			null);

		locations.Primary.ShouldNotContain("private-data");
		Path.GetFileName(locations.Primary).Length.ShouldBe(64);
		locations.Fallback.ShouldBeNull();
	}

	[Fact]
	public void CheckpointCoordinator_RejectsLateCompletionAfterInstanceClose()
	{
		WorkspaceCheckpointCoordinator coordinator = new();
		long generation = coordinator.BeginControllerGeneration("terminal-1");
		coordinator.RequestCapture("terminal-1", generation, 4).ShouldBeTrue();
		coordinator.CloseInstance("terminal-1");

		WorkspaceCheckpointAcceptStatus status = coordinator.TryAccept(
			"terminal-1",
			generation,
			new WorkspaceCheckpointReference(new string('a', 64), 1, 4));

		status.ShouldBe(WorkspaceCheckpointAcceptStatus.InstanceClosed);
		coordinator.Snapshot().ShouldBeEmpty();
	}

	static WorkspacePersistenceSnapshot CreateSnapshot(long revision)
	{
		WorkspaceState state = WorkspaceState.Empty("runtime", "storage", "root") with
		{
			Revision = revision,
			LayoutRevision = revision
		};
		return new WorkspacePersistenceSnapshot(
			WorkspaceManifest.FromState(state),
			revision,
			0,
			new Dictionary<string, long>(),
			[]);
	}

	static string CreateTestDirectory()
	{
		string path = Path.Combine(Path.GetTempPath(), "Cockpit.Workspace.Tests", Guid.NewGuid().ToString("N"));
		Directory.CreateDirectory(path);
		return path;
	}

	sealed class BlockingStore : IWorkspaceGenerationStore
	{
		int _commits;
		internal TaskCompletionSource FirstCommitEntered { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
		internal TaskCompletionSource ReleaseFirstCommit { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
		internal List<long> CommittedRevisions { get; } = [];

		public ValueTask<IWorkspaceWriteLease?> TryAcquireWriteLeaseAsync(string storageRoot, CancellationToken cancellationToken)
			=> ValueTask.FromResult<IWorkspaceWriteLease?>(new Lease(storageRoot));

		public Task<WorkspaceLoadResult> LoadAsync(string storageRoot, CancellationToken cancellationToken)
			=> Task.FromResult(new WorkspaceLoadResult(WorkspaceLoadStatus.NotFound));

		public async Task CommitAsync(IWorkspaceWriteLease lease, WorkspaceGenerationCommit generation, CancellationToken cancellationToken)
		{
			if(Interlocked.Increment(ref _commits) == 1)
			{
				FirstCommitEntered.TrySetResult();
				await ReleaseFirstCommit.Task.WaitAsync(cancellationToken);
			}
			CommittedRevisions.Add(generation.Manifest.Revision);
		}

		public Task<Stream> OpenObjectReadAsync(string storageRoot, string contentId, CancellationToken cancellationToken)
			=> throw new NotSupportedException();

		sealed class Lease(string storageRoot) : IWorkspaceWriteLease
		{
			public string StorageRoot { get; } = storageRoot;
			public ValueTask DisposeAsync() => ValueTask.CompletedTask;
		}
	}
}
