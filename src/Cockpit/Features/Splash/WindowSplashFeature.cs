namespace Cockpit.Features.Splash;

/// <summary>
/// Base splash feature for MAUI windows. Provides fire-once notifications for Blazor
/// readiness, status text updates, and final splash dismissal.
/// Thread-safe: event handlers are cleared after first invocation.
/// </summary>
public abstract class WindowSplashFeature
{
	readonly Lock _lock = new();
	Action? _onBlazorReady;
	Action? _onSplashHide;

	public event Action? OnBlazorReady
	{
		add { lock(_lock) { _onBlazorReady += value; } }
		remove { lock(_lock) { _onBlazorReady -= value; } }
	}

	/// <summary>Raised when the splash should be dismissed.</summary>
	public event Action? OnSplashHide
	{
		add { lock(_lock) { _onSplashHide += value; } }
		remove { lock(_lock) { _onSplashHide -= value; } }
	}

	/// <summary>Raised with a status string to display in the splash overlay.</summary>
	public event Action<string>? OnStatusUpdate;

	/// <summary>
	/// Signals that Blazor has rendered. Fires <see cref="OnBlazorReady"/> handlers once
	/// then clears them. Does not hide the splash — call <see cref="NotifyReady"/> for that.
	/// </summary>
	public void NotifyBlazorReady()
	{
		Action? handler;
		lock(_lock)
		{
			handler = _onBlazorReady;
			_onBlazorReady = null;
		}

		handler?.Invoke();
	}

	/// <summary>Updates the status label shown in the splash overlay.</summary>
	public void UpdateStatus(string message) => OnStatusUpdate?.Invoke(message);

	/// <summary>
	/// Signals that startup is complete and the splash should be dismissed.
	/// Fires <see cref="OnSplashHide"/> handlers once then clears them.
	/// </summary>
	public void NotifyReady()
	{
		Action? handler;
		lock(_lock)
		{
			handler = _onSplashHide;
			_onSplashHide = null;
		}

		handler?.Invoke();
	}
}
