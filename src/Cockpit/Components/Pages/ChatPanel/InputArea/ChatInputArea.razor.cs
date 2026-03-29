using System.Text.Json.Serialization;
using Blazor.Sonner.Services;
using Cockpit.Features.FileSearch;
using Cockpit.Features.Sessions;
using Cockpit.Features.Sessions.Models;
using Cockpit.Features.UIState;
using Cockpit.Utilities;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.Extensions.Logging;
using Microsoft.JSInterop;

namespace Cockpit.Components.Pages.ChatPanel.InputArea;

public partial class ChatInputArea : ComponentBase, IAsyncDisposable
{
	readonly UIStateFeature _uiStateFeature;
	readonly SessionFeature _sessionFeature;
	readonly IJSRuntime _jsRuntime;
	readonly ToastService _toastService;
	readonly ILogger<ChatInputArea> _logger;
	readonly IFileSearchFeature _fileSearchFeature;

	public ChatInputArea(
		UIStateFeature uiStateFeature,
		SessionFeature sessionFeature,
		IJSRuntime jsRuntime,
		ToastService toastService,
		ILogger<ChatInputArea> logger,
		IFileSearchFeature fileSearchFeature)
	{
		_uiStateFeature = uiStateFeature;
		_sessionFeature = sessionFeature;
		_jsRuntime = jsRuntime;
		_toastService = toastService;
		_logger = logger;
		_fileSearchFeature = fileSearchFeature;
	}

	// Brief yield to allow Blazor to flush the binding update before resizing
	const int textareaResizeYieldMs = 10;
	const long maxImagePreviewBytes = 10 * 1024 * 1024; // 10 MB
	bool _subscribedToUIState;
	DotNetObjectReference<ChatInputArea>? _dotNetRef;
	bool _ceSetup;
	string? _lastSessionId;

	// Mention picker state
	bool _showMentionPicker;
	string _mentionFilter = string.Empty;
	IReadOnlyList<FileSearchResult> _mentionFiles = [];
	int _selectedMentionIndex = -1;
	CancellationTokenSource? _mentionSearchCts;

	bool IsInputDisabled =>
		_sessionFeature.CurrentSession?.PendingPermissionRequests?.Count > 0
		|| _sessionFeature.CurrentSession?.PendingUserInputRequests?.Count > 0;

	string UserInput
	{
		get => _sessionFeature.CurrentSession?.UserInput ?? string.Empty;
		set => _sessionFeature.CurrentSession?.UserInput = value;
	}

	protected override void OnInitialized()
	{
		_sessionFeature.OnStateChanged += OnStateChanged;
		_uiStateFeature.OnAppendChatInput += OnAppendChatInput;
	}

	protected override async Task OnAfterRenderAsync(bool firstRender)
	{
		if(firstRender)
		{
			await UpdateInputBehavior();
			_uiStateFeature.OnStateChanged += OnUIStateChangedHandler;
			_subscribedToUIState = true;

			_dotNetRef = DotNetObjectReference.Create(this);
			await SetupContentEditableAsync();
		}
	}

	async Task SetupContentEditableAsync()
	{
		try
		{
			_dotNetRef ??= DotNetObjectReference.Create(this);
			await _jsRuntime.InvokeVoidAsync("cockpit.setupContentEditable", "chatInput", _dotNetRef);
			await _jsRuntime.InvokeVoidAsync("cockpit.setupImagePaste", "chatInput", _dotNetRef);
			_ceSetup = true;
		}
		catch(Exception ex)
		{
			_logger.LogDebug(ex, "Failed to setup content editable");
		}
	}

