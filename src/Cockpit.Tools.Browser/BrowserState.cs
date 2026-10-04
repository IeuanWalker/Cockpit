using System.Collections.Immutable;
using System.Text.Json.Serialization;

namespace Cockpit.Tools.Browser;

public sealed record BrowserPageState
{
	public required string Id { get; init; }
	public required string Url { get; init; }
	public string? Title { get; init; }
	public ImmutableArray<string> BackHistory { get; init; } = [];
	public ImmutableArray<string> ForwardHistory { get; init; } = [];
}

public sealed record BrowserState
{
	public const int CurrentSchemaVersion = 1;
	public ImmutableArray<BrowserPageState> Tabs { get; init; } = [];
	public string? ActiveTabId { get; init; }
}

[JsonSerializable(typeof(BrowserState))]
public partial class BrowserJsonContext : JsonSerializerContext;
