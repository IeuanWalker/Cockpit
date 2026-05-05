using System.Text.RegularExpressions;
using Cockpit.Components.Controls.GitDiff.Models;

namespace Cockpit.Components.Controls.GitDiff;

public static partial class DiffParser
{
	[GeneratedRegex(@"@@ -(\d+)(?:,\d+)? \+(\d+)(?:,\d+)? @@", RegexOptions.Compiled)]
	private static partial Regex HunkHeaderRegex();

	public static ParsedDiffModel Parse(string? diffText)
	{
		if(string.IsNullOrWhiteSpace(diffText))
		{
			return new ParsedDiffModel
			{
				OldPath = string.Empty,
				NewPath = string.Empty,
				Hunks = [],
				LinesAdded = 0,
				LinesRemoved = 0
			};
		}

		string[] rawLines = diffText.Split('\n');
		List<DiffHunkModel> hunks = [];
		string oldPath = string.Empty;
		string newPath = string.Empty;
		DiffHunkModel? currentHunk = null;
		int oldLine = 0;
		int newLine = 0;
		int linesAdded = 0;
		int linesRemoved = 0;

		foreach(string raw in rawLines)
		{
			string line = raw.TrimEnd('\r');

			// Git "no newline at end of file" annotation — not a content line; skip without touching counters
			if(line.StartsWith("\\ ", StringComparison.Ordinal))
			{
				continue;
			}

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
				Match match = HunkHeaderRegex().Match(line);
				if(match.Success)
				{
					oldLine = int.Parse(match.Groups[1].Value);
					newLine = int.Parse(match.Groups[2].Value);
				}

				currentHunk = new DiffHunkModel
				{
					Header = line,
					Lines = [],
					OldStartLine = match.Success ? int.Parse(match.Groups[1].Value) : 0,
					NewStartLine = match.Success ? int.Parse(match.Groups[2].Value) : 0,
				};
				hunks.Add(currentHunk);
				continue;
			}

			if(currentHunk is null)
			{
				continue;
			}

			if(line.StartsWith('-'))
			{
				currentHunk.Lines.Add(new DiffLineModel
				{
					Type = DiffLineTypeEnum.Removed,
					Content = line.Length > 1 ? line[1..] : string.Empty,
					OldLineNumber = oldLine++,
					NewLineNumber = null
				});

				linesRemoved++;
			}
			else if(line.StartsWith('+'))
			{
				currentHunk.Lines.Add(new DiffLineModel
				{
					Type = DiffLineTypeEnum.Added,
					Content = line.Length > 1 ? line[1..] : string.Empty,
					OldLineNumber = null,
					NewLineNumber = newLine++
				});

				linesAdded++;
			}
			else
			{
				string content = line.Length > 0 && line[0] == ' ' ? line[1..] : line;
				currentHunk.Lines.Add(new DiffLineModel
				{
					Type = DiffLineTypeEnum.Context,
					Content = content,
					OldLineNumber = oldLine++,
					NewLineNumber = newLine++
				});
			}
		}

		return new ParsedDiffModel
		{
			OldPath = oldPath,
			NewPath = newPath,
			Hunks = hunks,
			LinesAdded = linesAdded,
			LinesRemoved = linesRemoved
		};
	}

	public static List<SplitRowModel> BuildSplitRows(DiffHunkModel hunk)
	{
		List<SplitRowModel> rows = [];
		List<DiffLineModel> lines = hunk.Lines;
		int i = 0;

		while(i < lines.Count)
		{
			DiffLineModel current = lines[i];
			if(current.Type == DiffLineTypeEnum.Context)
			{
				rows.Add(new SplitRowModel
				{
					Left = current,
					Right = current
				});
				i++;
			}
			else
			{
				List<DiffLineModel> removed = [];
				while(i < lines.Count && lines[i].Type == DiffLineTypeEnum.Removed)
				{
					removed.Add(lines[i++]);
				}

				List<DiffLineModel> added = [];
				while(i < lines.Count && lines[i].Type == DiffLineTypeEnum.Added)
				{
					added.Add(lines[i++]);
				}

				int count = Math.Max(removed.Count, added.Count);
				for(int j = 0; j < count; j++)
				{
					DiffLineModel? left = j < removed.Count ? removed[j] : null;
					DiffLineModel? right = j < added.Count ? added[j] : null;

					List<(int Start, int Length)>? leftSpans = null;
					List<(int Start, int Length)>? rightSpans = null;
					if(left is not null && right is not null)
					{
						(List<(int Start, int Length)>? ls, List<(int Start, int Length)>? rs) = InlineDiffComputer.Compute(left.Content, right.Content);
						if(ls.Count > 0)
						{
							leftSpans = ls;
						}

						if(rs.Count > 0)
						{
							rightSpans = rs;
						}
					}

					rows.Add(new SplitRowModel
					{
						Left = left,
						Right = right,
						LeftSpans = leftSpans,
						RightSpans = rightSpans
					});
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