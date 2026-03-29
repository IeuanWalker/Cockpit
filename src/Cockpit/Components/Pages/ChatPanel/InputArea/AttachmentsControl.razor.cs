using Blazor.Sonner.Services;
using Cockpit.Features.Sessions.Models;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.Logging;
using Microsoft.JSInterop;

namespace Cockpit.Components.Pages.ChatPanel.InputArea;

public partial class AttachmentsControl : ComponentBase
{
	readonly IJSRuntime _jsRuntime;
	readonly ILogger<AttachmentsControl> _logger;
	readonly ToastService _toastService;
	[Parameter] public SessionModel? Session { get; set; }

	public AttachmentsControl(
		IJSRuntime jsRuntime,
		ILogger<AttachmentsControl> logger,
		ToastService toastService)
	{
		_jsRuntime = jsRuntime;
		_logger = logger;
		_toastService = toastService;
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
				// Deduplicate by file path across all attachment types.
				// Non-mention (manually added) entries take priority so the user can remove them.
				HashSet<string> seen = new(StringComparer.OrdinalIgnoreCase);
				List<AttachmentModel> result = [];

				foreach(AttachmentModel att in Session.PendingAttachments)
				{
					if(!att.IsMention && seen.Add(att.FilePath))
					{
						result.Add(att);
					}
				}

				foreach(AttachmentModel att in Session.PendingAttachments)
				{
					if(att.IsMention && seen.Add(att.FilePath))
					{
						result.Add(att);
					}
				}

				return result;
			}
		}
	}

	async Task RemoveAttachment(AttachmentModel target)
	{
		SessionModel? session = Session;
		if(session is null)
		{
			return;
		}

		// Mention attachments are owned by their chip in the input — remove the chip instead
		if(target.IsMention)
		{
			_toastService.Error("Cannot remove", opts =>
				opts.Description = "This file is mentioned in the input. Remove the # mention from the input to detach it.");
			return;
		}

		lock(session.PendingAttachmentsLock)
		{
			session.PendingAttachments.Remove(target);
		}

		StateHasChanged();

		// Only delete if it's a session-owned file (pasted images saved to Cockpit\Files\)
		// Never delete user's original files selected via the file picker
		string sessionFilesDir = Path.GetFullPath(GetSessionFilesPath(session)) + Path.DirectorySeparatorChar;
		string normalizedFilePath = Path.GetFullPath(target.FilePath);
		bool isSessionOwned = normalizedFilePath.StartsWith(sessionFilesDir, StringComparison.OrdinalIgnoreCase);

		if(isSessionOwned)
		{
			try
			{
				if(File.Exists(target.FilePath))
				{
					File.Delete(target.FilePath);
				}
			}
			catch(Exception ex)
			{
				_logger.LogDebug(ex, "Failed to delete attachment file {FilePath}", target.FilePath);
			}
		}

		// Remove chip from contenteditable — no longer needed; mention path returns early above
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
