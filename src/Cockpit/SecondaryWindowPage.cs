using Cockpit.Features.Splash;

namespace Cockpit;

public abstract class SecondaryWindowPage : ContentPage
{
	int _splashHidden;

	protected void InitializeSplash(Grid splashOverlay, WindowSplashFeature splashFeature)
	{
		splashFeature.OnBlazorReady += () => Dispatcher.Dispatch(() => _ = HideSplashAsync(splashOverlay));

		Dispatcher.StartTimer(TimeSpan.FromSeconds(15), () =>
		{
			_ = HideSplashAsync(splashOverlay);
			return false;
		});
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
