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
		Dictionary<string, string> frontmatter = [];
		string content = raw.TrimStart('\uFEFF').ReplaceLineEndings("\n");
		int bodyStart = 0;

		if(content.StartsWith("---\n", StringComparison.Ordinal))
		{
			int endFm = content.IndexOf("\n---", 3, StringComparison.Ordinal);
			if(endFm > 0)
			{
				string fm = content[3..endFm].Trim();
				foreach(string line in fm.Split('\n'))
				{
					int colon = line.IndexOf(':');
					if(colon > 0)
					{
						string val = line[(colon + 1)..].Trim();
						if(val.Length >= 2 && val[0] == '\'' && val[^1] == '\'')
						val = val[1..^1];
						frontmatter[line[..colon].Trim()] = val;
					}
				}

				bodyStart = endFm + 4; // past "\n---"
				while(bodyStart < content.Length && content[bodyStart] == '\n')
				{
					bodyStart++;
				}
			}
		}

		return (frontmatter, content[bodyStart..].TrimStart('\n'));
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