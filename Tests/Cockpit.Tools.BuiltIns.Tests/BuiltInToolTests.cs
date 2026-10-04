using Cockpit.Tooling;
using Cockpit.Tools.Browser;
using Cockpit.Tools.FileExplorer;
using Cockpit.Tools.Terminal;
using Shouldly;

namespace Cockpit.Tools.BuiltIns.Tests;

public sealed class BuiltInToolTests
{
	[Fact]
	public async Task Terminal_instances_have_independent_runtime_ids_and_only_explicit_close_stops_them()
	{
		CancellationToken ct = TestContext.Current.CancellationToken;
		FakeTerminalBackend backend = new();
		TerminalToolInstance first = new(Context("one"), backend);
		TerminalToolInstance second = new(Context("two"), backend);
		await first.RestoreAsync(null, ct);
		await second.RestoreAsync(null, ct);
		await first.StartAsync(ct);
		await second.StartAsync(ct);

		first.RuntimeId.ShouldNotBe(second.RuntimeId);
		await first.OnDeactivatedAsync(ct);
		backend.IsRunning(first.RuntimeId).ShouldBeTrue();
		(await first.CanCloseAsync(ToolCloseReason.UserClosedTab, ct)).Kind.ShouldBe(ToolCloseDecisionKind.Confirm);
		await first.DisposeAsync();
		backend.IsRunning(first.RuntimeId).ShouldBeFalse();
		backend.IsRunning(second.RuntimeId).ShouldBeTrue();
	}

	[Fact]
	public async Task Browser_restores_all_inner_tabs_and_navigation_history()
	{
		CancellationToken ct = TestContext.Current.CancellationToken;
		BrowserToolInstance source = new(new("browser-one"));
		await source.RestoreAsync(null, ct);
		source.Navigate("example.com");
		source.AddTab();
		source.Navigate("second.example.com");
		ToolCheckpoint checkpoint = await source.CaptureAsync(Writer("browser-one"), ct);
		BrowserToolInstance restored = new(new("browser-two"));
		await restored.RestoreAsync(Read(checkpoint, "browser-one"), ct);

		restored.ViewState.Tabs.Length.ShouldBe(2);
		restored.Active!.Url.ShouldContain("second.example.com");
	}

	[Fact]
	public async Task Terminal_measures_startup_and_drains_input_output_exactly_once()
	{
		CancellationToken ct = TestContext.Current.CancellationToken;
		FakeTerminalBackend backend = new();
		await using TerminalToolInstance terminal = new(Context("input"), backend);
		await terminal.RestoreAsync(null, ct);
		await terminal.Start(84, 26);
		backend.Launches[terminal.RuntimeId].Columns.ShouldBe(84);
		backend.Launches[terminal.RuntimeId].Rows.ShouldBe(26);
		await terminal.SendAsync("\u001b[A", ct);
		await terminal.SendAsync("\u0003", ct);
		terminal.TakeOutput().ShouldBe("\u001b[A\u0003");
		terminal.TakeOutput().ShouldBeEmpty();
		await terminal.Resize(40, 12);
		backend.Sizes[terminal.RuntimeId].ShouldBe((40, 12));
	}

	[Fact]
	public async Task Terminal_reset_discards_old_output_and_keeps_other_shells_running()
	{
		CancellationToken ct = TestContext.Current.CancellationToken;
		FakeTerminalBackend backend = new();
		await using TerminalToolInstance first = new(Context("reset"), backend);
		await using TerminalToolInstance second = new(Context("keep"), backend);
		await first.RestoreAsync(null, ct);
		await second.RestoreAsync(null, ct);
		await first.StartAsync(ct);
		await second.StartAsync(ct);
		await first.SendAsync("old output", ct);
		await first.Restart(70, 20);
		first.TakeOutput().ShouldBeEmpty();
		backend.IsRunning(first.RuntimeId).ShouldBeTrue();
		backend.IsRunning(second.RuntimeId).ShouldBeTrue();
		backend.Launches[first.RuntimeId].Columns.ShouldBe(70);
	}

