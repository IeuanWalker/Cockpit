using Cockpit.Features.Canvas;
using Cockpit.Features.Splash;

namespace Cockpit;

public partial class CanvasPage : SecondaryWindowPage
{
	readonly CanvasWindowManager _canvasWindowManager;

	public CanvasPage(string instanceId, WindowSplashFeature splashFeature, CanvasWindowManager canvasWindowManager)
	{
		InstanceId = instanceId;
		_canvasWindowManager = canvasWindowManager;
		InitializeComponent();
		InitializeSplash(splashOverlay, splashFeature);
	}

	/// <summary>
	/// The canvas instance ID used by <see cref="CanvasRoot"/> to look up its
	/// <see cref="CanvasInstanceModel"/> from <see cref="CanvasWindowManager"/>.
	/// </summary>
	public string InstanceId { get; }

	protected override void OnDisappearing()
	{
		base.OnDisappearing();
		// User closed the window via the OS chrome — clean up the instance so it doesn't leak.
		_ = _canvasWindowManager.CloseAsync(InstanceId, CancellationToken.None);
	}
}
