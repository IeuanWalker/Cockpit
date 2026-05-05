using Cockpit.Features.SessionEvents.Models;
using Cockpit.Features.Sessions.Models;
using Cockpit.Utilities;
using GitHub.Copilot.SDK;

namespace Cockpit.Features.SessionEvents.Handlers;

static class UserMessageHandler
{
	/// <summary>
	/// Source values that indicate the message was synthesised by the agent rather than typed by the user.
	/// These messages are immediately acted upon and must never be marked pending.
	/// </summary>
	static readonly HashSet<string> agentGeneratedSources = ["thinking-exhausted-continuation"];

	internal static void Handle(SessionModel session, UserMessageEvent evt, bool wasAgentBusy = false)
	{
		if(evt.Data is null)
		{
			return;
		}

		List<AttachmentModel>? attachments = ConvertAttachments(evt.Data.Attachments);

		// Agent-synthesised messages (e.g. thinking-exhausted-continuation) are immediately
		// acted upon by the next assistant turn and must never be shown as "Pending…".
		bool isAgentGenerated = evt.Data.Source is not null && agentGeneratedSources.Contains(evt.Data.Source);

		string eventMessageId = evt.Id.ToString();
		// For agent-generated messages there is no UI-side optimistic placeholder, so skip the
		// content-based fallback match to avoid accidentally stealing a queued user message that
		// happens to share the same text.
		ChatMessageModel? optimistic = session.Messages.FirstOrDefault(m => m.IsUser && !m.IsComplete && m.Id == eventMessageId);
		if(!isAgentGenerated)
		{
			optimistic ??= session.Messages.FirstOrDefault(m => m.IsUser && !m.IsComplete && m.Content == (evt.Data.Content ?? string.Empty));
		}

		if(optimistic is not null)
		{
			// Confirm the optimistic message: update its metadata from the real event
			optimistic.Timestamp = evt.Timestamp;
			optimistic.EventType = evt.Type;
			optimistic.IsComplete = true;
			// Keep IsPending=true if already set (optimistic was created while agent was busy),
			// or set it now if the agent is still busy when the SDK echo arrives.
			// Never set IsPending for agent-generated messages.
			optimistic.IsPending = !isAgentGenerated && (optimistic.IsPending || wasAgentBusy);
			// Fill in attachments from event if the optimistic message didn't already have them
			optimistic.Attachments ??= attachments;
			optimistic.EventJson ??= [];
			optimistic.EventJson.Add(new Lazy<string>(() => SessionEventHelpers.SerializeEvent(evt)));
		}
		else
		{
			ChatMessageModel message = new()
			{
				Id = Guid.NewGuid().ToString(),
				Content = evt.Data.Content ?? string.Empty,
				IsUser = true,
				Timestamp = evt.Timestamp,
				Type = MessageTypeEnum.Text,
				EventType = evt.Type,
				IsPending = !isAgentGenerated && wasAgentBusy,
				Attachments = attachments,
				EventJson = [new Lazy<string>(() => SessionEventHelpers.SerializeEvent(evt))]
			};
			session.Messages.Add(message);
		}

		session.Status = SessionStatusEnum.Running;
	}

	static List<AttachmentModel>? ConvertAttachments(UserMessageAttachment[]? items)
	{
		if(items is null || items.Length == 0)
		{
			return null;
		}

		List<AttachmentModel> result = [];
		foreach(UserMessageAttachment item in items)
		{
			if(item is UserMessageAttachmentFile file)
			{
				string filePath = file.Path ?? string.Empty;
				string fileName = file.DisplayName ?? Path.GetFileName(filePath);
				string ext = FileUtil.GetNormalizedExtension(fileName);
				string mimeType = FileUtil.GetMimeType(ext);
				// DataUri is intentionally null here — AttachmentModel.GetPreviewSrc() will lazily
				// read the file from disk at render time, avoiding a blocking read on the SDK event thread.

				result.Add(new AttachmentModel(fileName, filePath, null, mimeType));
			}
		}

		return result.Count > 0 ? result : null;
	}
}
