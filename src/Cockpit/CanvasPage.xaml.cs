using Cockpit.Components;
using Cockpit.Features.Canvas;
using Cockpit.Features.Splash;
using Microsoft.AspNetCore.Components.WebView.Maui;

namespace Cockpit;

public partial class CanvasPage : SecondaryWindowPage
{
	readonly CanvasWindowManager _canvasWindowManager;

	public CanvasPage(string instanceId, WindowSplashFeature splashFeature, CanvasWindowManager canvasWindowManager)
	{
		InstanceId = instanceId;
		_canvasWindowManager = canvasWindowManager;
		InitializeComponent();

		blazorWebView.RootComponents.Add(new RootComponent
		{
			Selector = "#app",
			ComponentType = typeof(CanvasRoot),
			Parameters = new Dictionary<string, object?>
			{
				{ nameof(CanvasRoot.InstanceId), instanceId }
			}
		});

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
