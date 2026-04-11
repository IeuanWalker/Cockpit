using Cockpit.Features.SessionEvents.Handlers;
using Cockpit.Features.SessionEvents.Models;
using Cockpit.Features.Sessions.Models;
using GitHub.Copilot.SDK;
using Microsoft.Extensions.Logging;

namespace Cockpit.Features.Sessions;

public sealed partial class SessionFeature
{
	public async Task SendMessageAsync(string content, List<AttachmentModel>? attachments = null, bool isInternalRetry = false)
	{
		if(CurrentSession is null)
		{
			return;
		}

		ChatMessageModel? optimisticMessage = null;
		try
		{
			if(!_sdkRegistry.TryGet(CurrentSession.Id, out CopilotSession? existingSession))
			{
				throw new InvalidOperationException($"Session {CurrentSession.Id} not found");
			}

			if(CurrentSession.ModelChanged)
			{
				await existingSession.SetModelAsync(CurrentSession.Model.Id, CurrentSession.ReasoningEffort);

				CurrentSession.ModelChanged = false;
			}

			if(CurrentSession.AgentChanged)
			{
				if(CurrentSession.Context.SelectedAgent is null)
				{
					await existingSession.Rpc.Agent.DeselectAsync();
				}
				else
				{
					await existingSession.Rpc.Agent.SelectAsync(CurrentSession.Context.SelectedAgent.Config.Name);
				}

				CurrentSession.AgentChanged = false;
			}

			if(CurrentSession.SdkState == SdkSessionStateEnum.Loaded)
			{
				bool resumed = await ResumeSession(CurrentSession.Id);
				if(!resumed)
				{
					return;
				}
			}

			// Deduplicate attachments by file path before sending
			if(attachments?.Count > 0)
			{
				attachments = [.. attachments
					.GroupBy(a => a.FilePath, StringComparer.OrdinalIgnoreCase)
					.Select(g => g.First())];
			}

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
				CurrentSession.MessagesSnapshot = [.. CurrentSession.Messages];
			}
			_sessionListFeature.NotifyStateChanged();

			List<UserMessageDataAttachmentsItem>? sdkAttachments = null;
			if(attachments?.Count > 0)
			{
				sdkAttachments = [.. attachments
					.Select(a => (UserMessageDataAttachmentsItem)new UserMessageDataAttachmentsItemFile
					{
						Path = a.FilePath,
						DisplayName = a.FileName
					})];
			}

			string sentMessageId = await existingSession.SendAsync(new MessageOptions
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
		catch(IOException ex) when(ex.Message.Contains("Session not found", StringComparison.InvariantCultureIgnoreCase))
		{
			if(isInternalRetry)
			{
				// Already retried once after re-resuming — surface the error to the user
				HandleError(ex);
				return;
			}

			_logger.LogWarning(ex, "Session {SessionId} disconnected; attempting to re-resume before retrying", CurrentSession.Id);

			// Remove the optimistic message so it is not duplicated on retry
			if(optimisticMessage is not null)
			{
				lock(CurrentSession.SessionEventLock)
				{
					CurrentSession.Messages.Remove(optimisticMessage);
					CurrentSession.MessagesSnapshot = [.. CurrentSession.Messages];
				}
				_sessionListFeature.NotifyStateChanged();
			}

			// Reset state to NotLoaded so LoadSession performs a full re-resume via the SDK
			CurrentSession.SdkState = SdkSessionStateEnum.NotLoaded;
			bool resumed = await ResumeSession(CurrentSession.Id);
			if(!resumed)
			{
				HandleError(ex);
				return;
			}

			await SendMessageAsync(content, attachments, true);
		}
		catch(Exception ex)
		{
			HandleError(ex);
		}

		void HandleError(Exception ex)
		{
			_logger.LogError(ex, "Failed to send message");
			lock(CurrentSession.SessionEventLock)
			{
				if(optimisticMessage is not null)
				{
					optimisticMessage.IsError = true;
					optimisticMessage.IsComplete = true;
					optimisticMessage.IsPending = false;
				}
				CurrentSession.Status = SessionStatusEnum.Error;
				SessionErrorHandler.HandleException(CurrentSession, ex);
				CurrentSession.MessagesSnapshot = [.. CurrentSession.Messages];
			}
			_sessionListFeature.NotifyStateChanged();
		}
	}

	public async Task RetryMessageAsync(ChatMessageModel message)
	{
		if(CurrentSession is null)
		{
			return;
		}

		string content = message.Content;
		List<AttachmentModel>? attachments = message.Attachments;

		lock(CurrentSession.SessionEventLock)
		{
			int index = CurrentSession.Messages.IndexOf(message);
			CurrentSession.Messages.Remove(message);

			// Remove the companion error message that was added immediately after the failed send
			if(index >= 0 && index < CurrentSession.Messages.Count)
			{
				ChatMessageModel next = CurrentSession.Messages[index];
				if(next.Type == MessageTypeEnum.Error && !next.IsUser)
				{
					CurrentSession.Messages.RemoveAt(index);
				}
			}

			CurrentSession.MessagesSnapshot = [.. CurrentSession.Messages];
		}
		_sessionListFeature.NotifyStateChanged();

		await SendMessageAsync(content, attachments);
	}
}
