using Cockpit.Components.Controls;
using Cockpit.Features.UIState;
using Microsoft.AspNetCore.Components;

namespace Cockpit.Components.Popups.Settings;

public partial class SettingsPopup : ComponentBase, IDisposable
{
	readonly UIStateFeature _uiStateFeature;
	public SettingsPopup(UIStateFeature uiStateFeature)
	{
		_uiStateFeature = uiStateFeature;
	}

	SettingsSectionEnum _activeSection = SettingsSectionEnum.Appearance;
	PopupBase _popup = default!;

	public void OpenToSection(SettingsSectionEnum section)
	{
		_activeSection = section;
		_popup.Open();
		StateHasChanged();
	}

	protected override void OnInitialized()
	{
		_uiStateFeature.OnStateChanged += OnStateChanged;
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
			_uiStateFeature.OnStateChanged -= OnStateChanged;
		}
	}
}
