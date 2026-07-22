namespace Cockpit.Features.Sessions.Models;

/// <summary>
/// Compatibility surface for callers that have not yet adopted <see cref="Ui"/>.
/// UI components should access the grouped state directly.
/// </summary>
public partial class SessionModel
{
	public bool IsYolo
	{
		get => Ui.IsYolo;
		set => Ui.IsYolo = value;
	}

	public bool IsTerminalOpen
	{
		get => Ui.IsTerminalOpen;
		set => Ui.IsTerminalOpen = value;
	}

	public string UserInput
	{
		get => Ui.DraftText;
		set => Ui.DraftText = value;
	}

	public List<AttachmentModel> PendingAttachments
	{
		get => Ui.PendingAttachments;
		set => Ui.PendingAttachments = value;
	}

	public Lock PendingAttachmentsLock => Ui.PendingAttachmentsLock;

	public string UserInputResponseText
	{
		get => Ui.UserInputResponseText;
		set => Ui.UserInputResponseText = value;
	}
}
