namespace Cockpit.Features.Sessions.Models;

/// <summary>
/// Transient state owned by the session UI. This state is not part of the SDK
/// lifecycle or the conversation produced by session events.
/// </summary>
public sealed class SessionUiState
{
	public bool IsYolo { get; set; }
	public bool IsTerminalOpen { get; set; }

	/// <summary>
	/// Draft text preserved when the user switches sessions.
	/// </summary>
	public string DraftText { get; set; } = string.Empty;

	/// <summary>
	/// Attachments staged in the composer and preserved across session switches.
	/// </summary>
	public List<AttachmentModel> PendingAttachments { get; set; } = [];

	/// <summary>
	/// Synchronizes attachment mutations made by UI callbacks on different threads.
	/// </summary>
	public Lock PendingAttachmentsLock { get; } = new();

	/// <summary>
	/// Draft response for the active user-input request.
	/// </summary>
	public string UserInputResponseText { get; set; } = string.Empty;
}