	void OnStateChanged()
	{
		InvokeAsync(async () =>
		{
			string? currentSessionId = _sessionFeature.CurrentSession?.Id;
			bool sessionChanged = currentSessionId != _lastSessionId;
			_lastSessionId = currentSessionId;

			StateHasChanged();

			if(sessionChanged)
			{
				await Task.Delay(textareaResizeYieldMs);
				await SyncDomFromUserInput();
				await FocusInputAsync();
			}
		});
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
			await _jsRuntime.InvokeVoidAsync("cockpit.setupContentEditableBehavior", "chatInput", _uiStateFeature.SendOnEnter);
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
			await _jsRuntime.InvokeVoidAsync("cockpit.autoResizeContentEditable", "chatInput");
		}
		catch(Exception ex)
		{
			_logger.LogDebug(ex, "Failed to resize chat input");
		}
	}

	async Task SyncUserInputFromDom()
	{
		try
		{
			string text = await _jsRuntime.InvokeAsync<string>("cockpit.getPlainText", "chatInput");
			UserInput = text;
		}
		catch(Exception ex)
		{
			_logger.LogDebug(ex, "Failed to read contenteditable content");
		}
	}

	async Task SyncDomFromUserInput()
	{
		try
		{
			string currentText = UserInput;
			ChipInfo[] chips = await _jsRuntime.InvokeAsync<ChipInfo[]>("cockpit.setPlainText", "chatInput", currentText);

			SessionModel? session = _sessionFeature.CurrentSession;
			if(session is not null)
			{
				lock(session.PendingAttachmentsLock)
				{
					// Always clear stale mention attachments; rebuild from what setPlainText returned
					session.PendingAttachments.RemoveAll(a => a.IsMention);

					foreach(ChipInfo chip in chips)
					{
						string fileName = Path.GetFileName(chip.FilePath);
						string ext = Path.GetExtension(chip.FilePath);
						string mimeType = FileUtil.GetMimeType(ext);
						session.PendingAttachments.Add(new AttachmentModel(
							fileName, chip.FilePath, null, mimeType,
							isMention: true, chipId: chip.ChipId));
					}
				}
			}

			await OnTextareaInput();
		}
		catch(Exception ex)
		{
			_logger.LogDebug(ex, "Failed to set contenteditable content");
		}
	}

	async Task FocusInputAsync()
	{
		try
		{
			await _jsRuntime.InvokeVoidAsync("cockpit.focusElement", "chatInput");
		}
		catch(Exception ex)
		{
			_logger.LogDebug(ex, "Failed to focus chat input");
		}
	}

	void OnAppendChatInput(string text)
	{
		InvokeAsync(async () =>
		{
			// First sync current DOM content
			await SyncUserInputFromDom();

			string newContent = string.IsNullOrEmpty(UserInput) ? text : UserInput + "\n" + text;
			UserInput = newContent;

			// Sync to DOM
			try
			{
				await _jsRuntime.InvokeVoidAsync("cockpit.setPlainText", "chatInput", newContent);
			}
			catch { }

			StateHasChanged();
			await Task.Delay(textareaResizeYieldMs);
			await OnTextareaInput();
		});
	}

	[JSInvokable]
	public async Task OnContentInput()
	{
		try
		{
			// Sync text to session
			await SyncUserInputFromDom();

			// Check for active mention trigger
			string? filter = await _jsRuntime.InvokeAsync<string?>("cockpit.getActiveMentionFilter", "chatInput");

			if(filter is not null)
			{
				_mentionFilter = filter;

				// Cancel any in-flight search and start a new debounce window
				CancellationTokenSource? oldCts = _mentionSearchCts;
				CancellationTokenSource cts = new();
				_mentionSearchCts = cts;
				oldCts?.Cancel();
				oldCts?.Dispose();

				try
				{
					// Debounce: wait before running the (potentially expensive) search
					await Task.Delay(200, cts.Token);

					SessionModel? session = _sessionFeature.CurrentSession;
					string cwd = session?.Context.CurrentWorkingDirectory ?? string.Empty;

					IReadOnlyList<FileSearchResult> searchResults = !string.IsNullOrEmpty(cwd)
						? await _fileSearchFeature.SearchAsync(cwd, filter, cancellationToken: cts.Token)
						: [];

					// Merge in manually-attached files not already in search results
					if(session is not null)
					{
						List<FileSearchResult> combined = [.. searchResults];
						HashSet<string> seenPaths = new(combined.Select(f => f.FullPath), StringComparer.OrdinalIgnoreCase);

						lock(session.PendingAttachmentsLock)
						{
							foreach(AttachmentModel att in session.PendingAttachments)
							{
								if(att.IsMention || seenPaths.Contains(att.FilePath))
								{
									continue;
								}

								string fileName = Path.GetFileName(att.FilePath);
								if(string.IsNullOrEmpty(filter) || fileName.Contains(filter, StringComparison.OrdinalIgnoreCase))
								{
									combined.Add(new FileSearchResult(fileName, att.FilePath, att.FilePath));
									seenPaths.Add(att.FilePath);
								}
							}
						}

						searchResults = combined;
					}

					_mentionFiles = searchResults;

					if(_mentionFiles.Count > 0)
					{
						_showMentionPicker = true;
						_selectedMentionIndex = 0; // auto-select first item
					}
					else
					{
						_showMentionPicker = !string.IsNullOrEmpty(filter); // show "no files" if user typed something
					}
				}
				catch(OperationCanceledException)
				{
					// A newer search superseded this one — leave picker state as-is
					return;
				}
			}
			else
			{
				_showMentionPicker = false;
			}

			await InvokeAsync(StateHasChanged);
			await OnTextareaInput();
		}
		catch(Exception ex)
		{
			_logger.LogDebug(ex, "Error handling content input");
		}
	}

	[JSInvokable]
	public Task OnChipRemoved(string chipId)
	{
		SessionModel? session = _sessionFeature.CurrentSession;
		if(session is null)
		{
			return Task.CompletedTask;
		}

		lock(session.PendingAttachmentsLock)
		{
			int idx = session.PendingAttachments.FindIndex(a => a.ChipId == chipId);
			if(idx >= 0)
			{
				session.PendingAttachments.RemoveAt(idx);
			}
		}

		return InvokeAsync(StateHasChanged);
	}

	async Task OnFileSelectedAsync(FileSearchResult file)
	{
		string chipId = Guid.NewGuid().ToString();

		// Insert chip into DOM
		try
		{
			await _jsRuntime.InvokeVoidAsync("cockpit.insertFileChip", "chatInput", chipId, file.FullPath, file.FileName);
		}
		catch(Exception ex)
		{
			_logger.LogDebug(ex, "Failed to insert file chip");
			CloseMentionPicker();
			return;
		}

		// Add to pending attachments
		SessionModel? session = _sessionFeature.CurrentSession;
		if(session is not null)
		{
			string ext = Path.GetExtension(file.FileName);
			string mimeType = FileUtil.GetMimeType(ext);

			lock(session.PendingAttachmentsLock)
			{
				session.PendingAttachments.Add(new AttachmentModel(
					file.FileName, file.FullPath, null, mimeType,
					isMention: true, chipId: chipId));
			}
		}

		// Sync text content from DOM
		await SyncUserInputFromDom();

		CloseMentionPicker();

		// Re-focus the input
		try
		{
			await _jsRuntime.InvokeVoidAsync("cockpit.focusElement", "chatInput");
		}
		catch { }

		await InvokeAsync(StateHasChanged);
	}

	void CloseMentionPicker()
	{
		_showMentionPicker = false;
		_mentionFilter = string.Empty;
		_selectedMentionIndex = -1;
	}

	async Task OnSpeechInputChangedAsync(string value)
	{
		UserInput = value;
		try
		{
			await _jsRuntime.InvokeVoidAsync("cockpit.setPlainText", "chatInput", value);
		}
		catch { }

		await OnTextareaInput();
		StateHasChanged();
	}

	[JSInvokable]
	public async Task OnImagePasted(string base64, string mimeType, string ext, string fileName)
	{
		SessionModel? session = _sessionFeature.CurrentSession;
		if(session is null)
		{
			return;
		}

		string dir = GetSessionFilesPath(session);
		Directory.CreateDirectory(dir);
		string filePath = Path.Combine(dir, $"{Guid.NewGuid()}.{ext}");
		byte[] bytes;

		try
		{
			bytes = Convert.FromBase64String(base64);
			await File.WriteAllBytesAsync(filePath, bytes);
		}
		catch(Exception ex)
		{
			_logger.LogError(ex, "Failed to save pasted image");
			return;
		}

		string dataUri = $"data:{mimeType};base64,{base64}";
		bool isDuplicate;
		lock(session.PendingAttachmentsLock)
		{
			isDuplicate = session.PendingAttachments.Any(a => a.DataUri == dataUri);
			if(!isDuplicate)
			{
				session.PendingAttachments.Add(new AttachmentModel(fileName, filePath, dataUri, mimeType));
			}
		}

		if(isDuplicate)
		{
			_toastService.Info("Already attached", opts => opts.Description = $"{fileName} is already attached.");
			try { File.Delete(filePath); } catch { /* ignore */ }
		}

		await InvokeAsync(StateHasChanged);
	}

	async Task AttachFiles()
	{
		SessionModel? session = _sessionFeature.CurrentSession;
		if(session is null)
		{
			return;
		}

		try
		{
			PickOptions options = new()
			{
				PickerTitle = "Select images or files",
			};

			IEnumerable<FileResult?>? rawResults = await FilePicker.PickMultipleAsync(options);
			if(rawResults is null)
			{
				return;
			}

			IEnumerable<FileResult> results = rawResults.OfType<FileResult>();

			foreach(FileResult result in results)
			{
				string ext = FileUtil.GetNormalizedExtension(result.FileName);
				string mimeType = result.ContentType ?? FileUtil.GetMimeType(ext);

				// Use the original file path — don't copy it, the user may want the agent to edit it
				string filePath = result.FullPath;

				string? dataUri = null;
				if(mimeType.StartsWith("image/", StringComparison.OrdinalIgnoreCase))
				{
					FileInfo fileInfo = new(filePath);
					if(fileInfo.Exists && fileInfo.Length <= maxImagePreviewBytes)
					{
						byte[] fileBytes;
						using(Stream sourceStream = await result.OpenReadAsync())
						{
							using MemoryStream ms = new();
							await sourceStream.CopyToAsync(ms);
							fileBytes = ms.ToArray();
						}
						dataUri = $"data:{mimeType};base64,{Convert.ToBase64String(fileBytes)}";
					}
					else if(fileInfo.Exists)
					{
						_logger.LogWarning("Image '{FileName}' ({SizeBytes} bytes) exceeds the {LimitBytes}-byte preview limit; preview will not be generated", result.FileName, fileInfo.Length, maxImagePreviewBytes);
					}
				}

				lock(session.PendingAttachmentsLock)
				{
					if(session.PendingAttachments.Any(a => string.Equals(a.FilePath, filePath, StringComparison.OrdinalIgnoreCase)))
					{
						_toastService.Info("Already attached", opts => opts.Description = $"{result.FileName} is already attached.");
						continue;
					}

					session.PendingAttachments.Add(new AttachmentModel(result.FileName, filePath, dataUri, mimeType));
				}
			}

			StateHasChanged();
		}
		catch(Exception ex)
		{
			_logger.LogError(ex, "Failed to pick files");
		}
	}

	bool CanSend => !string.IsNullOrWhiteSpace(UserInput) && _sessionFeature.CurrentSession?.PendingPermissionRequests?.Count == 0;

	async Task SendMessage()
	{
		if(!CanSend)
		{
			return;
		}

		// Sync text from DOM first
		await SyncUserInputFromDom();

		SessionModel? session = _sessionFeature.CurrentSession;
		bool hasAttachments = session?.PendingAttachments.Count > 0;

		string message = UserInput.Trim();
		if(string.IsNullOrWhiteSpace(message))
		{
			return;
		}

		UserInput = string.Empty;

		// Clear DOM
		try
		{
			await _jsRuntime.InvokeVoidAsync("cockpit.setPlainText", "chatInput", string.Empty);
		}
		catch { }

		List<AttachmentModel>? attachments = null;
		if(hasAttachments && session is not null)
		{
			lock(session.PendingAttachmentsLock)
			{
				attachments = [.. session.PendingAttachments];
				session.PendingAttachments.Clear();
			}
		}

		// Reset textarea height after clearing
		await Task.Delay(textareaResizeYieldMs);
		await OnTextareaInput();

		await _sessionFeature.SendMessageAsync(message, attachments);
	}

	async Task HandleKeyDown(KeyboardEventArgs e)
	{
		if(_showMentionPicker)
		{
			switch(e.Key)
			{
				case "ArrowDown":
					_selectedMentionIndex = (_selectedMentionIndex + 1) % Math.Max(1, _mentionFiles.Count);
					await InvokeAsync(StateHasChanged);
					return;
				case "ArrowUp":
					_selectedMentionIndex = (_selectedMentionIndex - 1 + Math.Max(1, _mentionFiles.Count)) % Math.Max(1, _mentionFiles.Count);
					await InvokeAsync(StateHasChanged);
					return;
				case "Enter":
					if(_selectedMentionIndex >= 0 && _selectedMentionIndex < _mentionFiles.Count)
					{
						await OnFileSelectedAsync(_mentionFiles[_selectedMentionIndex]);
					}
					return;
				case "Escape":
					CloseMentionPicker();
					await InvokeAsync(StateHasChanged);
					return;
			}
		}

		if(e.Key == "Enter" && !e.ShiftKey && _uiStateFeature.SendOnEnter)
		{
			await SendMessage();
		}
	}

	static string GetSessionFilesPath(SessionModel session)
	{
		string basePath = session.Context.WorkspacePath ?? Path.GetTempPath();
		return Path.Combine(basePath, "Cockpit", "Files");
	}

	void ToggleYoloMode()
	{
		if(_sessionFeature.CurrentSession is null)
		{
			return;
		}

		_sessionFeature.CurrentSession.IsYolo = !_sessionFeature.CurrentSession.IsYolo;
		StateHasChanged();
	}

	public async ValueTask DisposeAsync()
	{
		_sessionFeature.OnStateChanged -= OnStateChanged;
		_uiStateFeature.OnAppendChatInput -= OnAppendChatInput;

		if(_subscribedToUIState)
		{
			_uiStateFeature.OnStateChanged -= OnUIStateChangedHandler;
		}

		if(_ceSetup)
		{
			try
			{
				await _jsRuntime.InvokeVoidAsync("cockpit.cleanupContentEditable", "chatInput");
				await _jsRuntime.InvokeVoidAsync("cockpit.cleanupImagePaste", "chatInput");
			}
			catch { /* component may already be detached */ }
		}

		_dotNetRef?.Dispose();
		_mentionSearchCts?.Dispose();
	}
}

record ChipInfo(
	[property: JsonPropertyName("chipId")] string ChipId,
	[property: JsonPropertyName("filePath")] string FilePath
);
