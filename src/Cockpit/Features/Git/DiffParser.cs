using System.Text.RegularExpressions;

namespace Cockpit.Features.Git;

public static class DiffParser
{
	static readonly Regex hunkHeaderRegex = new(@"@@ -(\d+)(?:,\d+)? \+(\d+)(?:,\d+)? @@", RegexOptions.Compiled);

	public static ParsedDiff Parse(string? diffText)
	{
		if(string.IsNullOrWhiteSpace(diffText))
		{
			return new ParsedDiff(string.Empty, string.Empty, [], 0, 0);
		}

		string[] rawLines = diffText.Split('\n');
		List<DiffHunk> hunks = [];
		string oldPath = string.Empty;
		string newPath = string.Empty;
		DiffHunk? currentHunk = null;
		int oldLine = 0;
		int newLine = 0;
		int linesAdded = 0;
		int linesRemoved = 0;

		foreach(string raw in rawLines)
		{
			string line = raw.TrimEnd('\r');

			if(line.StartsWith("--- ", StringComparison.Ordinal))
			{
				oldPath = TrimGitPathPrefix(line[4..]);
				continue;
			}
			if(line.StartsWith("+++ ", StringComparison.Ordinal))
			{
				newPath = TrimGitPathPrefix(line[4..]);
				continue;
			}
			if(line.StartsWith("@@ ", StringComparison.Ordinal))
			{
				Match match = hunkHeaderRegex.Match(line);
				if(match.Success)
				{
					oldLine = int.Parse(match.Groups[1].Value);
					newLine = int.Parse(match.Groups[2].Value);
				}

				currentHunk = new DiffHunk(line, []);
				hunks.Add(currentHunk);
				continue;
			}

			if(currentHunk is null)
			{
				continue;
			}

			if(line.StartsWith('-'))
			{
				currentHunk.Lines.Add(new DiffLine(DiffLineType.Removed, line.Length > 1 ? line[1..] : string.Empty, oldLine++, null));
				linesRemoved++;
			}
			else if(line.StartsWith('+'))
			{
				currentHunk.Lines.Add(new DiffLine(DiffLineType.Added, line.Length > 1 ? line[1..] : string.Empty, null, newLine++));
				linesAdded++;
			}
			else
			{
				string content = line.Length > 0 && line[0] == ' ' ? line[1..] : line;
				currentHunk.Lines.Add(new DiffLine(DiffLineType.Context, content, oldLine++, newLine++));
			}
		}

		return new ParsedDiff(oldPath, newPath, hunks, linesAdded, linesRemoved);
	}

	public static List<SplitRow> BuildSplitRows(DiffHunk hunk)
	{
		List<SplitRow> rows = [];
		List<DiffLine> lines = hunk.Lines;
		int i = 0;

		while(i < lines.Count)
		{
			DiffLine current = lines[i];
			if(current.Type == DiffLineType.Context)
			{
				rows.Add(new SplitRow(current, current));
				i++;
			}
			else
			{
				List<DiffLine> removed = [];
				while(i < lines.Count && lines[i].Type == DiffLineType.Removed)
				{
					removed.Add(lines[i++]);
				}

				List<DiffLine> added = [];
				while(i < lines.Count && lines[i].Type == DiffLineType.Added)
				{
					added.Add(lines[i++]);
				}

				int count = Math.Max(removed.Count, added.Count);
				for(int j = 0; j < count; j++)
				{
					DiffLine? left = j < removed.Count ? removed[j] : null;
					DiffLine? right = j < added.Count ? added[j] : null;
					rows.Add(new SplitRow(left, right));
				}
			}
		}

		return rows;
	}

	static string TrimGitPathPrefix(string path)
	{
		if(path.StartsWith("a/", StringComparison.Ordinal) || path.StartsWith("b/", StringComparison.Ordinal))
		{
			return path[2..];
		}

		return path;
	}
}

public enum DiffLineType { Context, Added, Removed }

public sealed record DiffLine(DiffLineType Type, string Content, int? OldLineNumber, int? NewLineNumber);

public sealed class DiffHunk(string header, List<DiffLine> lines)
{
	public string Header { get; } = header;
	public List<DiffLine> Lines { get; } = lines;
}

public sealed record ParsedDiff(string OldPath, string NewPath, List<DiffHunk> Hunks, int LinesAdded, int LinesRemoved);

public sealed record SplitRow(DiffLine? Left, DiffLine? Right);
