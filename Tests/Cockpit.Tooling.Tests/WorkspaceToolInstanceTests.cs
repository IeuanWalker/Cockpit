using System.Text.Json;
using System.Text.Json.Serialization;

namespace Cockpit.Tooling.Tests;

public sealed class WorkspaceToolInstanceTests
{
	[Fact]
	public async Task Typed_instance_round_trips_content_and_continues_mutation_version()
	{
		NotesInstance instance = new(new("notes-1"));
		JsonElement metadata = JsonSerializer.SerializeToElement(
			new NotesState("saved content"),
			NotesJsonContext.Default.NotesState);
		ToolStateReadContext source = new(1, 41, new("notes-1"), metadata, [], ThrowAsset);

		ToolRestoreResult restored = await instance.RestoreAsync(source, TestContext.Current.CancellationToken);
		instance.Change("updated content");
		ToolCheckpoint checkpoint = await instance.CaptureAsync(
			new(new("notes-1"), (_, _, _) => throw new InvalidOperationException()),
			TestContext.Current.CancellationToken);

		Assert.Equal(ToolRestoreStatus.Restored, restored.Status);
		Assert.Equal(42, checkpoint.MutationVersion);
		Assert.Equal("updated content", checkpoint.Metadata.GetProperty("Text").GetString());
	}

	[Fact]
	public async Task New_instance_restores_default_without_starting_runtime_work()
	{
		NotesInstance instance = new(new("notes-1"));

		ToolRestoreResult restored = await instance.RestoreAsync(null, TestContext.Current.CancellationToken);
		ToolCheckpoint checkpoint = await instance.CaptureAsync(
			new(new("notes-1"), (_, _, _) => throw new InvalidOperationException()),
			TestContext.Current.CancellationToken);

		Assert.Equal(ToolRestoreStatus.Restored, restored.Status);
		Assert.Equal("new", checkpoint.Metadata.GetProperty("Text").GetString());
		Assert.Equal(0, checkpoint.MutationVersion);
	}

	static ValueTask<Stream> ThrowAsset(ToolAssetReference _, CancellationToken __) =>
		throw new InvalidOperationException();
}

public sealed record NotesState(string Text);

[JsonSerializable(typeof(NotesState))]
sealed partial class NotesJsonContext : JsonSerializerContext;

sealed class NotesInstance(WorkspaceToolInstanceId id)
	: WorkspaceToolInstance<NotesState>(id, NotesJsonContext.Default.NotesState)
{
	protected override int SchemaVersion => 1;
	protected override NotesState CreateDefaultState() => new("new");
	public void Change(string text) => SetState(new(text));
}
