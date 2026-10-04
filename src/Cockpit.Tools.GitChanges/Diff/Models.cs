namespace Cockpit.Tools.GitChanges.Diff;

public enum DiffLineType { Context, Added, Removed }

public sealed record DiffLine(DiffLineType Type, string Content, int? OldLineNumber, int? NewLineNumber);

public sealed record DiffHunk(string Header, IReadOnlyList<DiffLine> Lines, int OldStartLine, int NewStartLine);

public sealed record ParsedDiff(string OldPath, string NewPath, IReadOnlyList<DiffHunk> Hunks, int LinesAdded, int LinesRemoved);

public sealed record SplitDiffRow(
	DiffLine? Left,
	DiffLine? Right,
	IReadOnlyList<(int Start, int Length)>? LeftSpans = null,
	IReadOnlyList<(int Start, int Length)>? RightSpans = null);
