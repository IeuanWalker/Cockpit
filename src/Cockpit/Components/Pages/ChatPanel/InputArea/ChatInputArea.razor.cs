using Cockpit.Features.Sessions;
using Cockpit.Features.Sessions.Models;
using Cockpit.Features.UIState;
using Cockpit.Utilities;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.Extensions.Logging;
using Microsoft.JSInterop;

namespace Cockpit.Components.Pages.ChatPanel;

public partial class ChatInputArea : ComponentBase, IAsyncDisposable
{
	[Inject] UIStateFeature _uiState { get; set; } = default!;
	[Inject] SessionFeature _sessionManager { get; set; } = default!;
	[Inject] IJSRuntime _jsRuntime { get; set; } = default!;
	[Inject] ILogger<ChatInputArea> _logger { get; set; } = default!;

	// Brief yield to allow Blazor to flush the binding update before resizing
	const int textareaResizeYieldMs = 10;
	bool _subscribedToUIState;
	DotNetObjectReference<ChatInputArea>? _dotNetRef;
	bool _pastSetup;

	string UserInput
	{
		get => _sessionManager.CurrentSession?.UserInput ?? string.Empty;
		set
		{
			if(_sessionManager.CurrentSession is not null)
			{
				_sessionManager.CurrentSession.UserInput = value;
			}
		}
	}

	protected override void OnInitialized()
	{
		_sessionManager.OnStateChanged += OnStateChanged;
		_uiState.OnAppendChatInput += OnAppendChatInput;
	}

	protected override async Task OnAfterRenderAsync(bool firstRender)
	{
		if(firstRender)
		{
			await UpdateInputBehavior();
			_uiState.OnStateChanged += OnUIStateChangedHandler;
			_subscribedToUIState = true;

			_dotNetRef = DotNetObjectReference.Create(this);
			await SetupPasteHandlerAsync();
		}
	}

	async Task SetupPasteHandlerAsync()
	{
		try
		{
			await _jsRuntime.InvokeVoidAsync("cockpit.setupImagePaste", "chatInput", _dotNetRef);
			_pastSetup = true;
		}
		catch(Exception ex)
		{
			_logger.LogDebug(ex, "Failed to setup image paste handler");
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

	void OnAppendChatInput(string text)
	{
		InvokeAsync(async () =>
		{
			UserInput = string.IsNullOrEmpty(UserInput) ? text : UserInput + "\n" + text;
			StateHasChanged();
			await Task.Delay(textareaResizeYieldMs);
			await OnTextareaInput();
		});
	}

	[JSInvokable]
	public async Task OnImagePasted(string base64, string mimeType, string ext, string fileName)
	{
		SessionModel? session = _sessionManager.CurrentSession;
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
		lock(session.PendingAttachmentsLock)
		{
			session.PendingAttachments.Add(new AttachmentModel(fileName, filePath, dataUri, mimeType));
		}

		await InvokeAsync(StateHasChanged);
	}

	async Task AttachFiles()
	{
		SessionModel? session = _sessionManager.CurrentSession;
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
					byte[] fileBytes;
					using(Stream sourceStream = await result.OpenReadAsync())
					{
						using MemoryStream ms = new();
						await sourceStream.CopyToAsync(ms);
						fileBytes = ms.ToArray();
					}
					dataUri = $"data:{mimeType};base64,{Convert.ToBase64String(fileBytes)}";
				}

				lock(session.PendingAttachmentsLock)
				{
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

	void RemoveAttachment(int index)
	{
		SessionModel? session = _sessionManager.CurrentSession;
		if(session is null)
		{
			return;
		}

		string filePath;
		lock(session.PendingAttachmentsLock)
		{
			if(index < 0 || index >= session.PendingAttachments.Count)
			{
				return;
			}

			filePath = session.PendingAttachments[index].FilePath;
			session.PendingAttachments.RemoveAt(index);
		}

		// Only delete if it's a session-owned file (pasted images saved to Cockpit\Files\)
		// Never delete user's original files selected via the file picker
		string sessionFilesDir = GetSessionFilesPath(session);
		bool isSessionOwned = filePath.StartsWith(sessionFilesDir, StringComparison.OrdinalIgnoreCase);

		if(isSessionOwned)
		{
			try
			{
				if(File.Exists(filePath))
				{
					File.Delete(filePath);
				}
			}
			catch(Exception ex)
			{
				_logger.LogDebug(ex, "Failed to delete attachment file {FilePath}", filePath);
			}
		}
	}

	bool CanSend => !string.IsNullOrWhiteSpace(UserInput) &&
		_sessionManager.CurrentSession?.PendingPermissionRequests?.Count == 0;

	async Task SendMessage()
	{
		if(!CanSend)
		{
			return;
		}

		SessionModel? session = _sessionManager.CurrentSession;
		bool hasAttachments = session?.PendingAttachments.Count > 0;

		string message = UserInput.Trim();
		UserInput = string.Empty;

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

		await _sessionManager.SendMessageAsync(message, attachments);
	}

	async Task HandleKeyDown(KeyboardEventArgs e)
	{
		if(e.Key == "Enter" && !e.ShiftKey && _uiState.SendOnEnter)
		{
			// Prevent default to avoid adding newline
			await SendMessage();
		}
	}

	async Task OpenLightbox(string src, string alt)
	{
		try
		{
			await _jsRuntime.InvokeVoidAsync("cockpit.showImageLightbox", src, alt);
		}
		catch { /* ignore if JS unavailable */ }
	}

	static string GetSessionFilesPath(SessionModel session)
	{
		string basePath = session.Context.WorkspacePath ?? Path.GetTempPath();
		return Path.Combine(basePath, "Cockpit", "Files");
	}

	public async ValueTask DisposeAsync()
	{
		_sessionManager.OnStateChanged -= OnStateChanged;
		_uiState.OnAppendChatInput -= OnAppendChatInput;

		if(_subscribedToUIState)
		{
			_uiState.OnStateChanged -= OnUIStateChangedHandler;
		}

		if(_pastSetup)
		{
			try
			{
				await _jsRuntime.InvokeVoidAsync("cockpit.cleanupImagePaste", "chatInput");
			}
			catch { /* component may already be detached */ }
		}

		_dotNetRef?.Dispose();
	}

	void ToggleYoloMode()
	{
		if(_sessionManager.CurrentSession is null)
		{
			return;
		}

		_sessionManager.CurrentSession.IsYolo = !_sessionManager.CurrentSession.IsYolo;
		StateHasChanged();
	}
}
