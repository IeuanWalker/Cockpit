namespace Cockpit.Components.Controls.GitDiff.Models;

public sealed class SplitRowModel
{
	public required DiffLineModel? Left { get; init; }
	public required DiffLineModel? Right { get; init; }
}