using Cockpit.Features.Sessions;
using Microsoft.AspNetCore.Components;

namespace Cockpit.Components.Pages.ContextPanel;

public partial class AgentSkills : ComponentBase, IDisposable
{
	readonly SessionFeature _sessionFeature;

	public AgentSkills(SessionFeature sessionFeature)
	{
		_sessionFeature = sessionFeature;
	}

	void ToggleSkill(string skill) => _sessionFeature.ToggleCurrentSessionContextSkill(skill);
	bool IsSkillEnabled(string skill) => _sessionFeature.CurrentSession?.Context?.AgentSkills.Contains(skill) == true;

	protected override void OnInitialized()
	{
		_sessionFeature.OnStateChanged += OnStateChanged;
	}

	void OnStateChanged()
	{
		InvokeAsync(StateHasChanged);
	}

	public void Dispose()
	{
		Dispose(true);
		GC.SuppressFinalize(this);
	}

	protected virtual void Dispose(bool disposing)
	{
		if(disposing)
		{
			_sessionFeature.OnStateChanged -= OnStateChanged;
		}
	}
}