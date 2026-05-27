using Foundation;

#pragma warning disable IDE0130 // Namespace does not match folder structure
namespace Cockpit.Features.KeepAlive;
#pragma warning restore IDE0130

sealed class MacCatalystKeepAliveService : IKeepAliveService
{
	NSObject? _activity;

	public void Activate()
	{
		if(_activity is not null)
		{
			return;
		}

		_activity = NSProcessInfo.ProcessInfo.BeginActivity(
			NSActivityOptions.IdleSystemSleepDisabled | NSActivityOptions.UserInitiated,
			"Active Copilot session");
	}

	public void Deactivate()
	{
		if(_activity is not null)
		{
			NSProcessInfo.ProcessInfo.EndActivity(_activity);
			_activity = null;
		}
	}
}
