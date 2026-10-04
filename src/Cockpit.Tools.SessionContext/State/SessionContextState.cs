using System.Collections.Immutable;
using Cockpit.Tools.SessionContext.Sources;

namespace Cockpit.Tools.SessionContext.State;

public sealed record SessionContextState
{
	public const int CurrentSchemaVersion = 2;

	public int SchemaVersion { get; init; } = CurrentSchemaVersion;
	public ImmutableDictionary<string, bool> SectionVisibility { get; init; }
		= ImmutableDictionary<string, bool>.Empty.WithComparers(StringComparer.Ordinal);
	public ImmutableDictionary<string, bool> SectionExpansion { get; init; }
		= ImmutableDictionary<string, bool>.Empty.WithComparers(StringComparer.Ordinal);
	public ImmutableArray<string> SectionOrder { get; init; } = [];
	public string? FilterText { get; init; }
	public SessionContextSelection? SelectedItem { get; init; }
	public SessionContextScrollAnchor? ScrollAnchor { get; init; }
	public CachedSessionContext? CachedContext { get; init; }
}

public sealed record SessionContextSelection(string SectionId, string ItemKey);

public sealed record SessionContextScrollAnchor(string SectionId, string? ItemKey, double Offset);

public sealed record CachedSessionContext
{
	public required SessionContextSnapshot Snapshot { get; init; }
	public required string SourceIdentity { get; init; }
	public required DateTimeOffset CachedAt { get; init; }
}