	[Fact]
	public async Task File_explorer_instances_restore_their_own_location_filter_and_preview()
	{
		CancellationToken ct = TestContext.Current.CancellationToken;
		string root = Path.Combine(Path.GetTempPath(), $"cockpit-file-tool-{Guid.NewGuid():N}");
		Directory.CreateDirectory(root);
		try
		{
			string path = Path.Combine(root, "notes.txt");
			await File.WriteAllTextAsync(path, "remember me", ct);
			FileExplorerToolInstance source = new(Context("files-one", root));
			await source.RestoreAsync(null, ct);
			source.SetFilter("notes");
			await source.OpenAsync(source.Entries.Single());
			ToolCheckpoint checkpoint = await source.CaptureAsync(Writer("files-one"), ct);
			FileExplorerToolInstance restored = new(Context("files-two", root));
			await restored.RestoreAsync(Read(checkpoint, "files-one"), ct);

			restored.ViewState.Filter.ShouldBe("notes");
			restored.ViewState.SelectedPath.ShouldBe(path);
			restored.ViewState.PreviewText.ShouldBe("remember me");
		}
		finally { Directory.Delete(root, true); }
	}

	static WorkspaceToolInstanceContext Context(string id, string? directory = "C:\\") => new(
		new(id), new(new("session"), new("session"), 0, "test", directory, null, "Loaded", new HashSet<string>()),
		new FakeHost(), CancellationToken.None);
	static ToolStateWriteContext Writer(string id) => new(new(id), (_, _, _) => throw new NotSupportedException());
	static ToolStateReadContext Read(ToolCheckpoint checkpoint, string id) => new(checkpoint.SchemaVersion, checkpoint.MutationVersion, new(id), checkpoint.Metadata, checkpoint.Assets, (_, _) => throw new NotSupportedException());

	sealed class FakeTerminalBackend : ITerminalToolBackend
	{
		readonly HashSet<string> _running = [];
		public Dictionary<string, TerminalLaunch> Launches { get; } = [];
		public Dictionary<string, (int, int)> Sizes { get; } = [];
		public event Action<string, string>? OutputReceived;
		public bool IsSupported => true;
		public int? WindowsBuildNumber => null;
		public Task ResizeAsync(string runtimeId, int columns, int rows, CancellationToken cancellationToken) { Sizes[runtimeId] = (columns, rows); return Task.CompletedTask; }
		public bool IsRunning(string runtimeId) => _running.Contains(runtimeId);
		public string GetBufferedOutput(string runtimeId) => string.Empty;
		public Task<bool> EnsureStartedAsync(string runtimeId, TerminalLaunch launch, CancellationToken cancellationToken) { _running.Add(runtimeId); Launches[runtimeId] = launch; return Task.FromResult(true); }
		public Task<bool> WriteAsync(string runtimeId, string input, CancellationToken cancellationToken) { OutputReceived?.Invoke(runtimeId, input); return Task.FromResult(_running.Contains(runtimeId)); }
		public Task CloseAsync(string runtimeId) { _running.Remove(runtimeId); return Task.CompletedTask; }
	}

	sealed class FakeHost : IWorkspaceToolHost
	{
		static ValueTask<WorkspaceHostCommandResult> Applied() => ValueTask.FromResult(WorkspaceHostCommandResult.Applied());
		public ValueTask<WorkspaceHostCommandResult> ActivateSelfAsync(CancellationToken cancellationToken) => Applied();
		public ValueTask<WorkspaceHostCommandResult> CloseSelfAsync(CancellationToken cancellationToken) => Applied();
		public ValueTask<WorkspaceHostCommandResult> OpenNewToolAsync(string toolId, CancellationToken cancellationToken) => Applied();
		public ValueTask<WorkspaceHostCommandResult> SetTitleAsync(string? title, CancellationToken cancellationToken) => Applied();
		public ValueTask<WorkspaceHostCommandResult> RequestLayoutNotificationAsync(CancellationToken cancellationToken) => Applied();
		public ValueTask<WorkspaceHostCommandResult> AppendToComposerAsync(string content, CancellationToken cancellationToken) => Applied();
		public ValueTask<WorkspaceHostCommandResult> RevealFileAsync(string path, CancellationToken cancellationToken) => Applied();
	}
}
