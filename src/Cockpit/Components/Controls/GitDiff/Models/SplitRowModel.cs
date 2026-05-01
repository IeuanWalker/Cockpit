namespace Cockpit.Components.Controls.GitDiff.Models;

public sealed class SplitRowModel
{
	public required DiffLineModel? Left { get; init; }
	public required DiffLineModel? Right { get; init; }
	public List<(int Start, int Length)>? LeftSpans { get; init; }
	public List<(int Start, int Length)>? RightSpans { get; init; }
}