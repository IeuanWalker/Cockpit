namespace Cockpit.Features.Telemetry.Models;

class TelemetrySpan
{
	public required string TraceId { get; set; }
	public required string SpanId { get; set; }
	public string? ParentSpanId { get; set; }
	public required string Name { get; set; }
	public SpanKindEnum Kind { get; set; }
	public DateTime StartTime { get; set; }
	public DateTime EndTime { get; set; }
	public TimeSpan Duration => EndTime - StartTime;
	public SpanStatusEnum Status { get; set; }
	public string? StatusMessage { get; set; }
	public Dictionary<string, string> Attributes { get; set; } = [];
	public List<TelemetrySpanEvent> Events { get; set; } = [];
}

class TelemetrySpanEvent
{
	public required string Name { get; set; }
	public DateTime Timestamp { get; set; }
	public Dictionary<string, string> Attributes { get; set; } = [];
}

enum SpanKindEnum
{
	Internal,
	Server,
	Client,
	Producer,
	Consumer
}

enum SpanStatusEnum
{
	Unset,
	Ok,
	Error
}
