using System.Text.Json.Serialization;

namespace Cockpit.Tools.Terminal;

public sealed record TerminalState
{
	public const int CurrentSchemaVersion = 1;
	public string? WorkingDirectory { get; init; }
	public string? Title { get; init; }
	public string Transcript { get; init; } = string.Empty;
	public bool WasRunning { get; init; }
	public int Columns { get; init; } = 120;
	public int Rows { get; init; } = 30;
}

[JsonSerializable(typeof(TerminalState))]
public partial class TerminalJsonContext : JsonSerializerContext;
