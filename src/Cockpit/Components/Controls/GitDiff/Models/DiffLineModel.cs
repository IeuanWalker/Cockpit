namespace Cockpit.Components.Controls.GitDiff.Models;

public sealed class DiffLineModel
{
	public required DiffLineTypeEnum Type { get; init; }
	public required string Content { get; init; }
	public required int? OldLineNumber { get; init; }
	public required int? NewLineNumber { get; init; }
}