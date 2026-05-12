namespace Cockpit.Features.Telemetry.Models;

class TelemetryTrace
{
	public required string TraceId { get; set; }
	public List<TelemetrySpan> Spans { get; set; } = [];
	public DateTime StartTime => Spans.Count > 0 ? Spans.Min(s => s.StartTime) : DateTime.MinValue;
	public DateTime EndTime => Spans.Count > 0 ? Spans.Max(s => s.EndTime) : DateTime.MinValue;
	public TimeSpan Duration => EndTime - StartTime;
	public string RootSpanName => Spans.FirstOrDefault(s => s.ParentSpanId is null)?.Name ?? Spans.FirstOrDefault()?.Name ?? "Unknown";
	public int SpanCount => Spans.Count;
	public bool HasErrors => Spans.Any(s => s.Status == SpanStatusEnum.Error);
}
