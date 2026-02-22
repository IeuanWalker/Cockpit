using Cockpit.Features.Sessions;
using Cockpit.Features.Sessions.Models;
using Cockpit.Features.Timestamp;
using Cockpit.Features.UIState;
using Humanizer;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Web;

namespace Cockpit.Components.Pages.SessionsPanel;

public partial class SessionList : ComponentBase, IDisposable
{
	readonly TimestampFeature _timestampFeature;
	readonly UIStateFeature _uiState;
	readonly SessionFeature _sessionFeature;

	public SessionList(
		TimestampFeature timestampFeature,
		UIStateFeature uiState,
		SessionFeature sessionFeature)
	{
		_timestampFeature = timestampFeature;
		_uiState = uiState;
		_sessionFeature = sessionFeature;
	}

	DeleteSessionPopup _deleteSessionPopup = default!;

	protected override void OnInitialized()
	{
		_sessionFeature.OnStateChanged += OnStateChanged;
		_uiState.OnStateChanged += OnStateChanged;
		_timestampFeature.OnTick += OnStateChanged;
	}

	void OnStateChanged()
	{
		InvokeAsync(StateHasChanged);
	}

	async Task SelectSession(SessionModel session)
	{
		await _sessionFeature.LoadSession(session.Id);
	}

	static string GetSessionStatusClass(SessionModel session)
	{
		return session.Status switch
		{
			SessionStatusEnum.NeedsPermission => "status-needs-permission",
			SessionStatusEnum.Running => "status-running",
			SessionStatusEnum.Finished => "status-finished",
			_ => "secondary-text"
		};
	}

	static string GetTimeAgo(DateTime dateTime)
	{
		return dateTime.Humanize();
	}

	void ShowDeleteDialog(SessionModel session, MouseEventArgs _)
	{
		_deleteSessionPopup.Open(session.Id);
		StateHasChanged();
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
			_uiState.OnStateChanged -= OnStateChanged;
			_timestampFeature.OnTick -= OnStateChanged;
		}
	}
}
