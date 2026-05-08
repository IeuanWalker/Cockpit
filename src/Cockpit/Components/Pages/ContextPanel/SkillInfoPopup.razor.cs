using Cockpit.Components.Controls;
using Cockpit.Features.Sessions;
using Cockpit.Features.Sessions.Models;
using Cockpit.Features.Skills;
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
	readonly Dictionary<string, bool> _expandedGroups = new(StringComparer.OrdinalIgnoreCase);

	Dictionary<string, List<Skill>> _groupedSkills = [];

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
		RebuildGroups();
		_popup?.Open();
		SelectSkill(selectedSkill);
	}

	void SelectSkill(Skill skill)
	{
		_selectedSkill = skill;
		StateHasChanged();
	}

	void ToggleGroup(string key)
	{
		bool current = _expandedGroups.GetValueOrDefault(key, true);
		_expandedGroups[key] = !current;
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
			await _skillsFeature.ReloadAsync(_sessionId);
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
		RebuildGroups();
		Skill? refreshed = _skills.FirstOrDefault(s => s.Name == _selectedSkill?.Name);
		_selectedSkill = refreshed ?? _selectedSkill;
	}

	void RebuildGroups()
	{
		_groupedSkills = _skills
			.GroupBy(s => string.IsNullOrWhiteSpace(s.Source) ? "Unknown" : s.Source, StringComparer.OrdinalIgnoreCase)
			.ToDictionary(g => g.Key, g => g.ToList(), StringComparer.OrdinalIgnoreCase);
	}
}
