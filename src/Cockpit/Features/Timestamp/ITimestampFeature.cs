namespace Cockpit.Features.Timestamp;

public interface ITimestampFeature
{
	event Action? OnTick;

	/// <summary>
	/// Formats a UTC datetime as a human-readable relative string.
	/// Rules (by elapsed time from now):
	///   &lt; 60 s       → "just now"
	///   &lt; 60 min     → "X minute(s) ago"
	///   &lt; 24 h       → "X hour(s) ago"
	///   &lt; 7 days     → "X day(s) ago"
	///   same year    → "MMM d"  (e.g. "Jan 5")
	///   older        → "MMM d, yyyy"
	/// </summary>
	string FormatRelative(DateTime utcTime);

	/// <summary>
	/// Formats the duration between <paramref name="start"/> and <paramref name="end"/>
	/// (or the current local time when <paramref name="end"/> is <see langword="null"/>)
	/// as a compact string: "&lt;1s", "1.5s", "2.3m", "1.2h".
	/// </summary>
	string FormatDuration(DateTime start, DateTime? end);
}
