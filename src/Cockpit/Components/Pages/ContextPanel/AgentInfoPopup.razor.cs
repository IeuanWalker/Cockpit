using Cockpit.Components.Controls;
using Cockpit.Features.Agents.Models;
using Cockpit.Utilities;
using Microsoft.AspNetCore.Components;
using Microsoft.JSInterop;

namespace Cockpit.Components.Pages.ContextPanel;

public partial class AgentInfoPopup : ComponentBase
{
	PopupBase? _popup;
	AgentProfile? _selectedAgent;
	List<AgentProfile> _agents = [];

	Dictionary<string, string> _frontmatter = [];
	string _body = string.Empty;

	bool _treeView;
	readonly Dictionary<string, bool> _expandedGroups = new(StringComparer.OrdinalIgnoreCase);
	List<AgentNode>? _cachedNodes;

	bool _needsSplitInit;

	readonly IJSRuntime _jsRuntime;

	public AgentInfoPopup(IJSRuntime jsRuntime)
	{
		_jsRuntime = jsRuntime;
	}

	List<AgentNode> DisplayNodes => _cachedNodes ??= BuildDisplayNodes();

	public void Open(IReadOnlyList<AgentProfile> agents, AgentProfile selectedAgent)
	{
		_agents = [.. agents];
		_cachedNodes = null;
		_needsSplitInit = true;
		_popup?.Open();
		SelectAgent(selectedAgent);
	}

	void SelectAgent(AgentProfile agent)
	{
		_selectedAgent = agent;
		_frontmatter = [];
		_body = string.Empty;
		_ = InvokeAsync(async () =>
		{
			await LoadAgentContentAsync(agent);
			StateHasChanged();
		});
	}

	protected override async Task OnAfterRenderAsync(bool firstRender)
	{
		if(_needsSplitInit)
		{
			_needsSplitInit = false;
			await _jsRuntime.InvokeVoidAsync("cockpit.initializePanelSplit", "agent-left-panel", "agent-split-handle");
		}
	}

	async Task LoadAgentContentAsync(AgentProfile agent)
	{
		if(string.IsNullOrEmpty(agent.FilePath) || !File.Exists(agent.FilePath))
		{
			return;
		}

		try
		{
			string content = await File.ReadAllTextAsync(agent.FilePath);
			(_frontmatter, _body) = ParseAgentFile(content);
		}
		catch
		{
			// leave empty on any read/parse error
		}
	}

	void RevealAgentFile()
	{
		FileUtil.RevealFile(_selectedAgent?.FilePath);
	}

	void SetTreeView(bool tree)
	{
		_treeView = tree;
		_cachedNodes = null;
	}

	void ToggleGroup(string key)
	{
		bool current = _expandedGroups.GetValueOrDefault(key, true);
		_expandedGroups[key] = !current;
		_cachedNodes = null;
	}

	List<AgentNode> BuildDisplayNodes()
	{
		if(!_treeView)
		{
			return [.. _agents
.OrderBy(a => a.DisplayLabel, StringComparer.OrdinalIgnoreCase)
.Select(a => new AgentNode(false, a.DisplayLabel, null, a, 0))];
		}

		AgentTreeDir root = new() { Name = string.Empty, Key = string.Empty };

		foreach(AgentProfile agent in _agents)
		{
			if(agent.FilePath is null)
			{
				continue;
			}

			string[] parts = agent.FilePath.Split(Path.DirectorySeparatorChar, StringSplitOptions.RemoveEmptyEntries);
			AgentTreeDir dir = root;

			for(int i = 0; i < parts.Length - 1; i++)
			{
				string part = parts[i];
				string key = dir.Key.Length == 0 ? part : dir.Key + Path.DirectorySeparatorChar + part;

				if(!dir.Dirs.TryGetValue(part, out AgentTreeDir? child))
				{
					child = new AgentTreeDir { Name = part, Key = key };
					dir.Dirs[part] = child;
				}

				dir = child;
			}

			dir.Agents.Add(agent);
		}

		List<AgentNode> result = [];
		AppendAgentDirNodes(root, 0, result);
		return result;
	}

	void AppendAgentDirNodes(AgentTreeDir dir, int depth, List<AgentNode> result)
	{
		foreach(AgentTreeDir subDir in dir.Dirs.Values.OrderBy(d => d.Name, StringComparer.OrdinalIgnoreCase))
		{
			result.Add(new AgentNode(true, subDir.Name, subDir.Key, null, depth));

			if(_expandedGroups.GetValueOrDefault(subDir.Key, true))
			{
				AppendAgentDirNodes(subDir, depth + 1, result);
			}
		}

		foreach(AgentProfile agent in dir.Agents.OrderBy(a => a.DisplayLabel, StringComparer.OrdinalIgnoreCase))
		{
			result.Add(new AgentNode(false, agent.DisplayLabel, null, agent, depth));
		}
	}

