using System.Timers;

namespace Cockpit.Features.Timestamp;

sealed class TimestampFeature : ITimestampFeature
{
	readonly System.Timers.Timer _timer;
	readonly TimeProvider _timeProvider;

	public event Action? OnTick;

	public TimestampFeature(TimeProvider timeProvider)
	{
		_timeProvider = timeProvider;
		_timer = new System.Timers.Timer(1000);
		_timer.Elapsed += TimerElapsed;
		_timer.AutoReset = true;
		_timer.Start();
	}

	void TimerElapsed(object? sender, ElapsedEventArgs e)
	{
		OnTick?.Invoke();
	}

	public string FormatRelative(DateTime utcTime)
	{
		DateTimeOffset now = _timeProvider.GetUtcNow();
		TimeSpan elapsed = now - utcTime;

		if (elapsed < TimeSpan.Zero)
		{
			return "just now";
		}

		if (elapsed.TotalSeconds < 60)
		{
			return "just now";
		}

		if (elapsed.TotalMinutes < 60)
		{
			int minutes = (int)elapsed.TotalMinutes;
			return minutes == 1 ? "1 minute ago" : $"{minutes} minutes ago";
		}

		if (elapsed.TotalHours < 24)
		{
			int hours = (int)elapsed.TotalHours;
			return hours == 1 ? "1 hour ago" : $"{hours} hours ago";
		}

		int days = (int)elapsed.TotalDays;
		if (days < 7)
		{
			return days == 1 ? "1 day ago" : $"{days} days ago";
		}

		DateTime localNow = _timeProvider.GetLocalNow().DateTime;
		DateTime localTime = utcTime.ToLocalTime();

		return localTime.Year == localNow.Year
			? localTime.ToString("MMM d")
			: localTime.ToString("MMM d, yyyy");
	}

	public string FormatDuration(DateTime start, DateTime? end)
	{
		DateTime endTime = end ?? _timeProvider.GetLocalNow().DateTime;
		TimeSpan duration = endTime - start;

		if (duration.TotalSeconds < 1)
		{
			return "<1s";
		}

		if (duration.TotalSeconds < 60)
		{
			return $"{duration.TotalSeconds:F1}s";
		}

		if (duration.TotalMinutes < 60)
		{
			return $"{duration.TotalMinutes:F1}m";
		}

		return $"{duration.TotalHours:F1}h";
	}

}
