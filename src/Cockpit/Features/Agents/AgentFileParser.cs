using Cockpit.Features.Agents.Models;
using GitHub.Copilot.SDK;

namespace Cockpit.Features.Agents;

/// <summary>
/// Parses <c>.agent.md</c> files (Markdown with YAML frontmatter) into <see cref="AgentProfile"/> instances.
/// </summary>
public static class AgentFileParser
{
	/// <summary>
	/// Attempts to parse an agent file. Returns null if the file cannot be parsed or has no name.
	/// </summary>
	public static async Task<AgentProfile?> TryParse(string filePath, AgentSource source)
	{
		try
		{
			string content = await File.ReadAllTextAsync(filePath);
			return ParseContent(content, filePath, source);
		}
		catch
		{
			return null;
		}
	}

	static AgentProfile? ParseContent(string content, string filePath, AgentSource source)
	{
		content = content.ReplaceLineEndings("\n");

		// Split on --- frontmatter delimiters
		// Format: --- YAML --- body
		if(!content.StartsWith("---"))
		{
			return null;
		}

		int firstEnd = FindFrontmatterEnd(content, 3);
		if(firstEnd < 0)
		{
			return null;
		}

		string frontmatter = content[3..firstEnd].Trim();
		string prompt = content[(firstEnd + 4)..].Trim();

		CustomAgentConfig config = ParseFrontmatter(frontmatter);

		// Prompt body overrides frontmatter prompt if present
		if(!string.IsNullOrWhiteSpace(prompt))
		{
			config.Prompt = prompt;
		}

		if(string.IsNullOrWhiteSpace(config.Name))
		{
			// Fall back to filename without extension
			config.Name = Path.GetFileNameWithoutExtension(Path.GetFileNameWithoutExtension(filePath)); // strip .agent.md
		}

		if(string.IsNullOrWhiteSpace(config.Name))
		{
			return null;
		}

		return new AgentProfile
		{
			Config = config,
			Source = source,
			FilePath = filePath
		};
	}

	static CustomAgentConfig ParseFrontmatter(string yaml)
	{
		CustomAgentConfig config = new();
		List<string>? currentList = null;

		Dictionary<string, string> frontmatter = [];
		Dictionary<string, List<string>> frontmatterLists = [];

		foreach(string rawLine in yaml.Split('\n'))
		{
			string line = rawLine.TrimEnd();

			// List item
			if(line.TrimStart().StartsWith("- ") && currentList is not null)
			{
				string item = line.TrimStart()[2..].Trim().Trim('"').Trim('\'');
				currentList.Add(item);
				continue;
			}

			// Key: value pair
			int colonIdx = line.IndexOf(':');
			if(colonIdx <= 0)
			{
				currentList = null;
				continue;
			}

			string key = line[..colonIdx].Trim().ToLowerInvariant();
			string value = line[(colonIdx + 1)..].Trim().Trim('"').Trim('\'');

			if(string.IsNullOrWhiteSpace(value))
			{
				currentList = [];
				frontmatterLists[key] = currentList;
			}
			else
			{
				currentList = null;
				frontmatter[key] = value;
			}
		}


		foreach(KeyValuePair<string, string> kvp in frontmatter)
		{
			switch(kvp.Key)
			{
				case "name":
					config.Name = kvp.Value;
					break;
				case "displayname":
					config.DisplayName = string.IsNullOrWhiteSpace(kvp.Value) ? null : kvp.Value;
					break;
				case "description":
					config.Description = string.IsNullOrWhiteSpace(kvp.Value) ? null : kvp.Value;
					break;
				case "prompt":
					if(!string.IsNullOrWhiteSpace(kvp.Value))
					{
						config.Prompt = kvp.Value;
					}
					break;
				case "disable-model-invocation":
					if(bool.TryParse(kvp.Value, out bool disableModelInvocation))
					{
						config.Infer = disableModelInvocation;
					}
					break;
				case "infer":
					if(!frontmatter.TryGetValue("disable-model-invocation", out string? _))
					{
						if(bool.TryParse(kvp.Value, out bool inferValue))
						{
							config.Infer = inferValue;
						}
					}
					break;
				case "tools":
					// Inline list: tools: [item1, item2]
					if(kvp.Value.StartsWith('[') && kvp.Value.EndsWith(']'))
					{
						config.Tools = ParseInlineList(kvp.Value);
					}
					break;
					// TODO: Support more - target, mcp-servers
			}
		}

		// Handle block-list values (e.g. tools: followed by - item lines)
		if(frontmatterLists.TryGetValue("tools", out List<string>? toolsList))
		{
			config.Tools ??= toolsList;
		}

		return config;
	}

	static List<string> ParseInlineList(string inline)
	{
		// Parse: [item1, item2, "item3"]
		string inner = inline.Trim('[', ']');
		return [.. inner.Split(',')
			.Select(s => s.Trim().Trim('"').Trim('\''))
			.Where(s => !string.IsNullOrWhiteSpace(s))];
	}

	/// <summary>
	/// Finds the index of the first <c>\n---</c> that is a bare closing delimiter
	/// (i.e., followed by <c>\n</c> or end-of-string), not a <c>---</c>-prefixed YAML value.
	/// </summary>
	static int FindFrontmatterEnd(string content, int startIdx)
	{
		int idx = startIdx;
		while(idx < content.Length)
		{
			int candidate = content.IndexOf("\n---", idx);
			if(candidate < 0)
			{
				return -1;
			}

			int afterDashes = candidate + 4; // position after \n---
			if(afterDashes >= content.Length || content[afterDashes] == '\n')
			{
				return candidate;
			}

			idx = candidate + 1;
		}

		return -1;
	}
}
