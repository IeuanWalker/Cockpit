using Cockpit.Features.AppSettings;
using Microsoft.AspNetCore.Components;

namespace Cockpit.Components.Popups.Settings;

public partial class PreviewFeaturesSettings : ComponentBase
{
	readonly IAppSettingsFeature _appSettingsFeature;

	public PreviewFeaturesSettings(IAppSettingsFeature appSettingsFeature)
	{
		_appSettingsFeature = appSettingsFeature;
	}

	bool _canvasEnabled;
	bool _showRestartWarning;

	protected override void OnInitialized()
	{
		_canvasEnabled = _appSettingsFeature.CanvasEnabled;
	}

	void OnCanvasChanged()
	{
		_appSettingsFeature.CanvasEnabled = _canvasEnabled;
		_showRestartWarning = true;
	}
}
