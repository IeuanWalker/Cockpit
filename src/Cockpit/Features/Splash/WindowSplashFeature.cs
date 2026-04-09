namespace Cockpit.Features.Splash;

public class WindowSplashFeature
{
	public event Action? OnBlazorReady;

	public void NotifyBlazorReady()
	{
		Action? handler = OnBlazorReady;
		OnBlazorReady = null;
		handler?.Invoke();
	}
}
