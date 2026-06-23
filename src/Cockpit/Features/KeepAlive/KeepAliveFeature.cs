using Cockpit.Features.AppSettings;
using Cockpit.Features.Sessions;

namespace Cockpit.Features.KeepAlive;

public sealed partial class KeepAliveFeature : IDisposable
{
	readonly IKeepAliveService _keepAliveService;
	readonly IAppSettingsFeature _appSettings;
	readonly SessionFeature _sessionFeature;
	readonly Lock _lock = new();
	bool _isActive;

	public KeepAliveFeature(IKeepAliveService keepAliveService, IAppSettingsFeature appSettings, SessionFeature sessionFeature)
	{
		_keepAliveService = keepAliveService;
		_appSettings = appSettings;
		_sessionFeature = sessionFeature;
		_sessionFeature.OnStateChanged += Recheck;
	}

	/// <summary>
	/// Re-evaluates whether keep-alive should be active. Call this when the setting changes.
	/// </summary>
	public void Recheck()
	{
		lock(_lock)
		{
			if(_disposed)
			{
				return;
			}

			bool shouldBeActive = _appSettings.KeepAlive && _sessionFeature.IsWorking;

			if(shouldBeActive && !_isActive)
			{
				_isActive = true;
				_keepAliveService.Activate();
			}
			else if(!shouldBeActive && _isActive)
			{
				_isActive = false;
				_keepAliveService.Deactivate();
			}
		}
	}

	bool _disposed;

	public void Dispose()
	{
		_sessionFeature.OnStateChanged -= Recheck;

		lock(_lock)
		{
			_disposed = true;

			if(_isActive)
			{
				_isActive = false;
				_keepAliveService.Deactivate();
			}
		}

		GC.SuppressFinalize(this);
	}
}
