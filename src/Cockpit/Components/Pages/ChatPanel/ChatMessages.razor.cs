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
	readonly SessionListFeature _sessionListFeature;
	readonly IJSRuntime _jsRuntime;
	readonly TextToSpeechFeature _textToSpeechFeature;
	readonly UIStateFeature _uiStateFeature;
	readonly ToastService _toastService;
	public ChatMessages(
		SessionListFeature sessionListFeature,
		IJSRuntime jsRuntime,
		TextToSpeechFeature textToSpeechFeature,
		UIStateFeature uiStateFeature,
		ToastService toastService)
	{
		_sessionListFeature = sessionListFeature;
		_jsRuntime = jsRuntime;
		_textToSpeechFeature = textToSpeechFeature;
		_uiStateFeature = uiStateFeature;
		_toastService = toastService;
	}

	DotNetObjectReference<ChatMessages>? _dotNetRef;
	bool _isScrolledUp = false;

	string? _previousSessionId;

	protected override void OnInitialized()
	{
		_sessionListFeature.OnStateChanged += OnStateChanged;
		_textToSpeechFeature.OnStateChanged += OnStateChanged;
		_uiStateFeature.OnStateChanged += OnStateChanged;
		_previousSessionId = _sessionListFeature.CurrentSession?.Id;
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
				string? currentSessionId = _sessionListFeature.CurrentSession?.Id;
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
		_sessionListFeature.OnStateChanged -= OnStateChanged;
		_textToSpeechFeature.OnStateChanged -= OnStateChanged;
		_uiStateFeature.OnStateChanged -= OnStateChanged;

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
