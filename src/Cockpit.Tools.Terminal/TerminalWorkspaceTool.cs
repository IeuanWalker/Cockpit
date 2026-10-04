using System.Text.Json;
using Cockpit.Tooling;

namespace Cockpit.Tools.Terminal;

public sealed class TerminalWorkspaceTool(ITerminalToolBackend backend) : IWorkspaceTool
{
	public WorkspaceToolDescriptor Descriptor { get; } = new()
	{
		ToolId = "cockpit.terminal",
		ApiVersion = new(1, 0),
		DisplayName = "Terminal",
		Description = "An independent shell for this workspace tab.",
		Icon = "terminal",
		StateSchemaVersion = TerminalState.CurrentSchemaVersion,
		Content = new WorkspaceToolContentDescriptor.Razor(typeof(TerminalTool))
	};

	public ValueTask<IWorkspaceToolSession> CreateSessionAsync(WorkspaceToolSessionContext context, CancellationToken cancellationToken) =>
		ValueTask.FromResult<IWorkspaceToolSession>(new TerminalToolSession(context.Session, backend));

	public ValueTask<ToolTemplateExportResult> ExportTemplateAsync(ToolStateReadContext source, ToolTemplateExportContext target, CancellationToken cancellationToken)
	{
		TerminalState state = source.Metadata.Deserialize(TerminalJsonContext.Default.TerminalState) ?? new();
		TerminalState template = state with { WasRunning = false };
		return ValueTask.FromResult(new ToolTemplateExportResult(ToolTemplateExportStatus.Prepared,
			new(TerminalState.CurrentSchemaVersion, source.MutationVersion, JsonSerializer.SerializeToElement(template, TerminalJsonContext.Default.TerminalState), []),
			Included: ["shell directory", "title", "terminal contents", "dimensions"], Excluded: ["running process"]));
	}

	public ValueTask<ToolStateCloneResult> CloneStateAsync(ToolStateReadContext source, ToolStateCloneContext target, CancellationToken cancellationToken) =>
		ValueTask.FromResult(new ToolStateCloneResult(ToolStateCloneStatus.Prepared,
			new(source.SchemaVersion, source.MutationVersion, source.Metadata.Clone(), source.Assets, ToolRestorationFidelity.Exact)));
}

sealed class TerminalToolSession(WorkspaceSessionInfo session, ITerminalToolBackend backend) : IWorkspaceToolSession
{
	public bool IsAvailable { get; private set; } = backend.IsSupported && !string.IsNullOrWhiteSpace(session.WorkingDirectory);
	public string? UnavailabilityReason => IsAvailable ? null : "No supported local shell or working directory is available.";
	public event Action? AvailabilityChanged;
	public ValueTask RefreshAvailabilityAsync(WorkspaceSessionInfo updated, CancellationToken cancellationToken)
	{
		bool next = backend.IsSupported && !string.IsNullOrWhiteSpace(updated.WorkingDirectory);
		if(next != IsAvailable) { IsAvailable = next; AvailabilityChanged?.Invoke(); }
		return ValueTask.CompletedTask;
	}
	public ValueTask<IWorkspaceToolInstance> CreateInstanceAsync(WorkspaceToolInstanceContext context, CancellationToken cancellationToken) =>
		ValueTask.FromResult<IWorkspaceToolInstance>(new TerminalToolInstance(context, backend));
	public ValueTask DisposeAsync() => ValueTask.CompletedTask;
}
