using Cockpit.Components.Controls;
using Cockpit.Features.UIState;
using Microsoft.AspNetCore.Components;

namespace Cockpit.Components.Popups.Settings;

public partial class SettingsPopup : ComponentBase, IDisposable
{
	SettingsSectionEnum _activeSection = SettingsSectionEnum.Appearance;
	[Inject] UIStateFeature _uiState { get; set; } = default!;

	PopupBase _popup = default!;

	public void OpenToSection(SettingsSectionEnum section)
	{
		_activeSection = section;
		_popup.Open();
		StateHasChanged();
	}

	protected override void OnInitialized()
	{
		_uiState.OnStateChanged += OnStateChanged;
	}

	void OnStateChanged()
	{
		InvokeAsync(StateHasChanged);
	}

	void SetActiveSection(SettingsSectionEnum section)
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
