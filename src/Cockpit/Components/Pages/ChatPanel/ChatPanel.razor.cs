using Cockpit.Services;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.Logging;
using Microsoft.JSInterop;

namespace Cockpit.Components.Pages.ChatPanel;

public partial class ChatPanel : ComponentBase, IAsyncDisposable
{
	[Inject] TimestampService TimestampService { get; set; } = default!;
	[Inject] UIStateService UIState { get; set; } = default!;
	[Inject] UnifiedSessionManager SessionManager { get; set; } = default!;
	[Inject] IJSRuntime JSRuntime { get; set; } = default!;
	[Inject] ILogger<Main> Logger { get; set; } = default!;

	bool _shouldScrollToBottom = false;
	bool _isUserScrolledUpFromChat = false;
	int _lastMessageCount = 0;
	DotNetObjectReference<ChatPanel>? _dotNetRef;

	protected override async Task OnInitializedAsync()
	{
		SessionManager.OnStateChanged += OnStateChanged;
		TimestampService.OnTick += OnTimestampTick;

		// Load existing sessions from SDK
		await SessionManager.LoadExistingSessionsAsync();
	}

	void OnTimestampTick()
	{
		InvokeAsync(StateHasChanged);
	}

	protected override async Task OnAfterRenderAsync(bool firstRender)
	{
		if(firstRender)
		{
			// Setup smart scroll tracking
			_dotNetRef = DotNetObjectReference.Create(this);
			await SetupSmartScroll();

			// Initialize message count
			_lastMessageCount = SessionManager.CurrentSession?.Messages?.Count ?? 0;
		}

		if(_shouldScrollToBottom && !_isUserScrolledUpFromChat)
		{
			_shouldScrollToBottom = false;
			await ScrollToBottom();
		}
	}

	void OnStateChanged()
	{
		// Only scroll chat if there's a new message
		int currentMessageCount = SessionManager.CurrentSession?.Messages?.Count ?? 0;
		if(currentMessageCount > _lastMessageCount)
		{
			_shouldScrollToBottom = true;
			if(SessionManager.CurrentSession?.Messages.LastOrDefault()?.IsUser == true)
			{
				// Always jump to latest when user sends a message
				_isUserScrolledUpFromChat = false;
			}
			_lastMessageCount = currentMessageCount;
		}

		InvokeAsync(StateHasChanged);
	}

	async Task ScrollToBottom()
	{
		try
		{
			await JSRuntime.InvokeVoidAsync("cockpit.scrollToBottom", "chatMessages");
		}
		catch(Exception ex)
		{
			Logger.LogDebug(ex, "Failed to scroll chat messages to bottom");
		}
	}

	async Task SetupSmartScroll()
	{
		try
		{
			await JSRuntime.InvokeVoidAsync("cockpit.setupSmartScroll", "chatMessages", _dotNetRef, "OnChatScrollPositionChanged");
		}
		catch(Exception ex)
		{
			Logger.LogDebug(ex, "Failed to setup smart scroll for chat messages");
		}
	}

	[JSInvokable]
	public void OnChatScrollPositionChanged(bool isNearBottom)
	{
		_isUserScrolledUpFromChat = !isNearBottom;
	}

	void ToggleTerminalPanel()
	{
		if(SessionManager.CurrentSession is not null)
		{
			SessionManager.CurrentSession.IsTerminalOpen = !SessionManager.CurrentSession.IsTerminalOpen;
			StateHasChanged();
		}
	}

	public async ValueTask DisposeAsync()
	{
		SessionManager.OnStateChanged -= OnStateChanged;
		TimestampService.OnTick -= OnTimestampTick;

		// Cleanup smart scroll
		try
		{
			await JSRuntime.InvokeVoidAsync("cockpit.cleanupSmartScroll", "chatMessages");
		}
		catch(Exception ex)
		{
			Logger.LogDebug(ex, "Failed to cleanup smart scroll for chat messages");
		}

		_dotNetRef?.Dispose();
	}

}
