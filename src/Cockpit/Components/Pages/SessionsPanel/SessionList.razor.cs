using Cockpit.Models;
using Cockpit.Services;
using Humanizer;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Web;

namespace Cockpit.Components.Pages.SessionsPanel;

public partial class SessionList : ComponentBase, IDisposable
{
	[Inject] TimestampService _timestampService { get; set; } = default!;
	[Inject] UIStateService _uiState { get; set; } = default!;
	[Inject] UnifiedSessionManager _sessionManager { get; set; } = default!;

	bool _showDeleteDialog = false;
	ChatSession? _sessionToDelete;

	protected override void OnInitialized()
	{
		_sessionManager.OnStateChanged += OnStateChanged;
		_uiState.OnStateChanged += OnStateChanged;
		_timestampService.OnTick += OnStateChanged;
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
			SessionStatus.NeedsPermission => string.Empty,
			SessionStatus.Running => string.Empty,
			SessionStatus.Finished => string.Empty,
			_ => "secondary-text"
		};
	}

	static string GetSessionStatusStyle(ChatSession session)
	{
		return session.Status switch
		{
			SessionStatus.NeedsPermission => "color: #FFA500;",
			SessionStatus.Running => "color: #FFB900;",
			SessionStatus.Finished => "color: #10893E;",
			_ => ""
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
			_timestampService.OnTick -= OnStateChanged;
		}
	}
}
