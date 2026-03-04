using Cockpit.Features.SessionEvents.Models;
using Cockpit.Features.Sessions.Models;
using GitHub.Copilot.SDK;
using Microsoft.Extensions.Logging;

namespace Cockpit.Features.Sessions;

public sealed partial class SessionFeature
{
	public async Task SendMessageAsync(string content, List<AttachmentModel>? attachments = null)
	{
		if(CurrentSession is null)
		{
			return;
		}

		try
		{
			if(CurrentSession.ModelChanged || CurrentSession.AgentChanged)
			{
				await _modelFeature.SaveSessionModelSettings(CurrentSession);
				CurrentSession.ModelChanged = false;
				CurrentSession.AgentChanged = false;

				await RestartSessionWithPendingConfig(CurrentSession);
			}

			if(CurrentSession.SdkState == SdkSessionStateEnum.Loaded)
			{
				bool resumed = await ResumeSession(CurrentSession.Id);
				if(!resumed)
				{
					return;
				}
			}

			if(!_sdkRegistry.TryGet(CurrentSession.Id, out CopilotSession? sdkSession))
			{
				throw new InvalidOperationException($"Session {CurrentSession.Id} not found in SDK sessions");
			}

			ChatMessageModel? optimisticMessage = null;
			lock(CurrentSession.SessionEventLock)
			{
				bool agentWasBusy = CurrentSession.ActiveWorkingGroup is not null;
				CurrentSession.Status = SessionStatusEnum.Running;

				optimisticMessage = new ChatMessageModel
				{
					Content = content,
					IsUser = true,
					Timestamp = DateTime.UtcNow,
					Type = MessageTypeEnum.Text,
					IsComplete = false,
					IsPending = agentWasBusy,
					Attachments = attachments?.Count > 0 ? attachments : null,
					EventJson = null
				};
				CurrentSession.Messages.Add(optimisticMessage);
			}
			_sessionListFeature.NotifyStateChanged();

			List<UserMessageDataAttachmentsItem>? sdkAttachments = null;
			if(attachments?.Count > 0)
			{
				sdkAttachments = attachments
					.Select(a => (UserMessageDataAttachmentsItem)new UserMessageDataAttachmentsItemFile
					{
						Path = a.FilePath,
						DisplayName = a.FileName
					})
					.ToList();
			}

			string sentMessageId = await sdkSession.SendAsync(new MessageOptions
			{
				Prompt = content,
				Attachments = sdkAttachments
			});

			if(!string.IsNullOrWhiteSpace(sentMessageId) && optimisticMessage is not null)
			{
				lock(CurrentSession.SessionEventLock)
				{
					if(CurrentSession.Messages.Contains(optimisticMessage) && !optimisticMessage.IsComplete)
					{
						optimisticMessage.Id = sentMessageId;
					}
				}
			}
		}
		catch(Exception ex)
		{
			_logger.LogError(ex, "Failed to send message");
			CurrentSession.Status = SessionStatusEnum.Error;
			_sessionListFeature.NotifyStateChanged();
		}
	}
}
