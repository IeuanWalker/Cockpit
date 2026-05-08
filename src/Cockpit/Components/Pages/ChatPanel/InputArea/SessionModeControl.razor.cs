using Cockpit.Features.Sessions;
using Cockpit.Features.Sessions.Models;
using Microsoft.AspNetCore.Components;

namespace Cockpit.Components.Pages.ChatPanel.InputArea;

public partial class SessionModeControl : ComponentBase, IDisposable
{
	readonly SessionListFeature _sessionListFeature;
	readonly SessionModePersistence _sessionModePersistence;

	public SessionModeControl(SessionListFeature sessionListFeature, SessionModePersistence sessionModePersistence)
	{
		_sessionListFeature = sessionListFeature;
		_sessionModePersistence = sessionModePersistence;
	}

	PickerControl _picker = default!;

	protected override void OnInitialized()
	{
		_sessionListFeature.OnStateChanged += OnStateChanged;
	}

	void OnStateChanged()
	{
		InvokeAsync(StateHasChanged);
	}

	SessionAgentModeEnum GetCurrentMode()
	{
		return _sessionListFeature.CurrentSession?.Context.SelectedAgentMode ?? SessionAgentModeEnum.Interactive;
	}

	void SelectMode(SessionAgentModeEnum mode)
	{
		if(_sessionListFeature.CurrentSession is null)
		{
			_picker.Close();
			return;
		}

		if(_sessionListFeature.CurrentSession.Context.SelectedAgentMode == mode)
		{
			_picker.Close();
			return;
		}

		_sessionListFeature.CurrentSession.Context.SelectedAgentMode = mode;
		_sessionListFeature.CurrentSession.AgentModeChanged = true;

		_ = _sessionModePersistence.SaveSessionModeAsync(_sessionListFeature.CurrentSession);

		_picker.Close();
		_sessionListFeature.NotifyStateChanged();
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
			_sessionListFeature.OnStateChanged -= OnStateChanged;
		}
	}
}
