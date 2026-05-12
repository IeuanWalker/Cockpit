namespace Cockpit.Features.Telemetry.Models;

class TelemetrySummary
{
	public int TotalTraces { get; set; }
	public int TotalSpans { get; set; }
	public int ErrorCount { get; set; }
	public DateTime StartTime { get; set; }
	public DateTime EndTime { get; set; }
	public TimeSpan Duration => EndTime - StartTime;
}
