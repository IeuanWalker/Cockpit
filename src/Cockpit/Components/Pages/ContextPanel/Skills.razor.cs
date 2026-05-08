using Cockpit.Features.Sessions;
using Cockpit.Features.Skills;
using GitHub.Copilot.SDK.Rpc;
using Microsoft.AspNetCore.Components;

namespace Cockpit.Components.Pages.ContextPanel;

public sealed partial class Skills : ComponentBase, IDisposable
{
	SkillInfoPopup? _skillInfoPopup;
	readonly SkillsFeature _skillsFeature;
	readonly SessionListFeature _sessionListFeature;

	public Skills(SkillsFeature skillsFeature, SessionListFeature sessionListFeature)
	{
		_skillsFeature = skillsFeature;
		_sessionListFeature = sessionListFeature;
	}

	List<Skill> _allSkills = [];
	Skill? _selectedSkill;
	bool _isBusy;

	int TotalCount => _allSkills.Count;

	protected override void OnInitialized()
	{
		_sessionListFeature.OnStateChanged += OnStateChanged;
		Refresh();
	}

	void OnStateChanged()
	{
		InvokeAsync(() => { Refresh(); StateHasChanged(); });
	}

	void ShowSkillInfo(Skill skill) => _skillInfoPopup?.Open(_allSkills, skill);

	async Task ToggleSkill(Skill skill)
	{
		if(_isBusy) return;
		_isBusy = true;
		StateHasChanged();
		try
		{
			string? sessionId = _sessionListFeature.CurrentSession?.Id;
			if(sessionId is null) return;
			if(skill.Enabled)
				await _skillsFeature.DisableSkillAsync(sessionId, skill.Name);
			else
				await _skillsFeature.EnableSkillAsync(sessionId, skill.Name);
		}
		finally
		{
			_isBusy = false;
			StateHasChanged();
		}
	}

	async Task ReloadSkills()
	{
		if(_isBusy) return;
		_isBusy = true;
		StateHasChanged();
		try
		{
			string? sessionId = _sessionListFeature.CurrentSession?.Id;
			if(sessionId is null) return;
			await _skillsFeature.ReloadAsync(sessionId);
		}
		finally
		{
			_isBusy = false;
			StateHasChanged();
		}
	}

	List<Skill> _renderedSkills = [];
	Skill? _renderedSelected;

	protected override bool ShouldRender()
	{
		if(ReferenceEquals(_allSkills, _renderedSkills) && ReferenceEquals(_renderedSelected, _selectedSkill))
			return false;
		_renderedSkills = _allSkills;
		_renderedSelected = _selectedSkill;
		return true;
	}

	void Refresh()
	{
		_allSkills = [.. _sessionListFeature.CurrentSession?.Context.Skills ?? []];
		_selectedSkill = null;
	}

	public void Dispose()
	{
		_sessionListFeature.OnStateChanged -= OnStateChanged;
	}
}