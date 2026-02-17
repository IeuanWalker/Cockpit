using Cockpit.Services;
using Microsoft.AspNetCore.Components;

namespace Cockpit.Components.Pages.ContextPanel;

public partial class AgentSkills : ComponentBase, IDisposable
{
	[Inject] UIStateService _uiState { get; set; } = null!;
	[Inject] UnifiedSessionManager _sessionManager { get; set; } = default!;

	void ToggleSkill(string skill) => _sessionManager.ToggleCurrentSessionContextSkill(skill);
	bool IsSkillEnabled(string skill) => _sessionManager.CurrentSession?.Context?.AgentSkills.Contains(skill) == true;

	protected override void OnInitialized()
	{
		_sessionManager.OnStateChanged += OnStateChanged;
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
			_sessionManager.OnStateChanged -= OnStateChanged;
		}
	}
}