namespace Cockpit.Features.Splash;

public class WindowSplashFeature
{
	public event Action? OnBlazorReady;
	public void NotifyBlazorReady() => OnBlazorReady?.Invoke();
}
