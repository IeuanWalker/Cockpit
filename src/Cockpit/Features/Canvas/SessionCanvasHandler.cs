using GitHub.Copilot;
using GitHub.Copilot.Rpc;

namespace Cockpit.Features.Canvas;

/// <summary>
/// Per-session <see cref="ICanvasHandler"/> implementation.
/// Routes all canvas lifecycle calls to the singleton <see cref="CanvasWindowManager"/>.
/// </summary>
public sealed class SessionCanvasHandler : CanvasHandlerBase
{
	readonly CanvasWindowManager _windowManager;

	public SessionCanvasHandler(CanvasWindowManager windowManager)
	{
		_windowManager = windowManager;
	}

	public override Task<CanvasProviderOpenResult> OnOpenAsync(
		CanvasProviderOpenRequest context,
		CancellationToken cancellationToken)
		=> _windowManager.OpenAsync(context, cancellationToken);

	public override Task OnCloseAsync(
		CanvasProviderCloseRequest context,
		CancellationToken cancellationToken)
		=> _windowManager.CloseAsync(context.InstanceId, cancellationToken);

	public override Task<object?> OnActionAsync(
		CanvasProviderInvokeActionRequest context,
		CancellationToken cancellationToken)
		=> _windowManager.InvokeActionAsync(context, cancellationToken);
}
