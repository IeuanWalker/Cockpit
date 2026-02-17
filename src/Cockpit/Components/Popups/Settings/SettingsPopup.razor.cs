using Cockpit.Services;
using Microsoft.AspNetCore.Components;

namespace Cockpit.Components.Popups.Settings;

public enum SettingsSection
{
	Appearance,
	Commands,
	Input,
	Voice,
	Sounds,
	Diagnostics
}

public partial class SettingsPopup : ComponentBase, IDisposable
{
	SettingsSection _activeSection = SettingsSection.Appearance;
	[Inject] UIStateService _uiState { get; set; } = default!;

	public void OpenToSection(SettingsSection section)
	{
		_activeSection = section;
		StateHasChanged();
	}

	protected override void OnInitialized()
	{
		_uiState.OnStateChanged += OnStateChanged;
	}

	protected override void OnAfterRender(bool firstRender)
	{
		if(_uiState.PendingSettingsSection.HasValue)
		{
			_activeSection = _uiState.PendingSettingsSection.Value;
			_uiState.ClearPendingSettingsSection();
			StateHasChanged();
		}
	}

	void OnStateChanged()
	{
		InvokeAsync(StateHasChanged);
	}

	void SetActiveSection(SettingsSection section)
	{
		_activeSection = section;
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
			_uiState.OnStateChanged -= OnStateChanged;
		}
	}
}
