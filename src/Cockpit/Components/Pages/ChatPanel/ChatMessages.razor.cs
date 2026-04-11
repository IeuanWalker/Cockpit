using System.Text.RegularExpressions;
using Blazor.Sonner.Services;
using Cockpit.Components.Controls;
using Cockpit.Features.Markdown;
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
	readonly SessionFeature _sessionFeature;
	readonly IJSRuntime _jsRuntime;
	readonly TextToSpeechFeature _textToSpeechFeature;
	readonly UIStateFeature _uiStateFeature;
	readonly ToastService _toastService;
	readonly MarkdownFeature _markdownFeature;
	public ChatMessages(
		SessionListFeature sessionListFeature,
		SessionFeature sessionFeature,
		IJSRuntime jsRuntime,
		TextToSpeechFeature textToSpeechFeature,
		UIStateFeature uiStateFeature,
		ToastService toastService,
		MarkdownFeature markdownFeature)
	{
		_sessionListFeature = sessionListFeature;
		_sessionFeature = sessionFeature;
		_jsRuntime = jsRuntime;
		_textToSpeechFeature = textToSpeechFeature;
		_uiStateFeature = uiStateFeature;
		_toastService = toastService;
		_markdownFeature = markdownFeature;
	}

	DotNetObjectReference<ChatMessages>? _dotNetRef;
	bool _isScrolledUp = false;
	bool _pendingScrollToBottom = false;

	string? _previousSessionId;

	EventJsonPopup? _eventJsonPopup;
	List<string>? _eventJsonContent;

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

		if(_pendingScrollToBottom)
		{
			_pendingScrollToBottom = false;
			await ScrollToBottom();
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
					_pendingScrollToBottom = true;
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

	static readonly Regex fileTokenRegex = fileMentionRegex();

	/// <summary>
	/// If the content contains no file tokens, returns null (caller should use MarkdownRenderer component).
	/// If it has file tokens, renders the content with chip spans substituted in, returns HTML.
	/// </summary>
	MarkupString? RenderUserContent(string content)
	{
		if(!content.Contains("#file:\"", StringComparison.Ordinal))
		{
			return null; // use normal MarkdownRenderer
		}

		// Step 1: replace tokens with safe placeholders
		List<(string Placeholder, string ChipHtml)> chips = [];
		int idx = 0;

		string withPlaceholders = fileTokenRegex.Replace(content, m =>
		{
			string filePath = m.Groups[1].Value;
			string fileName = Path.GetFileName(filePath);
			string placeholder = $"COCKPITFILECHIP{idx++}XEND";

			// Build chip HTML (the span that will show in the bubble)
			string escapedPath = System.Net.WebUtility.HtmlEncode(filePath);
			string escapedName = System.Net.WebUtility.HtmlEncode(fileName);
			string chipHtml =
				$"<span class=\"file-mention-chip-readonly\" title=\"{escapedPath}\">" +
				"<svg xmlns=\"http://www.w3.org/2000/svg\" width=\"12\" height=\"12\" fill=\"none\" " +
				"stroke=\"currentColor\" viewBox=\"0 0 24 24\" stroke-width=\"2\" " +
				"stroke-linecap=\"round\" stroke-linejoin=\"round\">" +
				"<path d=\"M9 12h6m-6 4h6m2 5H7a2 2 0 01-2-2V5a2 2 0 012-2h5.586 " +
				"a1 1 0 01.707.293l5.414 5.414a1 1 0 01.293.707V19a2 2 0 01-2 2z\"/>" +
				"</svg>" +
				$" {escapedName}</span>";

			chips.Add((placeholder, chipHtml));
			return placeholder;
		});

		// Step 2: run through Markdig (DisableHtml is fine — our placeholders are plain text)
		string html = _markdownFeature.ToHtml(withPlaceholders);

		// Step 3: replace placeholders with chip HTML
		foreach((string? placeholder, string? chipHtml) in chips)
		{
			html = html.Replace(placeholder, chipHtml);
		}

		return (MarkupString)html;
	}

	async Task OpenLightbox(string src, string alt)
	{
		try
		{
			await _jsRuntime.InvokeVoidAsync("cockpit.showImageLightbox", src, alt);
		}
		catch { /* ignore if JS unavailable */ }
	}

	async Task ShowEventJson(ChatMessageModel message)
	{
		if(await IsTextSelected())
		{
			return;
		}

		_eventJsonContent = message.EventJson?.Select(j => j.Value).ToList();
		_eventJsonPopup?.Open();
	}

	async Task<bool> IsTextSelected()
	{
		string selection = await _jsRuntime.InvokeAsync<string>("eval", "window.getSelection().toString()");
		return !string.IsNullOrEmpty(selection);
	}

	async Task RetryMessage(ChatMessageModel message)
	{
		await _sessionFeature.RetryMessageAsync(message);
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

	[GeneratedRegex(@"#file:""((?:[^""\\]|\\.)*)""", RegexOptions.Compiled)]
	private static partial Regex fileMentionRegex();
}
