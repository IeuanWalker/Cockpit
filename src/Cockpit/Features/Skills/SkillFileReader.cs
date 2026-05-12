namespace Cockpit.Features.Skills;

/// <summary>Reads and parses skill definition files from disk.</summary>
static class SkillFileReader
{
	/// <summary>
	/// Reads the Markdown content of the skill file at <paramref name="path"/>, stripping any
	/// YAML front-matter. Returns <see langword="null"/> when the path is absent, is not a
	/// <c>.md</c> file, or cannot be read.
	/// </summary>
	internal static async Task<string?> ReadAsync(string? path)
	{
		if(string.IsNullOrEmpty(path)
			|| !path.EndsWith(".md", StringComparison.OrdinalIgnoreCase)
			|| !File.Exists(path))
		{
			return null;
		}

		try
		{
			string raw = await File.ReadAllTextAsync(path);
			return StripFrontmatter(raw);
		}
		catch
		{
			return null;
		}
	}

	/// <summary>
	/// Strips YAML front-matter delimited by <c>---</c> from the beginning of
	/// <paramref name="raw"/> and returns the remaining body.
	/// The BOM (<c>\uFEFF</c>) is also removed when present.
	/// </summary>
	internal static string StripFrontmatter(string raw)
	{
		string content = raw.TrimStart('\uFEFF').ReplaceLineEndings("\n");

		if(!content.StartsWith("---\n", StringComparison.Ordinal))
		{
			return content;
		}

		int endFm = content.IndexOf("\n---", 3, StringComparison.Ordinal);
		if(endFm <= 0)
		{
			return content;
		}

		int bodyStart = endFm + 4;
		while(bodyStart < content.Length && content[bodyStart] == '\n')
		{
			bodyStart++;
		}

		return content[bodyStart..];
	}
}
