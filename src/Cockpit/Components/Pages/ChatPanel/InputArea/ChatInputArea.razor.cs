using Blazor.Sonner.Services;
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
	readonly UIStateFeature _uiStateFeature;
	readonly SessionFeature _sessionFeature;
	readonly IJSRuntime _jsRuntime;
	readonly ToastService _toastService;
	readonly ILogger<ChatInputArea> _logger;

	public ChatInputArea(
		UIStateFeature uiStateFeature,
		SessionFeature sessionFeature,
		IJSRuntime jsRuntime,
		ToastService toastService,
		ILogger<ChatInputArea> logger)
	{
		_uiStateFeature = uiStateFeature;
		_sessionFeature = sessionFeature;
		_jsRuntime = jsRuntime;
		_toastService = toastService;
		_logger = logger;
	}

	// Brief yield to allow Blazor to flush the binding update before resizing
	const int textareaResizeYieldMs = 10;
	const long maxImagePreviewBytes = 10 * 1024 * 1024; // 10 MB
	bool _subscribedToUIState;
	DotNetObjectReference<ChatInputArea>? _dotNetRef;
	bool _pastSetup;
	string? _lastSessionId;

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
		InvokeAsync(async () =>
		{
			string? currentSessionId = _sessionFeature.CurrentSession?.Id;
			bool sessionChanged = currentSessionId != _lastSessionId;
			_lastSessionId = currentSessionId;

			StateHasChanged();

			if(sessionChanged)
			{
				await Task.Delay(textareaResizeYieldMs);
				await OnTextareaInput();
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
			await _jsRuntime.InvokeVoidAsync("cockpit.setupChatInputBehavior", "chatInput", _uiStateFeature.SendOnEnter);
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

	void RemoveAttachment(int index)
	{
		SessionModel? session = _sessionFeature.CurrentSession;
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

	bool CanSend => !string.IsNullOrWhiteSpace(UserInput) && _sessionFeature.CurrentSession?.PendingPermissionRequests?.Count == 0;

	async Task SendMessage()
	{
		if(!CanSend)
		{
			return;
		}

		SessionModel? session = _sessionFeature.CurrentSession;
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

		await _sessionFeature.SendMessageAsync(message, attachments);
	}

	async Task HandleKeyDown(KeyboardEventArgs e)
	{
		if(e.Key == "Enter" && !e.ShiftKey && _uiStateFeature.SendOnEnter)
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
		_sessionFeature.OnStateChanged -= OnStateChanged;
		_uiStateFeature.OnAppendChatInput -= OnAppendChatInput;

		if(_subscribedToUIState)
		{
			_uiStateFeature.OnStateChanged -= OnUIStateChangedHandler;
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
		if(_sessionFeature.CurrentSession is null)
		{
			return;
		}

		_sessionFeature.CurrentSession.IsYolo = !_sessionFeature.CurrentSession.IsYolo;
		StateHasChanged();
	}
}
