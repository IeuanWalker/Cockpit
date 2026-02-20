using Cockpit.Features.TextToSpeech;
using Cockpit.Services;
using Microsoft.AspNetCore.Components;
using Microsoft.JSInterop;

namespace Cockpit.Components.Pages.ChatPanel;

public partial class ChatMessages : ComponentBase, IAsyncDisposable
{
	[Inject] UnifiedSessionManager _sessionManager { get; set; } = default!;
	[Inject] IJSRuntime _jsRuntime { get; set; } = default!;
	[Inject] TextToSpeechFeature _textToSpeechService { get; set; } = default!;
	[Inject] UIStateService _uiState { get; set; } = default!;

	DotNetObjectReference<ChatMessages>? _dotNetRef;
	bool _isScrolledUp = false;

	string? _previousSessionId;

	protected override void OnInitialized()
	{
		_sessionManager.OnStateChanged += OnStateChanged;
		_textToSpeechService.OnStateChanged += OnStateChanged;
		_uiState.OnStateChanged += OnStateChanged;
		_previousSessionId = _sessionManager.CurrentSession?.Id;
	}

	protected override async Task OnAfterRenderAsync(bool firstRender)
	{
		if(firstRender)
		{
			_dotNetRef = DotNetObjectReference.Create(this);
			await _jsRuntime.InvokeVoidAsync("cockpit.setupScrollAnchor", "chatMessages");
			await _jsRuntime.InvokeVoidAsync("cockpit.setupSmartScroll", "chatMessages", _dotNetRef, "OnChatScrollPositionChanged");
		}
	}

	[JSInvokable]
	public void OnChatScrollPositionChanged(bool isNearBottom)
	{
		_isScrolledUp = !isNearBottom;
		InvokeAsync(StateHasChanged);
	}

	async Task ScrollToBottom()
	{
		_isScrolledUp = false;
		await _jsRuntime.InvokeVoidAsync("cockpit.scrollToBottom", "chatMessages");
	}

	void OnStateChanged()
	{
		_ = InvokeAsync(async () =>
		{
			string? currentSessionId = _sessionManager.CurrentSession?.Id;
			if(currentSessionId != _previousSessionId)
			{
				_previousSessionId = currentSessionId;
				await _textToSpeechService.StopAsync();
			}

			StateHasChanged();
		});
	}

	public async ValueTask DisposeAsync()
	{
		_sessionManager.OnStateChanged -= OnStateChanged;
		_textToSpeechService.OnStateChanged -= OnStateChanged;
		_uiState.OnStateChanged -= OnStateChanged;

		await _textToSpeechService.StopAsync();

		try
		{
			await _jsRuntime.InvokeVoidAsync("cockpit.cleanupScrollAnchor", "chatMessages");
			await _jsRuntime.InvokeVoidAsync("cockpit.cleanupSmartScroll", "chatMessages");
		}
		catch { /* component may be gone */ }
		_dotNetRef?.Dispose();
		GC.SuppressFinalize(this);
	}
}
