using System.Timers;

namespace CopilotGUI.Services;

public sealed class TimestampService : IDisposable
{
	readonly System.Timers.Timer _timer;
	public event Action? OnTick;

	public TimestampService()
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
