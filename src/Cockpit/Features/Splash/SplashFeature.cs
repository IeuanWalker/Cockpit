namespace Cockpit.Features.Splash;

public class SplashFeature
{
	public event Action? OnBlazorReady;

	public void NotifyBlazorReady() => OnBlazorReady?.Invoke();
}
