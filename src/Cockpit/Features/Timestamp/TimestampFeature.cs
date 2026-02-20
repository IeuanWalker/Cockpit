using System.Timers;

namespace Cockpit.Features.Timestamp;

public sealed partial class TimestampFeature : IDisposable
{
	readonly System.Timers.Timer _timer;
	public event Action? OnTick;

	public TimestampFeature()
	{
		_timer = new System.Timers.Timer(1000); // Update every 1 second
		_timer.Elapsed += TimerElapsed;
		_timer.AutoReset = true;
		_timer.Start();
	}

	void TimerElapsed(object? sender, ElapsedEventArgs e)
	{
		OnTick?.Invoke();
	}

	public void Dispose()
	{
		_timer?.Stop();
		_timer?.Dispose();
	}
}
