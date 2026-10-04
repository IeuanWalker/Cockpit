using System.Collections.Immutable;

namespace Cockpit.Tools.SessionContext.Sources;

public sealed record SessionContextSnapshot
{
	public required string RuntimeSessionKey { get; init; }
	public required long IdentityEpoch { get; init; }
	public required long Generation { get; init; }
	public required DateTimeOffset CapturedAt { get; init; }
	public string? CurrentWorkingDirectory { get; init; }
	public string? WorkspacePath { get; init; }
	public string? Repository { get; init; }
	public string? Branch { get; init; }
	public bool CanOpenInVsCode { get; init; }
	public bool IsCompacting { get; init; }
	public bool CanCompact { get; init; }
	public TokenUsageSnapshot? TokenUsage { get; init; }
	public ImmutableArray<SessionContextSectionSnapshot> Sections { get; init; } = [];
}

public sealed record SessionContextSectionSnapshot
{
	public required string SectionId { get; init; }
	public required string DisplayName { get; init; }
	public ImmutableArray<SessionContextItemSnapshot> Items { get; init; } = [];
	public bool CanRefresh { get; init; }
	public string? Error { get; init; }
}

public sealed record SessionContextItemSnapshot
{
	public required string Key { get; init; }
	public required string Label { get; init; }
	public string? Detail { get; init; }
	public string? Kind { get; init; }
	public string? Path { get; init; }
	public string? Content { get; init; }
	public string? Status { get; init; }
	public string? Badge { get; init; }
	public bool? Enabled { get; init; }
	public bool IsSelected { get; init; }
	public bool CanToggle { get; init; }
	public ImmutableDictionary<string, string> Properties { get; init; }
		= ImmutableDictionary<string, string>.Empty.WithComparers(StringComparer.Ordinal);
}

public sealed record TokenUsageSnapshot(
	double UsedTokens,
	double MaximumTokens,
	double? Percentage,
	double? SystemTokens = null,
	double? ConversationTokens = null,
	double? ToolDefinitionsTokens = null,
	double MessagesLength = 0);

public enum SessionContextCommand
{
	OpenPath,
	OpenInVsCode,
	RevealItem,
	ToggleItem,
	RefreshSection,
	RemoveSessionCommand,
	OpenCommandSettings,
	CompactContext
}

public sealed record SessionContextCommandRequest(
	SessionContextCommand Command,
	string? SectionId = null,
	string? ItemKey = null,
	string? Argument = null);

public sealed record SessionContextCommandResult(bool Applied, string? Error = null)
{
	public static SessionContextCommandResult Success() => new(true);
	public static SessionContextCommandResult Failure(string error) => new(false, error);
}

public sealed record SessionContextSourceChange(
	string RuntimeSessionKey,
	long IdentityEpoch,
	long Generation);
