namespace Cockpit.Features.Telemetry.Models;

class TelemetryFileInfo
{
	public required string FileName { get; set; }
	public required string FullPath { get; set; }
	public required DateTime Date { get; set; }
	public long SizeBytes { get; set; }
}
