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
	readonly ITextToSpeechFeature _textToSpeechFeature;
	readonly IUIStateFeature _uiStateFeature;
	readonly ToastService _toastService;
	readonly IMarkdownFeature _markdownFeature;
	public ChatMessages(
		SessionListFeature sessionListFeature,
		SessionFeature sessionFeature,
		IJSRuntime jsRuntime,
		ITextToSpeechFeature textToSpeechFeature,
		IUIStateFeature uiStateFeature,
		ToastService toastService,
		IMarkdownFeature markdownFeature)
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
	ElementReference _chatMessagesElement;
	readonly MessageWindowState _window = new();
	readonly MessageWindowTailStateSync _windowTailStateSync = new();
	bool _isScrolledUp = false;
	bool _pendingScrollToBottom = false;
	bool _shiftInProgress;
	bool _pendingWindowShiftRestore;
	bool _observersReady;
	bool _pendingObserverReset;
	MessageViewportAnchor? _pendingViewportAnchor;
	long _windowGeneration;
	long _pendingWindowShiftGeneration;
	string? _windowLoadError;
	string? _expandedActivityGroupId;
	readonly HashSet<ChatMessageModel> _observedLocallySentMessages = [];

	string? _previousSessionId;

	EventJsonPopup? _eventJsonPopup;
	List<string>? _eventJsonContent;

	protected override void OnInitialized()
	{
		_sessionListFeature.OnSessionStateChanged += OnSessionStateChanged;
		_textToSpeechFeature.OnStateChanged += OnAuxiliaryStateChanged;
		_uiStateFeature.OnStateChanged += OnAuxiliaryStateChanged;
		_previousSessionId = _sessionListFeature.CurrentSession?.Id;
		_window.Reset(CurrentMessages.Count);
		ChatMessageWindowUpdatePolicy.ResetObservedLocallySentMessages(
			_observedLocallySentMessages,
			CurrentMessages);
	}

	protected override async Task OnAfterRenderAsync(bool firstRender)
	{
		if(firstRender)
		{
			_dotNetRef = DotNetObjectReference.Create(this);
			await _jsRuntime.InvokeVoidAsync("cockpit.setupScrollAnchor", _chatMessagesElement);
			await _jsRuntime.InvokeVoidAsync("cockpit.setupSmartScroll", _chatMessagesElement, _dotNetRef, "OnChatScrollPositionChanged", nameof(ChatMessages));
			await SetupWindowObservers();

			// If the app loaded an existing session before this component initialized,
			// ensure the initial view is pinned to the bottom so history shows latest messages.
			if(!_pendingScrollToBottom && _sessionListFeature.CurrentSession?.Conversation.MessagesSnapshot.Length > 0)
			{
				await ScrollToBottom();
			}
		}

		// Synchronize before yielding to scroll/anchor work. Mutation and resize
		// observers defer reconciliation to an animation frame, so this prevents
		// them from treating a newly historical window as if it still held the tail.
		if(!_shiftInProgress)
		{
			await SynchronizeWindowTailState();
		}

		if(_pendingScrollToBottom)
		{
			_pendingScrollToBottom = false;
			await ScrollToBottom();
		}

		if(_pendingWindowShiftRestore)
		{
			long generation = _pendingWindowShiftGeneration;
			_pendingWindowShiftRestore = false;
			MessageViewportAnchor? anchor = _pendingViewportAnchor;
			_pendingViewportAnchor = null;
			if(generation != _windowGeneration)
			{
				return;
			}
			try
			{
				if(anchor is not null)
				{
					await _jsRuntime.InvokeVoidAsync(
						"cockpit.restoreMessageWindowAnchor", _chatMessagesElement, anchor, generation);
				}
				if(generation == _windowGeneration && _observersReady)
				{
					_windowLoadError = null;
				}
			}
			catch(Exception ex)
			{
				if(generation == _windowGeneration)
				{
					_windowLoadError = ex.Message;
					StateHasChanged();
				}
			}
			finally
			{
				if(generation == _windowGeneration)
				{
					_shiftInProgress = false;
					await CompleteWindowShift(generation);
				}
			}
		}

		if(_pendingObserverReset)
		{
			_pendingObserverReset = false;
			await SetupWindowObservers();
		}
	}

	[JSInvokable]
	public void OnChatScrollPositionChanged(bool isNearBottom)
	{
		bool isAtConversationTail = isNearBottom && _window.IncludesTail;
		_window.SetFollowingTail(isAtConversationTail);
		_isScrolledUp = !isAtConversationTail;
		InvokeAsync(StateHasChanged);
	}

	void ReturnToTail()
	{
		InvalidateWindowGeneration();
		_window.MoveToTail();
		_pendingObserverReset = true;
		_pendingScrollToBottom = true;
		StateHasChanged();
	}

	async Task ScrollToBottom()
	{
		_isScrolledUp = false;
		await _jsRuntime.InvokeVoidAsync("cockpit.scrollToBottom", _chatMessagesElement);
	}

	void OnSessionStateChanged(SessionStateChange change)
	{
		string? currentSessionId = _sessionListFeature.CurrentSession?.Id;
		if(!ChatMessageStateChangeFilter.IsRelevant(currentSessionId, _previousSessionId, change))
		{
			return;
		}

		_ = InvokeAsync(async () =>
		{
			try
			{
				currentSessionId = _sessionListFeature.CurrentSession?.Id;
				bool sessionChanged = currentSessionId != _previousSessionId;
				if(sessionChanged)
				{
					_previousSessionId = currentSessionId;
					ResetForCurrentSession();
					_pendingScrollToBottom = true;
					await _textToSpeechFeature.Stop();
				}
				else if(change.SessionId is null || change.SessionId == currentSessionId)
				{
					if(ChatMessageWindowUpdatePolicy.RequiresConversationReset(change.Kind))
					{
						// A replay clear and its first event can be coalesced. Reset against the
						// current (possibly already non-zero) snapshot instead of depending on an
						// intermediate empty render to restore tail-following state.
						ResetForCurrentSession();
						_pendingScrollToBottom = true;
					}
					else if((change.Kind & SessionChangeKind.ConversationStructure) != 0)
					{
						bool wasFollowingTail = _window.IsFollowingTail;
						bool revealLocallySentMessage = ChatMessageWindowUpdatePolicy.ObserveNewLocallySentMessages(
							_observedLocallySentMessages,
							CurrentMessages);
						_window.Synchronize(CurrentMessages.Count);
						if(revealLocallySentMessage)
						{
							InvalidateWindowGeneration();
							_window.MoveToTail();
							_pendingObserverReset = true;
							_pendingScrollToBottom = true;
						}
						else if(wasFollowingTail)
						{
							_pendingScrollToBottom = true;
						}
					}
				}
			}
			catch(Exception ex)
			{
				_toastService.Error("Text-to-Speech Error", opts => opts.Description = ex.Message);
			}

			StateHasChanged();
		});
	}

	void OnAuxiliaryStateChanged() => _ = InvokeAsync(StateHasChanged);

	IReadOnlyList<ChatMessageModel> CurrentMessages =>
		_sessionListFeature.CurrentSession?.Conversation.MessagesSnapshot ?? [];

	IEnumerable<ChatMessageModel> VisibleMessages
	{
		get
		{
			IReadOnlyList<ChatMessageModel> messages = CurrentMessages;
			int start = Math.Min(_window.StartIndex, messages.Count);
			int count = Math.Min(_window.Count, messages.Count - start);
			return messages.Skip(start).Take(count);
		}
	}

	void ResetForCurrentSession()
	{
		InvalidateWindowGeneration();
		_window.Reset(CurrentMessages.Count);
		_expandedAttachments.Clear();
		_expandedActivityGroupId = null;
		_pendingObserverReset = true;
		_isScrolledUp = false;
		_windowLoadError = null;
		ChatMessageWindowUpdatePolicy.ResetObservedLocallySentMessages(
			_observedLocallySentMessages,
			CurrentMessages);
	}

	void InvalidateWindowGeneration()
	{
		unchecked
		{
			_windowGeneration++;
		}
		_observersReady = false;
		_shiftInProgress = false;
		_pendingViewportAnchor = null;
		_pendingWindowShiftRestore = false;
		_pendingWindowShiftGeneration = 0;
	}

	[JSInvokable]
	public Task OnMessageWindowBoundaryReached(
		string direction,
		MessageViewportAnchor? anchor,
		long generation)
		=> generation == _windowGeneration
			? ShiftWindow(direction, anchor, generation)
			: Task.CompletedTask;

	async Task ShiftWindow(string direction, MessageViewportAnchor? anchor, long generation)
	{
		if(generation != _windowGeneration || _shiftInProgress)
		{
			return;
		}

		_shiftInProgress = true;
		bool shifted = direction switch
		{
			"older" => _window.MoveOlder(),
			"newer" => _window.MoveNewer(),
			_ => false
		};

		if(!shifted)
		{
			_shiftInProgress = false;
			await CompleteWindowShift(generation);
			return;
		}

		if(_observersReady)
		{
			try
			{
				await _jsRuntime.InvokeVoidAsync(
					"cockpit.beginMessageWindowShift", _chatMessagesElement, generation);
			}
			catch
			{
				// The bounded render still works; smart-scroll setup can recover on retry.
			}
		}

		if(generation != _windowGeneration)
		{
			return;
		}

		_pendingViewportAnchor = anchor;
		_pendingWindowShiftRestore = true;
		_pendingWindowShiftGeneration = generation;
		if(direction == "older")
		{
			_isScrolledUp = true;
		}
		await InvokeAsync(StateHasChanged);
	}

	async Task SetupWindowObservers()
	{
		long generation;
		unchecked
		{
			generation = ++_windowGeneration;
		}
		_observersReady = false;
		bool includesTail = _window.IncludesTail;
		try
		{
			bool observersReady = await _jsRuntime.InvokeAsync<bool>(
				"cockpit.setupMessageWindow",
				_chatMessagesElement,
				_dotNetRef,
				nameof(OnMessageWindowBoundaryReached),
				includesTail,
				generation);
			if(generation != _windowGeneration)
			{
				return;
			}
			_observersReady = observersReady;
			_windowLoadError = observersReady ? null : "The message viewport is unavailable.";
			if(observersReady)
			{
				_windowTailStateSync.MarkPublished(new(generation, includesTail));
			}
		}
		catch(Exception ex)
		{
			if(generation == _windowGeneration)
			{
				_observersReady = false;
				_windowLoadError = ex.Message;
				StateHasChanged();
			}
		}
	}

	async Task SynchronizeWindowTailState()
	{
		if(!_windowTailStateSync.TryCreateUpdate(
			_observersReady,
			_windowGeneration,
			_window.IncludesTail,
			out MessageWindowTailStateUpdate update))
		{
			return;
		}

		try
		{
			bool applied = await _jsRuntime.InvokeAsync<bool>(
				"cockpit.setMessageWindowTailState",
				_chatMessagesElement,
				update.IncludesTail,
				update.Generation);
			if(applied &&
				update.Generation == _windowGeneration &&
				update.IncludesTail == _window.IncludesTail)
			{
				_windowTailStateSync.MarkPublished(update);
			}
			else if(!applied && update.Generation == _windowGeneration)
			{
				_observersReady = false;
				_windowLoadError = "The message viewport is unavailable.";
				StateHasChanged();
			}
		}
		catch(Exception ex)
		{
			if(update.Generation == _windowGeneration)
			{
				// Disable automatic retries until the explicit fallback retry runs;
				// otherwise each error render would enqueue another failed interop call.
				_observersReady = false;
				_windowLoadError = ex.Message;
				StateHasChanged();
			}
		}
	}

	async Task CompleteWindowShift(long generation)
	{
		if(generation != _windowGeneration || !_observersReady)
		{
			return;
		}

		bool includesTail = _window.IncludesTail;
		try
		{
			bool applied = await _jsRuntime.InvokeAsync<bool>(
				"cockpit.completeMessageWindowShift",
				_chatMessagesElement,
				includesTail,
				generation);
			if(applied && generation == _windowGeneration && includesTail == _window.IncludesTail)
			{
				_windowTailStateSync.MarkPublished(new(generation, includesTail));
			}
		}
		catch(Exception ex)
		{
			if(generation == _windowGeneration)
			{
				_windowLoadError = ex.Message;
				StateHasChanged();
			}
		}
	}

	async Task RetryWindowObservers()
	{
		await SetupWindowObservers();
		StateHasChanged();
	}

	Task LoadEarlierMessages() => ManuallyShiftWindow("older");

	Task LoadNewerMessages() => ManuallyShiftWindow("newer");

	async Task ManuallyShiftWindow(string direction)
	{
		long generation = _windowGeneration;
		MessageViewportAnchor? anchor = null;
		try
		{
			anchor = await _jsRuntime.InvokeAsync<MessageViewportAnchor?>(
				"cockpit.captureMessageWindowAnchor",
				_chatMessagesElement);
		}
		catch
		{
			// The bounded shift still works if viewport anchoring is unavailable.
		}

		await ShiftWindow(direction, anchor, generation);
	}

	void SetActivityGroupExpanded(string groupId, bool expanded)
	{
		_expandedActivityGroupId = expanded ? groupId : null;
		StateHasChanged();
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

	static string GetAttachmentLabel(int imageCount, int fileCount, int folderCount)
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

		if(folderCount > 0)
		{
			parts.Add($"{folderCount} folder{(folderCount > 1 ? "s" : "")}");
		}

		return string.Join(", ", parts);
	}

	const string fileIconPathData = "M9 12h6m-6 4h6m2 5H7a2 2 0 01-2-2V5a2 2 0 012-2h5.586a1 1 0 01.707.293l5.414 5.414a1 1 0 01.293.707V19a2 2 0 01-2 2z";
	const string folderIconPathData = "M3 7a2 2 0 012-2h4l2 2h6a2 2 0 012 2v8a2 2 0 01-2 2H5a2 2 0 01-2-2V7z";
	const string fileIconColor = "#60a5fa";
	const string folderIconColor = "#f59e0b";

	/// <summary>
	/// If the content contains no file/folder tokens, returns null (caller should use MarkdownRenderer component).
	/// If it has mention tokens, renders the content with chip spans substituted in, returns HTML.
	/// </summary>
	MarkupString? RenderUserContent(string content)
	{
		if(!content.Contains("#file:\"", StringComparison.Ordinal) &&
		   !content.Contains("#folder:\"", StringComparison.Ordinal))
		{
			return null; // use normal MarkdownRenderer
		}

		// Step 1: replace tokens with safe placeholders
		List<(string Placeholder, string ChipHtml)> chips = [];
		int idx = 0;

		string withPlaceholders = mentionRegex().Replace(content, m =>
		{
			string mentionType = m.Groups[1].Value;
			string mentionPath = m.Groups[2].Value;
			bool isDirectory = mentionType.Equals("folder", StringComparison.Ordinal);

			string trimmedPath = mentionPath.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
			string mentionName = Path.GetFileName(trimmedPath);
			if(string.IsNullOrWhiteSpace(mentionName))
			{
				mentionName = mentionPath;
			}

			string placeholder = $"COCKPITFILECHIP{idx++}XEND";

			// Build chip HTML (the span that will show in the bubble)
			string escapedPath = System.Net.WebUtility.HtmlEncode(mentionPath);
			string escapedName = System.Net.WebUtility.HtmlEncode(mentionName);
			string iconPath = isDirectory ? folderIconPathData : fileIconPathData;
			string iconColor = isDirectory ? folderIconColor : fileIconColor;
			string chipHtml =
				$"<span class=\"file-mention-chip-readonly\" title=\"{escapedPath}\">" +
				"<svg xmlns=\"http://www.w3.org/2000/svg\" width=\"12\" height=\"12\" fill=\"none\" " +
				"stroke=\"currentColor\" viewBox=\"0 0 24 24\" stroke-width=\"2\" " +
				$"style=\"color: {iconColor};\" " +
				"stroke-linecap=\"round\" stroke-linejoin=\"round\">" +
				$"<path d=\"{iconPath}\"/>" +
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

	async Task CopyUserMessage(ChatMessageModel message)
	{
		try
		{
			await _jsRuntime.InvokeVoidAsync("navigator.clipboard.writeText", message.Content);
			_toastService.Success("Copied", opts => opts.Description = "Message copied to clipboard");
		}
		catch
		{
			_toastService.Error("Copy failed", opts => opts.Description = "Could not copy to clipboard");
		}
	}

	static string HumanizeTimestamp(DateTimeOffset timestamp)
	{
		TimeSpan elapsed = DateTimeOffset.UtcNow - timestamp;

		if(elapsed.TotalSeconds < 60)
		{
			return "just now";
		}

		if(elapsed.TotalMinutes < 60)
		{
			int minutes = (int)elapsed.TotalMinutes;
			return $"{minutes} minute{(minutes == 1 ? "" : "s")} ago";
		}

		if(elapsed.TotalHours < 24)
		{
			int hours = (int)elapsed.TotalHours;
			return $"{hours} hour{(hours == 1 ? "" : "s")} ago";
		}

		if(elapsed.TotalDays < 2)
		{
			return $"yesterday at {timestamp.LocalDateTime:h:mm tt}";
		}

		return timestamp.LocalDateTime.ToString("MMM d, h:mm tt");
	}

	public async ValueTask DisposeAsync()
	{
		InvalidateWindowGeneration();
		_sessionListFeature.OnSessionStateChanged -= OnSessionStateChanged;
		_textToSpeechFeature.OnStateChanged -= OnAuxiliaryStateChanged;
		_uiStateFeature.OnStateChanged -= OnAuxiliaryStateChanged;

		await _textToSpeechFeature.Stop();

		try { await _jsRuntime.InvokeVoidAsync("cockpit.cleanupScrollAnchor", _chatMessagesElement); }
		catch { /* component may be gone */ }
		try { await _jsRuntime.InvokeVoidAsync("cockpit.cleanupSmartScroll", _chatMessagesElement, nameof(ChatMessages)); }
		catch { /* component may be gone */ }
		try { await _jsRuntime.InvokeVoidAsync("cockpit.cleanupMessageWindow", _chatMessagesElement); }
		catch { /* component may be gone */ }
		_dotNetRef?.Dispose();
		GC.SuppressFinalize(this);
	}

	[GeneratedRegex(@"#(file|folder):""((?:[^""\\]|\\.)*)""", RegexOptions.Compiled)]
	private static partial Regex mentionRegex();
}

static class ChatMessageStateChangeFilter
{
	public static bool IsRelevant(string? currentSessionId, string? previousSessionId, SessionStateChange change)
	{
		if(currentSessionId != previousSessionId)
		{
			return (change.Kind & SessionChangeKind.CurrentSession) != 0;
		}

		SessionChangeKind relevantKinds = SessionChangeKind.ConversationContent | SessionChangeKind.ConversationStructure | SessionChangeKind.ConversationReset;
		return (change.Kind & relevantKinds) != 0 && (change.SessionId is null || change.SessionId == currentSessionId);
	}
}

static class ChatMessageWindowUpdatePolicy
{
	public static bool RequiresConversationReset(SessionChangeKind kind) => (kind & SessionChangeKind.ConversationReset) != 0;

	public static void ResetObservedLocallySentMessages(HashSet<ChatMessageModel> observed, IReadOnlyList<ChatMessageModel> messages)
	{
		observed.Clear();
		foreach(ChatMessageModel message in messages)
		{
			if(message.IsUser && message.WasSentLocally)
			{
				observed.Add(message);
			}
		}
	}

	public static bool ObserveNewLocallySentMessages(HashSet<ChatMessageModel> observed, IReadOnlyList<ChatMessageModel> messages)
	{
		bool foundNew = false;
		for(int i = messages.Count - 1; i >= 0; i--)
		{
			ChatMessageModel message = messages[i];
			if(!message.IsUser || !message.WasSentLocally)
			{
				continue;
			}

			if(!observed.Add(message))
			{
				break;
			}
			foundNew = true;
		}
		return foundNew;
	}
}
