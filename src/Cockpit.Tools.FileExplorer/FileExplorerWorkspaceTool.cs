using Cockpit.Tooling;

namespace Cockpit.Tools.FileExplorer;

public sealed class FileExplorerWorkspaceTool : IWorkspaceTool
{
	public WorkspaceToolDescriptor Descriptor { get; } = new()
	{
		ToolId = "cockpit.file-explorer",
		ApiVersion = new(1, 0),
		DisplayName = "Files",
		Description = "Browse and preview files in this session.",
		Icon = "files",
		StateSchemaVersion = FileExplorerState.CurrentSchemaVersion,
		Content = new WorkspaceToolContentDescriptor.Razor(typeof(FileExplorerTool))
	};
	public ValueTask<IWorkspaceToolSession> CreateSessionAsync(WorkspaceToolSessionContext context, CancellationToken cancellationToken) =>
		ValueTask.FromResult<IWorkspaceToolSession>(new FileExplorerToolSession(context.Session));
	public ValueTask<ToolTemplateExportResult> ExportTemplateAsync(ToolStateReadContext source, ToolTemplateExportContext target, CancellationToken cancellationToken) =>
		ValueTask.FromResult(new ToolTemplateExportResult(ToolTemplateExportStatus.Prepared,
			new(source.SchemaVersion, source.MutationVersion, source.Metadata.Clone(), []), Included: ["location", "filter", "preview"]));
	public ValueTask<ToolStateCloneResult> CloneStateAsync(ToolStateReadContext source, ToolStateCloneContext target, CancellationToken cancellationToken) =>
		ValueTask.FromResult(new ToolStateCloneResult(ToolStateCloneStatus.Prepared, new(source.SchemaVersion, source.MutationVersion, source.Metadata.Clone(), [])));
}

sealed class FileExplorerToolSession(WorkspaceSessionInfo session) : IWorkspaceToolSession
{
	public bool IsAvailable { get; private set; } = session.WorkingDirectory is string path && Directory.Exists(path);
	public string? UnavailabilityReason => IsAvailable ? null : "The session has no accessible working directory.";
	public event Action? AvailabilityChanged;
	public ValueTask RefreshAvailabilityAsync(WorkspaceSessionInfo updated, CancellationToken cancellationToken)
	{
		bool next = updated.WorkingDirectory is string path && Directory.Exists(path);
		if(next != IsAvailable) { IsAvailable = next; AvailabilityChanged?.Invoke(); }
		return ValueTask.CompletedTask;
	}
	public ValueTask<IWorkspaceToolInstance> CreateInstanceAsync(WorkspaceToolInstanceContext context, CancellationToken cancellationToken) =>
		ValueTask.FromResult<IWorkspaceToolInstance>(new FileExplorerToolInstance(context));
	public ValueTask DisposeAsync() => ValueTask.CompletedTask;
}
