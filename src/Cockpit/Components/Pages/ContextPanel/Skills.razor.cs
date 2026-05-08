using Cockpit.Features.Sessions;
using GitHub.Copilot.SDK.Rpc;
using Microsoft.AspNetCore.Components;

namespace Cockpit.Components.Pages.ContextPanel;

public sealed partial class Skills : ComponentBase, IDisposable
{
SkillInfoPopup? _skillInfoPopup;
readonly SessionListFeature _sessionListFeature;

public Skills(SessionListFeature sessionListFeature)
{
_sessionListFeature = sessionListFeature;
}

List<Skill> _allSkills = [];
Skill? _selectedSkill;

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