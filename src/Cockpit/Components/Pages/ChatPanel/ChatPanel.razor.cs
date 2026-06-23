using Cockpit.Features.Sessions;
using Cockpit.Features.Timestamp;
using Cockpit.Features.UIState;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.Logging;
using Microsoft.JSInterop;

namespace Cockpit.Components.Pages.ChatPanel;

public partial class ChatPanel : ComponentBase, IAsyncDisposable
{
	readonly ITimestampFeature _timestampFeature;
	readonly IUIStateFeature _uiStateFeature;
	readonly SessionFeature _sessionFeature;
	readonly IJSRuntime _jsRuntime;
	readonly ILogger<Main> _logger;
	public ChatPanel(
		ITimestampFeature timestampFeature,
		IUIStateFeature uiStateFeature,
		SessionFeature sessionFeature,
		IJSRuntime jsRuntime,
		ILogger<Main> logger)
	{
		_timestampFeature = timestampFeature;
		_uiStateFeature = uiStateFeature;
		_sessionFeature = sessionFeature;
		_jsRuntime = jsRuntime;
		_logger = logger;
	}

	bool _shouldScrollToBottom = false;
	bool _forcedScrollToBottom = false;
	bool _isUserScrolledUpFromChat = false;
	int _lastMessageCount = 0;
	string? _lastSessionId;
	DotNetObjectReference<ChatPanel>? _dotNetRef;

	protected override async Task OnInitializedAsync()
	{
		_sessionFeature.OnStateChanged += OnStateChanged;
		_uiStateFeature.OnStateChanged += OnStateChanged;
		_timestampFeature.OnTick += OnTimestampTick;

		// Load existing sessions from SDK
		await _sessionFeature.LoadExistingSessions();
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
			_lastMessageCount = _sessionFeature.CurrentSession?.Messages.Count ?? 0;
			_lastSessionId = _sessionFeature.CurrentSession?.Id;
		}

		if(_shouldScrollToBottom && (!_isUserScrolledUpFromChat || _forcedScrollToBottom))
		{
			_shouldScrollToBottom = false;
			_forcedScrollToBottom = false;
			await ScrollToBottom();
		}
	}

	void OnStateChanged()
	{
		string? currentSessionId = _sessionFeature.CurrentSession?.Id;
		int currentMessageCount = _sessionFeature.CurrentSession?.Messages.Count ?? 0;

		if(currentSessionId != _lastSessionId)
		{
			_shouldScrollToBottom = true;
			_forcedScrollToBottom = true;
			_isUserScrolledUpFromChat = false;
		}

		// Only auto-follow chat if there's a new message
		if(currentMessageCount > _lastMessageCount)
		{
			_shouldScrollToBottom = true;
			if(_sessionFeature.CurrentSession?.Messages.LastOrDefault()?.IsUser == true)
			{
				// Always jump to latest when user sends a message
				_isUserScrolledUpFromChat = false;
			}
		}

		_lastSessionId = currentSessionId;
		_lastMessageCount = currentMessageCount;
		InvokeAsync(StateHasChanged);
	}

	async Task ScrollToBottom()
	{
		try
		{
			await _jsRuntime.InvokeVoidAsync("cockpit.scrollToBottom", "chatMessages");
		}
		catch(Exception ex)
		{
			_logger.LogDebug(ex, "Failed to scroll chat messages to bottom");
		}
	}

	async Task SetupSmartScroll()
	{
		try
		{
			await _jsRuntime.InvokeVoidAsync("cockpit.setupSmartScroll", "chatMessages", _dotNetRef, "OnChatScrollPositionChanged", nameof(ChatPanel));
		}
		catch(Exception ex)
		{
			_logger.LogDebug(ex, "Failed to setup smart scroll for chat messages");
		}
	}

	[JSInvokable]
	public void OnChatScrollPositionChanged(bool isNearBottom)
	{
		_isUserScrolledUpFromChat = !isNearBottom;
	}

	void ToggleTerminalPanel()
	{
		if(_sessionFeature.CurrentSession is not null)
		{
			_sessionFeature.CurrentSession.IsTerminalOpen = !_sessionFeature.CurrentSession.IsTerminalOpen;
			StateHasChanged();
		}
	}

	public async ValueTask DisposeAsync()
	{
		_sessionFeature.OnStateChanged -= OnStateChanged;
		_uiStateFeature.OnStateChanged -= OnStateChanged;
		_timestampFeature.OnTick -= OnTimestampTick;

		// Cleanup smart scroll
		try
		{
			await _jsRuntime.InvokeVoidAsync("cockpit.cleanupSmartScroll", "chatMessages", nameof(ChatPanel));
		}
		catch(Exception ex)
		{
			_logger.LogDebug(ex, "Failed to cleanup smart scroll for chat messages");
		}

		_dotNetRef?.Dispose();
		GC.SuppressFinalize(this);
	}

}
