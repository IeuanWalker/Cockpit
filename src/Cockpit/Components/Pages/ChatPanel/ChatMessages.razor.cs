using Blazor.Sonner.Services;
using Cockpit.Features.SessionEvents.Models;
using Cockpit.Features.Sessions;
using Cockpit.Features.TextToSpeech;
using Cockpit.Features.UIState;
using Microsoft.AspNetCore.Components;
using Microsoft.JSInterop;

namespace Cockpit.Components.Pages.ChatPanel;

public partial class ChatMessages : ComponentBase, IAsyncDisposable
{
	[Inject] SessionListFeature _sessionManager { get; set; } = default!;
	[Inject] IJSRuntime _jsRuntime { get; set; } = default!;
	[Inject] TextToSpeechFeature _textToSpeechFeature { get; set; } = default!;
	[Inject] UIStateFeature _uiState { get; set; } = default!;
	[Inject] ToastService _toastService { get; set; } = default!;

	DotNetObjectReference<ChatMessages>? _dotNetRef;
	bool _isScrolledUp = false;

	string? _previousSessionId;

	protected override void OnInitialized()
	{
		_sessionManager.OnStateChanged += OnStateChanged;
		_textToSpeechFeature.OnStateChanged += OnStateChanged;
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
			try
			{
				string? currentSessionId = _sessionManager.CurrentSession?.Id;
				if(currentSessionId != _previousSessionId)
				{
					_previousSessionId = currentSessionId;
					await _textToSpeechFeature.Stop();
				}
			}
			catch(Exception ex)
			{
				_toastService.Error("Text-to-Speech Error", opts => opts.Description = ex.Message);
			}

			StateHasChanged();
		});
	}

	readonly HashSet<ChatMessageModel> _expandedAttachments = [];

	void ToggleAttachments(ChatMessageModel message)
	{
		if(!_expandedAttachments.Remove(message))
		{
			_expandedAttachments.Add(message);
		}
		StateHasChanged();
	}

	static string GetAttachmentLabel(int imageCount, int fileCount)
	{
		List<string> parts = [];
		if(imageCount > 0)
		{
			parts.Add($"{imageCount} image{(imageCount > 1 ? "s" : "")}");
		}

		if(fileCount > 0)
		{
			parts.Add($"{fileCount} file{(fileCount > 1 ? "s" : "")}");
		}

		return string.Join(", ", parts);
	}

	async Task OpenLightbox(string src, string alt)
	{
		try
		{
			await _jsRuntime.InvokeVoidAsync("cockpit.showImageLightbox", src, alt);
		}
		catch { /* ignore if JS unavailable */ }
	}

	public async ValueTask DisposeAsync()
	{
		_sessionManager.OnStateChanged -= OnStateChanged;
		_textToSpeechFeature.OnStateChanged -= OnStateChanged;
		_uiState.OnStateChanged -= OnStateChanged;

		await _textToSpeechFeature.Stop();

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
