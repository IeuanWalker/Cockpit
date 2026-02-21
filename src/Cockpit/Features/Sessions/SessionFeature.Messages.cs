using Cockpit.Features.SessionEvents.Models;
using Cockpit.Features.Sessions.Models;
using GitHub.Copilot.SDK;
using Microsoft.Extensions.Logging;

namespace Cockpit.Features.Sessions;

public sealed partial class SessionFeature
{
	public async Task SendMessageAsync(string content, List<UserMessageDataAttachmentsItem>? attachments = null)
	{
		if(CurrentSession is null)
		{
			return;
		}

		try
		{
			if(CurrentSession.RequiresRestart)
			{
				await RestartSessionWithPendingConfig();
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
					IsPending = agentWasBusy
				};
				CurrentSession.Messages.Add(optimisticMessage);
			}
			_sessionListFeature.NotifyStateChanged();

			string sentMessageId = await sdkSession.SendAsync(new MessageOptions
			{
				Prompt = content,
				Attachments = attachments
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
