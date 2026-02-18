using Cockpit.Services;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.Extensions.Logging;
using Microsoft.JSInterop;

namespace Cockpit.Components.Pages.ChatPanel;

public partial class ChatInputArea : ComponentBase, IAsyncDisposable
{
	[Inject] UIStateService _uiState { get; set; } = default!;
	[Inject] UnifiedSessionManager _sessionManager { get; set; } = default!;
	[Inject] IJSRuntime _jsRuntime { get; set; } = default!;
	[Inject] ILogger<ChatInputArea> _logger { get; set; } = default!;

	string _chatInput = string.Empty;
	bool _subscribedToUIState;

	protected override void OnInitialized()
	{
		_sessionManager.OnStateChanged += OnStateChanged;
	}

	protected override async Task OnAfterRenderAsync(bool firstRender)
	{
		if(firstRender)
		{
			await UpdateInputBehavior();
			_uiState.OnStateChanged += OnUIStateChangedHandler;
			_subscribedToUIState = true;
		}
	}

	void OnStateChanged()
	{
		InvokeAsync(StateHasChanged);
	}

	void OnUIStateChangedHandler()
	{
		InvokeAsync(async () =>
		{
			await UpdateInputBehavior();
			StateHasChanged();
		});
	}

	async Task UpdateInputBehavior()
	{
		try
		{
			await _jsRuntime.InvokeVoidAsync("cockpit.setupChatInputBehavior", "chatInput", _uiState.SendOnEnter);
		}
		catch(Exception ex)
		{
			_logger.LogDebug(ex, "Failed to setup chat input behavior");
		}
	}

	async Task OnTextareaInput()
	{
		try
		{
			await _jsRuntime.InvokeVoidAsync("cockpit.autoResizeTextarea", "chatInput");
		}
		catch(Exception ex)
		{
			_logger.LogDebug(ex, "Failed to resize chat input");
		}
	}

	async Task SendMessage()
	{
		if(string.IsNullOrWhiteSpace(_chatInput))
		{
			return;
		}

		string message = _chatInput.Trim();
		_chatInput = string.Empty;

		// Reset textarea height after clearing
		await Task.Delay(10);
		await OnTextareaInput();

		// Send via SDK
		await _sessionManager.SendMessageAsync(message);
	}

	async Task HandleKeyDown(KeyboardEventArgs e)
	{
		if(e.Key == "Enter" && !e.ShiftKey && _uiState.SendOnEnter)
		{
			// Prevent default to avoid adding newline
			await SendMessage();
		}
	}

	public ValueTask DisposeAsync()
	{
		_sessionManager.OnStateChanged -= OnStateChanged;

		if(_subscribedToUIState)
		{
			_uiState.OnStateChanged -= OnUIStateChangedHandler;
		}

		return ValueTask.CompletedTask;
	}
}