	static (Dictionary<string, string> frontmatter, string body) ParseAgentFile(string raw)
	{
		Dictionary<string, string> frontmatter = new(StringComparer.OrdinalIgnoreCase);
		string content = raw.TrimStart('\uFEFF').ReplaceLineEndings("\n");

		if(!content.StartsWith("---\n", StringComparison.Ordinal))
			return (frontmatter, content.TrimStart('\n'));

		int endFm = content.IndexOf("\n---", 4, StringComparison.Ordinal);
		if(endFm <= 0)
			return (frontmatter, content.TrimStart('\n'));

		string[] lines = content[4..endFm].Split('\n');

		string? currentKey = null;
		string? scalarMode = null;
		List<string> collected = new();

		void Flush()
		{
			if(currentKey is null) return;
			string val = BuildFrontmatterValue(scalarMode, collected);
			if(!string.IsNullOrEmpty(val))
				frontmatter[currentKey] = val;
			currentKey = null;
			scalarMode = null;
			collected.Clear();
		}

		foreach(string line in lines)
		{
			if(line.Length > 0 && (line[0] == ' ' || line[0] == '\t'))
			{
				collected.Add(line);
				continue;
			}

			Flush();

			int colon = line.IndexOf(':');
			if(colon <= 0) continue;

			currentKey = line[..colon].Trim();
			string rawVal = line[(colon + 1)..].Trim();

			if(rawVal is ">-" or ">" or "|" or "|-")
			{
				scalarMode = rawVal;
			}
			else
			{
				if(rawVal.Length >= 2 &&
				   ((rawVal[0] == '\'' && rawVal[^1] == '\'') ||
				    (rawVal[0] == '"' && rawVal[^1] == '"')))
					rawVal = rawVal[1..^1];
				collected.Add(rawVal);
			}
		}

		Flush();

		int bodyStart = endFm + 4;
		while(bodyStart < content.Length && content[bodyStart] == '\n')
			bodyStart++;

		return (frontmatter, content[bodyStart..].TrimStart('\n'));
	}

	static string BuildFrontmatterValue(string? scalarMode, List<string> lines)
	{
		if(lines.Count == 0) return string.Empty;

		if(scalarMode is ">-" or ">")
			return string.Join(" ", lines.Select(l => l.Trim()).Where(l => l.Length > 0));

		if(scalarMode is "|" or "|-")
		{
			int indent = lines.Where(l => l.Length > 0).Select(l => l.Length - l.TrimStart().Length).DefaultIfEmpty(0).Min();
			return string.Join("\n", lines.Select(l => l.Length > indent ? l[indent..] : l.TrimStart())).Trim();
		}

		if(lines.Count == 1) return lines[0];

		return FormatBlockFrontmatterValue(lines);
	}

	static string FormatBlockFrontmatterValue(List<string> lines)
	{
		List<string> items = new();
		string? label = null;
		string? agent = null;

		void FlushItem()
		{
			if(label is null) return;
			items.Add(agent is not null ? $"{label} → {agent}" : label);
			label = null;
			agent = null;
		}

		foreach(string raw in lines)
		{
			string line = raw.TrimStart();
			if(line.StartsWith("- ", StringComparison.Ordinal))
			{
				FlushItem();
				string rest = line[2..].Trim();
				int c = rest.IndexOf(':');
				if(c > 0)
				{
					string key = rest[..c].Trim();
					string val = rest[(c + 1)..].Trim().Trim('\'', '"');
					if(key.Equals("label", StringComparison.OrdinalIgnoreCase))
						label = val;
					else
						items.Add($"{key}: {val}");
				}
				else if(rest.Length > 0)
				{
					items.Add(rest.Trim('\'', '"'));
				}
			}
			else if(line.Length > 0 && (label is not null || agent is not null))
			{
				int c = line.IndexOf(':');
				if(c > 0)
				{
					string key = line[..c].Trim();
					string val = line[(c + 1)..].Trim().Trim('\'', '"');
					if(key.Equals("agent", StringComparison.OrdinalIgnoreCase))
						agent = val;
					else if(key.Equals("label", StringComparison.OrdinalIgnoreCase) && label is null)
						label = val;
				}
			}
		}

		FlushItem();

		if(items.Count > 0) return string.Join(", ", items);

		return string.Join(", ", lines.Select(l => l.Trim()).Where(l => l.Length > 0));
	}
}

sealed class AgentTreeDir
{
	public required string Name { get; init; }
	public required string Key { get; init; }
	public Dictionary<string, AgentTreeDir> Dirs { get; } = new(StringComparer.OrdinalIgnoreCase);
	public List<AgentProfile> Agents { get; } = [];
}

sealed record AgentNode(bool IsGroup, string Label, string? DirKey, AgentProfile? Agent, int Depth);