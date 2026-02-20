using Cockpit.Features.Timestamp;
using Cockpit.Services;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.Logging;
using Microsoft.JSInterop;

namespace Cockpit.Components.Pages.ChatPanel;

public partial class ChatPanel : ComponentBase, IAsyncDisposable
{
	[Inject] TimestampFeature _timestampFeature { get; set; } = default!;
	[Inject] UIStateService _uiState { get; set; } = default!;
	[Inject] UnifiedSessionManager _sessionManager { get; set; } = default!;
	[Inject] IJSRuntime _jsRuntime { get; set; } = default!;
	[Inject] ILogger<Main> _logger { get; set; } = default!;

	bool _shouldScrollToBottom = false;
	bool _isUserScrolledUpFromChat = false;
	int _lastMessageCount = 0;
	string? _lastSessionId;
	DotNetObjectReference<ChatPanel>? _dotNetRef;

	protected override async Task OnInitializedAsync()
	{
		_sessionManager.OnStateChanged += OnStateChanged;
		_timestampFeature.OnTick += OnTimestampTick;

		// Load existing sessions from SDK
		await _sessionManager.LoadExistingSessionsAsync();
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
			_lastMessageCount = _sessionManager.CurrentSession?.Messages?.Count ?? 0;
			_lastSessionId = _sessionManager.CurrentSession?.Id;
		}

		if(_shouldScrollToBottom && !_isUserScrolledUpFromChat)
		{
			_shouldScrollToBottom = false;
			await ScrollToBottom();
		}
	}

	void OnStateChanged()
	{
		string? currentSessionId = _sessionManager.CurrentSession?.Id;
		int currentMessageCount = _sessionManager.CurrentSession?.Messages?.Count ?? 0;

		if(currentSessionId != _lastSessionId)
		{
			_shouldScrollToBottom = true;
			_isUserScrolledUpFromChat = false;
		}

		// Only auto-follow chat if there's a new message
		if(currentMessageCount > _lastMessageCount)
		{
			_shouldScrollToBottom = true;
			if(_sessionManager.CurrentSession?.Messages.LastOrDefault()?.IsUser == true)
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
			await _jsRuntime.InvokeVoidAsync("cockpit.setupSmartScroll", "chatMessages", _dotNetRef, "OnChatScrollPositionChanged");
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

	void OpenWorkspaceFolder()
	{
		string? path = _sessionManager.CurrentSession?.WorkspacePath
						?? _sessionManager.CurrentSession?.WorkingDirectory;
		if(string.IsNullOrEmpty(path))
		{
			return;
		}

		try
		{
			System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
			{
				FileName = path,
				UseShellExecute = true
			});
		}
		catch(Exception ex)
		{
			_logger.LogDebug(ex, "Failed to open workspace folder");
		}
	}

	void ToggleTerminalPanel()
	{
		if(_sessionManager.CurrentSession is not null)
		{
			_sessionManager.CurrentSession.IsTerminalOpen = !_sessionManager.CurrentSession.IsTerminalOpen;
			StateHasChanged();
		}
	}

	public async ValueTask DisposeAsync()
	{
		_sessionManager.OnStateChanged -= OnStateChanged;
		_timestampFeature.OnTick -= OnTimestampTick;

		// Cleanup smart scroll
		try
		{
			await _jsRuntime.InvokeVoidAsync("cockpit.cleanupSmartScroll", "chatMessages");
		}
		catch(Exception ex)
		{
			_logger.LogDebug(ex, "Failed to cleanup smart scroll for chat messages");
		}

		_dotNetRef?.Dispose();
	}

}
