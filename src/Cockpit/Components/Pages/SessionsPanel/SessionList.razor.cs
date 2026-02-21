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
	[Inject] TimestampFeature _timestampFeature { get; set; } = default!;
	[Inject] UIStateFeature _uiState { get; set; } = default!;
	[Inject] SessionFeature _sessionManager { get; set; } = default!;

	bool _showDeleteDialog = false;
	ChatSession? _sessionToDelete;

	protected override void OnInitialized()
	{
		_sessionManager.OnStateChanged += OnStateChanged;
		_uiState.OnStateChanged += OnStateChanged;
		_timestampFeature.OnTick += OnStateChanged;
	}

	void OnStateChanged()
	{
		InvokeAsync(StateHasChanged);
	}

	async Task SelectSession(ChatSession session)
	{
		await _sessionManager.ResumeSessionAsync(session.Id);
	}

	static string GetSessionStatusClass(ChatSession session)
	{
		return session.Status switch
		{
			SessionStatus.NeedsPermission => "status-needs-permission",
			SessionStatus.Running => "status-running",
			SessionStatus.Finished => "status-finished",
			_ => "secondary-text"
		};
	}

	static string GetTimeAgo(DateTime dateTime)
	{
		return dateTime.Humanize();
	}

	void ShowDeleteDialog(ChatSession session, MouseEventArgs _)
	{
		_sessionToDelete = session;
		_showDeleteDialog = true;
		StateHasChanged();
	}

	async Task ConfirmDelete()
	{
		if(_sessionToDelete is not null)
		{
			await _sessionManager.DeleteSessionAsync(_sessionToDelete.Id);
		}
		_showDeleteDialog = false;
		_sessionToDelete = null;
		StateHasChanged();
	}

	void CancelDelete()
	{
		_showDeleteDialog = false;
		_sessionToDelete = null;
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
			_sessionManager.OnStateChanged -= OnStateChanged;
			_uiState.OnStateChanged -= OnStateChanged;
			_timestampFeature.OnTick -= OnStateChanged;
		}
	}
}
