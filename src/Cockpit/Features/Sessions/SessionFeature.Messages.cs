using Cockpit.Features.MessageMode;
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

		SessionModel session = CurrentSession;
		string sessionId = session.Id;
		ChatMessageModel? optimisticMessage = null;
		try
		{
			if(!_sdkRegistry.TryGet(sessionId, out CopilotSession? existingSession))
			{
				throw new InvalidOperationException($"Session {sessionId} not found");
			}

			if(session.ModelChanged)
			{
				await existingSession.SetModelAsync(session.Model.Id, session.ReasoningEffort);

				session.ModelChanged = false;
			}

			if(session.AgentChanged)
			{
				if(session.Context.SelectedAgent is null)
				{
					await existingSession.Rpc.Agent.DeselectAsync();
				}
				else
				{
					await existingSession.Rpc.Agent.SelectAsync(session.Context.SelectedAgent.Config.Name);
				}

				session.AgentChanged = false;
			}

			if(session.AgentModeChanged)
			{
				await existingSession.Rpc.Mode.SetAsync(session.Context.SelectedAgentMode.ToSdkSessionMode());
				session.AgentModeChanged = false;
			}

			if(session.SdkState == SdkSessionStateEnum.Loaded)
			{
				bool resumed = await ResumeSession(sessionId);
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

			string turnMode = UserAppSettings.MessageTurnMode == MessageTurnModeEnum.Enqueue ? "enqueue" : "immediate";

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
					// Immediate mode bypasses the queue — never show as pending even if agent is busy
					IsPending = agentWasBusy && turnMode == "enqueue",
					Attachments = attachments?.Count > 0 ? attachments : null,
					EventJson = null
				};
				CurrentSession.Messages.Add(optimisticMessage);
				CurrentSession.MessagesSnapshot = [.. CurrentSession.Messages];
			}
			_sessionListFeature.NotifyStateChanged();

			List<UserMessageAttachment>? sdkAttachments = null;
			if(attachments?.Count > 0)
			{
				sdkAttachments = [.. attachments
					.Select(a => (UserMessageAttachmentFile)new UserMessageAttachmentFile
					{
						Path = a.FilePath,
						DisplayName = a.FileName
					})];
			}
			string sentMessageId = await existingSession.SendAsync(new MessageOptions
			{
				Prompt = content,
				Attachments = sdkAttachments,
				Mode = turnMode
			});

			if(!string.IsNullOrWhiteSpace(sentMessageId) && optimisticMessage is not null)
			{
				lock(session.SessionEventLock)
				{
					if(session.Messages.Contains(optimisticMessage) && !optimisticMessage.IsComplete)
					{
						optimisticMessage.Id = sentMessageId;
					}
				}
			}
		}
		catch(IOException ex) when(ex.Message.Contains("Session not found", StringComparison.OrdinalIgnoreCase))
		{
			if(isInternalRetry)
			{
				// Already retried once after re-resuming — surface the error to the user
				HandleError(ex);
				return;
			}

			_logger.LogWarning(ex, "Session {SessionId} disconnected; attempting to re-resume before retrying", sessionId);

			// Remove the optimistic message so it is not duplicated on retry
			if(optimisticMessage is not null)
			{
				lock(session.SessionEventLock)
				{
					session.Messages.Remove(optimisticMessage);
					session.MessagesSnapshot = [.. session.Messages];
				}
				_sessionListFeature.NotifyStateChanged();
			}

			// Remove and dispose any previously registered SDK session before forcing a full re-resume
			if(_sdkRegistry.TryRemove(CurrentSession.Id, out CopilotSession? existingSession))
			{
				await existingSession.DisposeAsync();
			}

			// Reset state to NotLoaded so LoadSession performs a full re-resume via the SDK
			session.SdkState = SdkSessionStateEnum.NotLoaded;
			bool resumed = await ResumeSession(sessionId);
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
			lock(session.SessionEventLock)
			{
				if(optimisticMessage is not null)
				{
					optimisticMessage.IsError = true;
					optimisticMessage.IsComplete = true;
					optimisticMessage.IsPending = false;
				}
				session.Status = SessionStatusEnum.Error;
				SessionErrorHandler.HandleException(session, ex);
				session.MessagesSnapshot = [.. session.Messages];
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
