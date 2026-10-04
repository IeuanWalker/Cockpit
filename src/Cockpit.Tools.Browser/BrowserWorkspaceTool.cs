using Cockpit.Tooling;

namespace Cockpit.Tools.Browser;

public sealed class BrowserWorkspaceTool : IWorkspaceTool
{
	public WorkspaceToolDescriptor Descriptor { get; } = new()
	{
		ToolId = "cockpit.browser",
		ApiVersion = new(1, 0),
		DisplayName = "Browser",
		Description = "A general embedded browser with its own tabs and navigation.",
		Icon = "browser",
		StateSchemaVersion = BrowserState.CurrentSchemaVersion,
		Content = new WorkspaceToolContentDescriptor.Razor(typeof(BrowserTool))
	};
	public ValueTask<IWorkspaceToolSession> CreateSessionAsync(WorkspaceToolSessionContext context, CancellationToken cancellationToken) =>
		ValueTask.FromResult<IWorkspaceToolSession>(new BrowserToolSession());
	public ValueTask<ToolTemplateExportResult> ExportTemplateAsync(ToolStateReadContext source, ToolTemplateExportContext target, CancellationToken cancellationToken) =>
		ValueTask.FromResult(new ToolTemplateExportResult(ToolTemplateExportStatus.Prepared, new(source.SchemaVersion, source.MutationVersion, source.Metadata.Clone(), []), Included: ["browser tabs", "URLs", "navigation history"]));
	public ValueTask<ToolStateCloneResult> CloneStateAsync(ToolStateReadContext source, ToolStateCloneContext target, CancellationToken cancellationToken) =>
		ValueTask.FromResult(new ToolStateCloneResult(ToolStateCloneStatus.Prepared, new(source.SchemaVersion, source.MutationVersion, source.Metadata.Clone(), [])));
}

sealed class BrowserToolSession : IWorkspaceToolSession
{
	public bool IsAvailable => true;
	public string? UnavailabilityReason => null;
	public event Action? AvailabilityChanged { add { } remove { } }
	public ValueTask RefreshAvailabilityAsync(WorkspaceSessionInfo session, CancellationToken cancellationToken) => ValueTask.CompletedTask;
	public ValueTask<IWorkspaceToolInstance> CreateInstanceAsync(WorkspaceToolInstanceContext context, CancellationToken cancellationToken) =>
		ValueTask.FromResult<IWorkspaceToolInstance>(new BrowserToolInstance(context.InstanceId));
	public ValueTask DisposeAsync() => ValueTask.CompletedTask;
}
