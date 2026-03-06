using Cockpit.Features.Sessions;
using Cockpit.Features.Sessions.Models;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.Logging;
using Microsoft.JSInterop;

namespace Cockpit.Components.Pages.ChatPanel.InputArea;

public partial class AttachmentsControl : ComponentBase
{
	readonly SessionFeature _sessionFeature;
	readonly IJSRuntime _jsRuntime;
	readonly ILogger<AttachmentsControl> _logger;
	[Parameter] public string? SessionId { get; set; }
	[Parameter] public int AttachmentCount { get; set; }

	public AttachmentsControl(
		SessionFeature sessionFeature,
		IJSRuntime jsRuntime,
		ILogger<AttachmentsControl> logger)
	{
		_sessionFeature = sessionFeature;
		_jsRuntime = jsRuntime;
		_logger = logger;
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

		StateHasChanged();

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
