using System.Text.Json;
using Cockpit.Features.Splash;

namespace Cockpit.Features.Canvas;

/// <summary>
/// Tracks a single open canvas instance for a session.
/// Created when the SDK issues a <c>canvas.open</c> request and
/// removed when the window is closed or <c>canvas.close</c> is received.
/// </summary>
public sealed class CanvasInstanceModel
{
	public required string InstanceId { get; init; }
	public required string CanvasId { get; init; }
	public required string SessionId { get; init; }

	/// <summary>Title shown in the window chrome, supplied by the SDK request.</summary>
	public string? Title { get; set; }

	/// <summary>Status text shown beneath the title in the canvas window.</summary>
	public string? Status { get; set; }

	/// <summary>The agent-supplied input payload for this canvas instance.</summary>
	public JsonElement? Input { get; set; }

	/// <summary>
	/// Per-instance splash feature. A new instance is created for each canvas window so
	/// that multiple concurrent canvas windows don't share a single singleton and
	/// accidentally dismiss each other's splash screens.
	/// </summary>
	public CanvasSplashFeature SplashFeature { get; } = new();

	/// <summary>
	/// Callback registered by the Blazor canvas component to handle
	/// <c>canvas.action.invoke</c> requests. Returns a serialisable result
	/// or <see langword="null"/>.
	/// </summary>
	public Func<string, JsonElement?, CancellationToken, Task<object?>>? ActionCallback { get; set; }

	/// <summary>
	/// Reference to the MAUI <see cref="Window"/> that hosts this canvas,
	/// used to close it programmatically.
	/// </summary>
	public Window? Window { get; set; }
}
