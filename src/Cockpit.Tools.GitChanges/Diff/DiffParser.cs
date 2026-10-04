using System.Text.RegularExpressions;

namespace Cockpit.Tools.GitChanges.Diff;

public static partial class DiffParser
{
	[GeneratedRegex(@"@@ -(\d+)(?:,\d+)? \+(\d+)(?:,\d+)? @@", RegexOptions.NonBacktracking)]
	private static partial Regex HunkHeaderRegex();

	public static ParsedDiff Parse(string? text)
	{
		if(string.IsNullOrWhiteSpace(text))
		{
			return new("", "", [], 0, 0);
		}

		string oldPath = "", newPath = "";
		List<DiffHunk> hunks = [];
		List<DiffLine>? lines = null;
		string header = "";
		int oldStart = 0, newStart = 0, oldLine = 0, newLine = 0, added = 0, removed = 0;
		void Finish()
		{
			if(lines is not null)
			{
				hunks.Add(new(header, lines, oldStart, newStart));
			}
		}

		foreach(string raw in text.Split('\n'))
		{
			string line = raw.TrimEnd('\r');
			if(line.StartsWith("\\ ", StringComparison.Ordinal))
			{
				continue;
			}

			if(line.StartsWith("--- ", StringComparison.Ordinal)) { oldPath = TrimPrefix(line[4..]); continue; }
			if(line.StartsWith("+++ ", StringComparison.Ordinal)) { newPath = TrimPrefix(line[4..]); continue; }
			if(line.StartsWith("@@ ", StringComparison.Ordinal))
			{
				Finish();
				Match match = HunkHeaderRegex().Match(line);
				oldLine = oldStart = match.Success ? int.Parse(match.Groups[1].Value) : 0;
				newLine = newStart = match.Success ? int.Parse(match.Groups[2].Value) : 0;
				header = line;
				lines = [];
				continue;
			}
			if(lines is null)
			{
				continue;
			}

			if(line.StartsWith('-')) { lines.Add(new(DiffLineType.Removed, line[1..], oldLine++, null)); removed++; }
			else if(line.StartsWith('+')) { lines.Add(new(DiffLineType.Added, line[1..], null, newLine++)); added++; }
			else { lines.Add(new(DiffLineType.Context, line.StartsWith(' ') ? line[1..] : line, oldLine++, newLine++)); }
		}
		Finish();
		return new(oldPath, newPath, hunks, added, removed);
	}

	static string TrimPrefix(string path) =>
		path.StartsWith("a/", StringComparison.Ordinal) || path.StartsWith("b/", StringComparison.Ordinal) ? path[2..] : path;
}
