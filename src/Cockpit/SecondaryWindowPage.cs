using Cockpit.Features.Splash;

namespace Cockpit;

public abstract class SecondaryWindowPage : ContentPage
{
	int _splashHidden;
	Action? _blazorReadyHandler;
	WindowSplashFeature? _splashFeature;

	protected void InitializeSplash(Grid splashOverlay, WindowSplashFeature splashFeature)
	{
		_splashFeature = splashFeature;
		_blazorReadyHandler = () => Dispatcher.Dispatch(() => _ = HideSplashAsync(splashOverlay));
		splashFeature.OnBlazorReady += _blazorReadyHandler;

		Dispatcher.StartTimer(TimeSpan.FromSeconds(15), () =>
		{
			_ = HideSplashAsync(splashOverlay);
			return false;
		});
	}

	protected override void OnDisappearing()
	{
		base.OnDisappearing();
		if(_splashFeature is not null && _blazorReadyHandler is not null)
		{
			_splashFeature.OnBlazorReady -= _blazorReadyHandler;
			_blazorReadyHandler = null;
			_splashFeature = null;
		}
	}

	async Task HideSplashAsync(Grid splashOverlay)
	{
		if(Interlocked.Exchange(ref _splashHidden, 1) != 0)
		{
			return;
		}

		await splashOverlay.FadeToAsync(0, 400, Easing.CubicOut);
		splashOverlay.IsVisible = false;
		((Grid)Content).Children.Remove(splashOverlay);
	}
}
