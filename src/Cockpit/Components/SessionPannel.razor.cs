using Cockpit.Components.Popups;
using Cockpit.Models;
using Cockpit.Services;
using Humanizer;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.JSInterop;

namespace Cockpit.Components;

public partial class SessionPannel : ComponentBase, IDisposable
{
	[Inject] TimestampService TimestampService { get; set; } = default!;
	[Inject] UIStateService UIState { get; set; } = default!;
	[Inject] ChatService ChatService { get; set; } = default!;
	[Inject] IJSRuntime JSRuntime { get; set; } = default!;

	DotNetObjectReference<SessionPannel>? _dotNetHelper;
	bool _showDeleteDialog = false;
	ChatSession? _sessionToDelete;
	CreateSessionPopup? _createSessionPopup;

	protected override void OnInitialized()
	{
		ChatService.OnSessionsChanged += OnStateChanged;
		UIState.OnStateChanged += OnStateChanged;
		TimestampService.OnTick += OnTimestampTick;
	}

	void OnStateChanged()
	{
		InvokeAsync(StateHasChanged);
	}

	bool _isLoadingSessions = true;
	protected override async Task OnInitializedAsync()
	{
		_isLoadingSessions = true;
		await ChatService.LoadExistingSessionsAsync();
		_isLoadingSessions = false;
	}

	async Task RefreshSessions()
	{
		_isLoadingSessions = true;
		await ChatService.LoadExistingSessionsAsync();
		_isLoadingSessions = false;
	}

	void OnTimestampTick()
	{
		InvokeAsync(StateHasChanged);
	}

	protected override async Task OnAfterRenderAsync(bool firstRender)
	{
		if(firstRender)
		{
			_dotNetHelper = DotNetObjectReference.Create(this);
			await JSRuntime.InvokeVoidAsync("cockpit.initializeResize", "leftResizeHandle", "leftSidebar", "left", _dotNetHelper);
		}
	}

	[JSInvokable]
	public void OnResize(int width)
	{
		UIState.SetLeftSidebarWidth(width);
	}

	async Task SelectSession(ChatSession session)
	{
		await ChatService.ResumeSessionAsync(session.Id);
	}

	void CreateNewSession()
	{
		try
		{
			_createSessionPopup?.Open();
		}
		catch(Exception ex)
		{
			Console.Error.WriteLine($"Failed to open directory dialog: {ex.Message}");
		}
	}

	static string GetSessionStatusClass(ChatSession session)
	{
		return session.Status switch
		{
			SessionStatus.AgentRunning => "",
			SessionStatus.AgentFinished => "",
			_ => "secondary-text"
		};
	}

	static string GetSessionStatusStyle(ChatSession session)
	{
		return session.Status switch
		{
			SessionStatus.AgentRunning => "color: #FFB900;",
			SessionStatus.AgentFinished => "color: #10893E;",
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
		if(_sessionToDelete != null)
		{
			await ChatService.DeleteSessionAsync(_sessionToDelete.Id);
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
			ChatService.OnSessionsChanged -= OnStateChanged;
			UIState.OnStateChanged -= OnStateChanged;
			TimestampService.OnTick -= OnTimestampTick;
			_dotNetHelper?.Dispose();
		}
	}
}