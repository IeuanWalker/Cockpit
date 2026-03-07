using Cockpit.Features.SessionEvents.Models;
using Cockpit.Features.Sessions.Models;
using Cockpit.Utilities;
using GitHub.Copilot.SDK;

namespace Cockpit.Features.SessionEvents.Handlers;

static class UserMessageHandler
{
	internal static void Handle(SessionModel session, UserMessageEvent evt, bool wasAgentBusy = false)
	{
		if(evt.Data is null)
		{
			return;
		}

		List<AttachmentModel>? attachments = ConvertAttachments(evt.Data.Attachments);

		string eventMessageId = evt.Id.ToString();
		ChatMessageModel? optimistic = session.Messages.FirstOrDefault(m => m.IsUser && !m.IsComplete && m.Id == eventMessageId);
		optimistic ??= session.Messages.FirstOrDefault(m => m.IsUser && !m.IsComplete && m.Content == (evt.Data.Content ?? string.Empty));

		if(optimistic is not null)
		{
			// Confirm the optimistic message: update its metadata from the real event
			optimistic.Timestamp = evt.Timestamp;
			optimistic.EventType = evt.Type;
			optimistic.IsComplete = true;
			// Keep IsPending=true if already set (optimistic was created while agent was busy),
			// or set it now if the agent is still busy when the SDK echo arrives
			optimistic.IsPending = optimistic.IsPending || wasAgentBusy;
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
				IsPending = wasAgentBusy,
				Attachments = attachments,
				EventJson = [new Lazy<string>(() => SessionEventHelpers.SerializeEvent(evt))]
			};
			session.Messages.Add(message);
		}

		session.Status = SessionStatusEnum.Running;
	}

	static List<AttachmentModel>? ConvertAttachments(UserMessageDataAttachmentsItem[]? items)
	{
		if(items is null || items.Length == 0)
		{
			return null;
		}

		List<AttachmentModel> result = [];
		foreach(UserMessageDataAttachmentsItem item in items)
		{
			if(item is UserMessageDataAttachmentsItemFile file)
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
