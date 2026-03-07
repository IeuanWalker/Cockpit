using Cockpit.Features.Sessions.Models;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.Logging;
using Microsoft.JSInterop;

namespace Cockpit.Components.Pages.ChatPanel.InputArea;

public partial class AttachmentsControl : ComponentBase
{
	readonly IJSRuntime _jsRuntime;
	readonly ILogger<AttachmentsControl> _logger;
	[Parameter] public SessionModel? Session { get; set; }

	public AttachmentsControl(
		IJSRuntime jsRuntime,
		ILogger<AttachmentsControl> logger)
	{
		_jsRuntime = jsRuntime;
		_logger = logger;
	}

	IReadOnlyList<AttachmentModel> AttachmentsSnapshot
	{
		get
		{
			if(Session is null)
			{
				return [];
			}

			lock(Session.PendingAttachmentsLock)
			{
				return [.. Session.PendingAttachments];
			}
		}
	}

	void RemoveAttachment(int index)
	{
		SessionModel? session = Session;
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

		StateHasChanged();

		// Only delete if it's a session-owned file (pasted images saved to Cockpit\Files\)
		// Never delete user's original files selected via the file picker
		string sessionFilesDir = Path.GetFullPath(GetSessionFilesPath(session)) + Path.DirectorySeparatorChar;
		string normalizedFilePath = Path.GetFullPath(filePath);
		bool isSessionOwned = normalizedFilePath.StartsWith(sessionFilesDir, StringComparison.OrdinalIgnoreCase);

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
}
