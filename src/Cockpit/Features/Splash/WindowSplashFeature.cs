namespace Cockpit.Features.Splash;

/// <summary>
/// Base splash feature for MAUI windows. Implements a fire-once notification:
/// <see cref="NotifyBlazorReady"/> invokes all subscribers in registration order, then clears them.
/// Subsequent calls after the first are no-ops unless new handlers are registered.
/// Thread-safe: concurrent calls to <see cref="NotifyBlazorReady"/> will only fire handlers once.
/// </summary>
public abstract class WindowSplashFeature
{
	readonly object _lock = new();
	Action? _onBlazorReady;

	public event Action? OnBlazorReady
	{
		add { lock (_lock) { _onBlazorReady += value; } }
		remove { lock (_lock) { _onBlazorReady -= value; } }
	}

	/// <summary>
	/// Invokes all registered <see cref="OnBlazorReady"/> handlers in registration order, then clears them.
	/// Safe to call multiple times; subsequent calls with no subscribers are no-ops.
	/// If a handler throws, remaining handlers in the same invocation list are not called (standard multicast delegate behaviour).
	/// </summary>
	public void NotifyBlazorReady()
	{
		Action? handler;
		lock (_lock)
		{
			handler = _onBlazorReady;
			_onBlazorReady = null;
		}

		handler?.Invoke();
	}
}
