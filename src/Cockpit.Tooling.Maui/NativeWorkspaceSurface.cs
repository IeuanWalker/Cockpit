namespace Cockpit.Tooling.Maui;

/// <summary>Creates one native surface for one logical tool view generation.</summary>
public interface INativeWorkspaceSurfaceFactory
{
	ValueTask<INativeWorkspaceSurface> CreateAsync(
		NativeWorkspaceSurfaceContext context,
		CancellationToken cancellationToken);
}

/// <summary>A native view owned by a tool but positioned and occluded by the host.</summary>
public interface INativeWorkspaceSurface : IAsyncDisposable
{
	IView View { get; }

	ValueTask SetPlacementAsync(
		NativeWorkspaceSurfacePlacement placement,
		CancellationToken cancellationToken);

	ValueTask SetVisibilityAsync(bool isVisible, CancellationToken cancellationToken);
	ValueTask RequestFocusAsync(CancellationToken cancellationToken);
}

public sealed record NativeWorkspaceSurfaceContext(
	WorkspaceToolInstanceId InstanceId,
	RuntimeSessionKey RuntimeSessionKey,
	WorkspaceViewGeneration Generation,
	CancellationToken LifetimeToken);

public sealed record NativeWorkspaceSurfacePlacement(
	double X,
	double Y,
	double Width,
	double Height,
	double Scale,
	IReadOnlyList<NativeWorkspaceOcclusion> Occlusions);

public sealed record NativeWorkspaceOcclusion(
	double X,
	double Y,
	double Width,
	double Height);
