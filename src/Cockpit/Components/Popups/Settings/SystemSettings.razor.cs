using Cockpit.Features.AppSettings;
using Cockpit.Features.KeepAlive;
using Microsoft.AspNetCore.Components;

namespace Cockpit.Components.Popups.Settings;

public partial class SystemSettings : ComponentBase
{
	readonly IAppSettingsFeature _appSettingsFeature;
	readonly KeepAliveFeature _keepAliveFeature;

	public SystemSettings(IAppSettingsFeature appSettingsFeature, KeepAliveFeature keepAliveFeature)
	{
		_appSettingsFeature = appSettingsFeature;
		_keepAliveFeature = keepAliveFeature;
	}

	bool _keepAliveEnabled;

	protected override void OnInitialized()
	{
		_keepAliveEnabled = _appSettingsFeature.KeepAlive;
	}

	void OnKeepAliveChanged()
	{
		_appSettingsFeature.KeepAlive = _keepAliveEnabled;
		_keepAliveFeature.Recheck();
	}
}
