using Cockpit.Components.Controls;
using Cockpit.Features.Sessions;
using Cockpit.Features.Sessions.Models;
using Cockpit.Features.Skills;
using Cockpit.Utilities;
using GitHub.Copilot.SDK.Rpc;
using Microsoft.AspNetCore.Components;

namespace Cockpit.Components.Pages.ContextPanel;

public partial class SkillInfoPopup : ComponentBase
{
	PopupBase? _popup;
	Skill? _selectedSkill;
	List<Skill> _skills = [];
	bool _isBusy;
	string? _sessionId;

	bool _treeView;
	readonly Dictionary<string, bool> _expandedGroups = new(StringComparer.OrdinalIgnoreCase);
	List<SkillNode>? _cachedNodes;

	string _skillFileContent = string.Empty;

	List<SkillNode> DisplayNodes => _cachedNodes ??= BuildDisplayNodes();

	readonly SkillsFeature _skillsFeature;
	readonly SessionListFeature _sessionListFeature;

	public SkillInfoPopup(SkillsFeature skillsFeature, SessionListFeature sessionListFeature)
	{
		_skillsFeature = skillsFeature;
		_sessionListFeature = sessionListFeature;
	}

	public void Open(IReadOnlyList<Skill> skills, Skill selectedSkill)
	{
		_skills = [.. skills];
		_sessionId = _sessionListFeature.CurrentSession?.Id;
		_cachedNodes = null;
		_popup?.Open();
		SelectSkill(selectedSkill);
	}

	void SelectSkill(Skill skill)
	{
		_selectedSkill = skill;
		_skillFileContent = string.Empty;
		LoadSkillFileContent();
		StateHasChanged();
	}

	void LoadSkillFileContent()
	{
		Skill? skill = _selectedSkill;
		if(skill is null) return;
		_ = InvokeAsync(async () =>
		{
			if(!string.IsNullOrEmpty(skill.Path)
				&& skill.Path.EndsWith(".md", StringComparison.OrdinalIgnoreCase)
				&& File.Exists(skill.Path))
			{
				try
				{
					_skillFileContent = await File.ReadAllTextAsync(skill.Path);
				}
				catch
				{
					_skillFileContent = string.Empty;
				}
			}
			StateHasChanged();
		});
	}

	void RevealSkillFile() => FileUtil.RevealFile(_selectedSkill?.Path);

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

	List<SkillNode> BuildDisplayNodes()
	{
		if(!_treeView)
		{
			return [.. _skills
				.OrderBy(s => s.Name, StringComparer.OrdinalIgnoreCase)
				.Select(s => new SkillNode(false, s.Name, null, s, 0))];
		}

		SkillTreeDir root = new() { Name = string.Empty, Key = string.Empty };

		foreach(var skill in _skills)
		{
			if(string.IsNullOrEmpty(skill.Path))
			{
				root.Skills.Add(skill);
				continue;
			}

			string[] parts = skill.Path.Split(Path.DirectorySeparatorChar, StringSplitOptions.RemoveEmptyEntries);
			SkillTreeDir dir = root;

			for(int i = 0; i < parts.Length - 1; i++)
			{
				string part = parts[i];
				string key = dir.Key.Length == 0 ? part : dir.Key + Path.DirectorySeparatorChar + part;

				if(!dir.Dirs.TryGetValue(part, out SkillTreeDir? child))
				{
					child = new SkillTreeDir { Name = part, Key = key };
					dir.Dirs[part] = child;
				}

				dir = child;
			}

			dir.Skills.Add(skill);
		}

		List<SkillNode> result = [];
		AppendSkillDirNodes(root, 0, result);
		return result;
	}

	void AppendSkillDirNodes(SkillTreeDir dir, int depth, List<SkillNode> result)
	{
		foreach(SkillTreeDir subDir in dir.Dirs.Values.OrderBy(d => d.Name, StringComparer.OrdinalIgnoreCase))
		{
			result.Add(new SkillNode(true, subDir.Name, subDir.Key, null, depth));

			if(_expandedGroups.GetValueOrDefault(subDir.Key, true))
			{
				AppendSkillDirNodes(subDir, depth + 1, result);
			}
		}

		foreach(var skill in dir.Skills.OrderBy(s => s.Name, StringComparer.OrdinalIgnoreCase))
		{
			result.Add(new SkillNode(false, skill.Name, null, skill, depth));
		}
	}

	async Task ToggleSkill(Skill skill)
	{
		if(_isBusy || _sessionId is null) return;
		_isBusy = true;
		StateHasChanged();
		try
		{
			if(skill.Enabled)
				await _skillsFeature.DisableSkillAsync(_sessionId, skill.Name);
			else
				await _skillsFeature.EnableSkillAsync(_sessionId, skill.Name);

			RefreshFromSession();
		}
		finally
		{
			_isBusy = false;
			StateHasChanged();
		}
	}

	async Task ReloadSkills()
	{
		if(_isBusy || _sessionId is null) return;
		_isBusy = true;
		StateHasChanged();
		try
		{
			await Task.WhenAll(_skillsFeature.ReloadAsync(_sessionId), Task.Delay(200));
			RefreshFromSession();
		}
		finally
		{
			_isBusy = false;
			StateHasChanged();
		}
	}

	void RefreshFromSession()
	{
		SessionModel? session = _sessionListFeature.Sessions.FirstOrDefault(s => s.Id == _sessionId);
		if(session is null) return;
		_skills = [.. session.Context.Skills];
		_cachedNodes = null;
		var refreshed = _skills.FirstOrDefault(s => s.Name == _selectedSkill?.Name);
		if(refreshed is not null && !ReferenceEquals(refreshed, _selectedSkill))
		{
			_selectedSkill = refreshed;
			LoadSkillFileContent();
		}
	}
}

sealed class SkillTreeDir
{
	public required string Name { get; init; }
	public required string Key { get; init; }
	public Dictionary<string, SkillTreeDir> Dirs { get; } = new(StringComparer.OrdinalIgnoreCase);
	public List<Skill> Skills { get; } = [];
}

sealed record SkillNode(bool IsGroup, string Label, string? DirKey, Skill? Skill, int Depth);
