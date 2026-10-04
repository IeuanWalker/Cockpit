using Microsoft.Extensions.DependencyInjection;

namespace Cockpit.Tooling;

public interface IWorkspaceTool
{
	WorkspaceToolDescriptor Descriptor { get; }

	ValueTask<IWorkspaceToolSession> CreateSessionAsync(
		WorkspaceToolSessionContext context,
		CancellationToken cancellationToken);

	ValueTask<ToolTemplateExportResult> ExportTemplateAsync(
		ToolStateReadContext source,
		ToolTemplateExportContext target,
		CancellationToken cancellationToken);

	ValueTask<ToolStateCloneResult> CloneStateAsync(
		ToolStateReadContext source,
		ToolStateCloneContext target,
		CancellationToken cancellationToken);
}

public interface IWorkspaceToolSession : IAsyncDisposable
{
	bool IsAvailable { get; }
	string? UnavailabilityReason { get; }
	event Action? AvailabilityChanged;

	ValueTask RefreshAvailabilityAsync(
		WorkspaceSessionInfo session,
		CancellationToken cancellationToken);

	ValueTask<IWorkspaceToolInstance> CreateInstanceAsync(
		WorkspaceToolInstanceContext context,
		CancellationToken cancellationToken);
}

public interface IWorkspaceToolInstance : IAsyncDisposable
{
	WorkspaceToolInstanceId InstanceId { get; }
	event Action<ToolStateChanged>? StateChanged;

	ValueTask<ToolRestoreResult> RestoreAsync(
		ToolStateReadContext? savedState,
		CancellationToken cancellationToken);

	ValueTask<ToolCheckpoint> CaptureAsync(
		ToolStateWriteContext destination,
		CancellationToken cancellationToken);

	ValueTask OnActivatedAsync(
		WorkspaceToolActivationContext context,
		CancellationToken cancellationToken);

	ValueTask OnDeactivatedAsync(CancellationToken cancellationToken);

	ValueTask OnLayoutChangedAsync(
		WorkspaceToolLayoutContext context,
		CancellationToken cancellationToken);

	ValueTask<ToolCloseDecision> CanCloseAsync(
		ToolCloseReason reason,
		CancellationToken cancellationToken);
}

public interface IWorkspaceToolModule
{
	void ConfigureServices(IServiceCollection services);
}

public sealed record WorkspaceToolSessionContext(
	WorkspaceSessionInfo Session,
	IReadOnlyDictionary<string, object?> Properties);

public sealed record WorkspaceToolInstanceContext(
	WorkspaceToolInstanceId InstanceId,
	WorkspaceSessionInfo Session,
	IWorkspaceToolHost Host,
	CancellationToken LifetimeToken);

/// <summary>The single host-supplied parameter for a Razor tool component.</summary>
public sealed record WorkspaceToolComponentContext(
	IWorkspaceToolInstance Instance,
	WorkspaceToolInstanceContext InstanceContext);

public sealed record WorkspaceToolActivationContext(
	WorkspaceViewGeneration Generation,
	double Width,
	double Height,
	bool UserFocusRequested,
	CancellationToken VisibilityToken);

public sealed record WorkspaceToolLayoutContext(
	WorkspaceViewGeneration Generation,
	double Width,
	double Height,
	bool IsVisible,
	bool IsMaximised);
