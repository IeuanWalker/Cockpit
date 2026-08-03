using Cockpit.Features.MessageMode;
using Cockpit.Features.SessionEvents.Handlers;
using Cockpit.Features.SessionEvents.Models;
using Cockpit.Features.Sessions.Models;
using GitHub.Copilot;
using Microsoft.Extensions.Logging;

namespace Cockpit.Features.Sessions;

public sealed partial class SessionFeature
{
	public Task SendMessageAsync(string content, List<AttachmentModel>? attachments = null, bool isInternalRetry = false)
	{
		SessionModel? session = CurrentSession;
		return session is null
			? Task.CompletedTask
			: SendMessageAsync(session, content, attachments, isInternalRetry);
	}

	async Task SendMessageAsync(
		SessionModel session,
		string content,
		List<AttachmentModel>? attachments,
		bool isInternalRetry)
	{
		string sessionId = session.Id;
		ChatMessageModel? optimisticMessage = null;
		try
		{
			if(!_sdkRegistry.TryGet(sessionId, out CopilotSession? existingSession))
			{
				throw new InvalidOperationException($"Session {sessionId} not found");
			}

			long? modelChangeVersion = session.Lifecycle.CaptureModelChange();
			if(modelChangeVersion is not null)
			{
				ProviderConfig? newProviderConfig = await _modelFeature.GetProviderConfig(session.Model.Id);

				// Compare the ByokConfigId the current SDK session was created with against what the new
				// model selection needs. session.ByokConfigId reflects the new selection (already updated
				// by the UI), so we use _sdkSessionByokId to know what the live SDK session actually has.
				string? sdkActiveByokId = _sdkSessionByokId.GetValueOrDefault(sessionId);
				string? newByokId = newProviderConfig is not null ? session.ByokConfigId : null;
				bool requiresRestart = newByokId != sdkActiveByokId;

				if(requiresRestart)
				{
					// If switching to BYOK, ByokConfigId was already set by the UI before ModelChanged was set.
					// If switching away from BYOK, ByokConfigId was already cleared to null by the UI.
					await RestartSession(session.Id, session.Model.Id, session.ReasoningEffort, newProviderConfig);

					// The session ID may have changed if a brand-new SDK session was created (no prior messages).
					// Re-capture sessionId and re-fetch the live SDK session before sending.
					sessionId = session.Id;
					if(!_sdkRegistry.TryGet(sessionId, out existingSession))
					{
						throw new InvalidOperationException($"Session {sessionId} not found after model restart");
					}

					session.Lifecycle.ClearModelChanged(modelChangeVersion);
				}
				else
				{
					await existingSession.SetModelAsync(session.Model.Id, session.ReasoningEffort);
					session.Lifecycle.ClearModelChanged(modelChangeVersion);
				}
			}

			long? agentChangeVersion = session.Lifecycle.CaptureAgentChange();
			if(agentChangeVersion is not null)
			{
				if(session.Context.SelectedAgent is null)
				{
					await existingSession.Rpc.Agent.DeselectAsync();
				}
				else
				{
					await existingSession.Rpc.Agent.SelectAsync(session.Context.SelectedAgent.Name);
				}

				session.Lifecycle.ClearAgentChanged(agentChangeVersion);
			}

			long? agentModeChangeVersion = session.Lifecycle.CaptureAgentModeChange();
			if(agentModeChangeVersion is not null)
			{
				await existingSession.Rpc.Mode.SetAsync(session.Context.SelectedAgentMode.ToSdkSessionMode());
				session.Lifecycle.ClearAgentModeChanged(agentModeChangeVersion);
			}

			if(session.Lifecycle.SdkState == SdkSessionStateEnum.Loaded)
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

			MessageTurnModeEnum selectedTurnMode = _appSettingsFeature.MessageTurnMode;
			string turnMode = selectedTurnMode.ToSdkToken();

			lock(session.SessionEventLock)
			{
				bool agentWasBusy = session.Conversation.ActiveWorkingGroup is not null;
				session.Lifecycle.SetAgentRunState(AgentRunStateEnum.Running);

				optimisticMessage = new ChatMessageModel
				{
					Content = content,
					IsUser = true,
					Timestamp = DateTime.UtcNow,
					Type = MessageTypeEnum.Text,
					IsComplete = false,
					WasSentLocally = true,
					// Immediate mode bypasses the queue — never show as pending even if agent is busy
					IsPending = agentWasBusy && selectedTurnMode == MessageTurnModeEnum.Enqueue,
					Attachments = attachments?.Count > 0 ? attachments : null,
					EventJson = null
				};
				session.Conversation.AddMessage(optimisticMessage);
				session.Conversation.PublishMessagesSnapshotIfChanged();

				// For immediate (steering) mode: flag that a new turn is imminent so the
				// working panel and Running status are preserved through the idle transition.
				if(agentWasBusy && selectedTurnMode == MessageTurnModeEnum.Immediate)
				{
					session.Conversation.HasQueuedImmediateMessage = true;
				}
				else if(!agentWasBusy)
				{
					// Clear any stale value (e.g. after a failed send) when no turn is in-flight.
					session.Conversation.HasQueuedImmediateMessage = false;
				}
			}
			_sessionListFeature.NotifyStateChanged(
				session.Id,
				SessionChangeKind.ConversationStructure | SessionChangeKind.ConversationContent | SessionChangeKind.SessionSummary);

			List<Attachment>? sdkAttachments = null;
			if(attachments?.Count > 0)
			{
				sdkAttachments = [];
				foreach(AttachmentModel attachment in attachments)
				{
					if(attachment.IsDirectory)
					{
						sdkAttachments.Add(new AttachmentDirectory
						{
							DisplayName = attachment.FileName,
							Path = attachment.FilePath
						});
					}
					else
					{
						sdkAttachments.Add(new AttachmentFile
						{
							DisplayName = attachment.FileName,
							Path = attachment.FilePath
						});
					}
				}
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
					if(session.Conversation.Messages.Contains(optimisticMessage) && !optimisticMessage.IsComplete)
					{
						string oldId = optimisticMessage.Id;
						optimisticMessage.Id = sentMessageId;

						// Keep the working group anchor in sync with the updated message ID.
						// assistant.turn_start may have captured the old GUID before SendAsync returned.
						if(session.Conversation.ActiveWorkingGroup?.TriggeredByUserMessageId == oldId)
						{
							session.Conversation.ActiveWorkingGroup.TriggeredByUserMessageId = sentMessageId;
						}
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
					session.Conversation.RemoveMessage(optimisticMessage);
					session.Conversation.PublishMessagesSnapshotIfChanged();
				}
				_sessionListFeature.NotifyStateChanged(session.Id, SessionChangeKind.ConversationStructure);
			}

			// Remove and dispose any previously registered SDK session before forcing a full re-resume
			if(_sdkRegistry.TryRemove(sessionId, out CopilotSession? existingSession))
			{
				await existingSession.DisposeAsync();
			}

			// Reset state to NotLoaded so LoadSession performs a full re-resume via the SDK
			session.Lifecycle.SetSdkState(SdkSessionStateEnum.NotLoaded);
			bool resumed = await ResumeSession(sessionId);
			if(!resumed)
			{
				HandleError(ex);
				return;
			}

			await SendMessageAsync(session, content, attachments, true);
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
				session.Lifecycle.SetAgentRunState(AgentRunStateEnum.Error);
				SessionErrorHandler.HandleException(session, ex);
				bool structureChanged = session.Conversation.PublishMessagesSnapshotIfChanged();
				_sessionListFeature.NotifyStateChanged(
					session.Id,
					SessionChangeKind.ConversationContent
						| SessionChangeKind.SessionSummary
						| (structureChanged ? SessionChangeKind.ConversationStructure : SessionChangeKind.None));
			}
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
			int index = CurrentSession.Conversation.IndexOfMessage(message);
			CurrentSession.Conversation.RemoveMessage(message);

			// Remove the companion error message that was added immediately after the failed send
			if(index >= 0 && index < CurrentSession.Messages.Count)
			{
				ChatMessageModel next = CurrentSession.Messages[index];
				if(next.Type == MessageTypeEnum.Error && !next.IsUser)
				{
					CurrentSession.Conversation.RemoveMessageAt(index);
				}
			}

			CurrentSession.Conversation.PublishMessagesSnapshotIfChanged();
		}
		_sessionListFeature.NotifyStateChanged(CurrentSession.Id, SessionChangeKind.ConversationStructure);

		await SendMessageAsync(content, attachments);
	}

	/// <summary>
	/// Triggers context compaction for the current session via <c>session.Rpc.History.CompactAsync()</c>.
	/// No-op when there is no current session, no live SDK session, or the session is already compacting.
	/// </summary>
	public async Task CompactContextAsync()
	{
		if(CurrentSession is null)
		{
			return;
		}

		SessionModel session = CurrentSession;

		// Claim the compaction slot atomically to prevent duplicate requests from racing callers.
		// The SDK will also set IsCompacting via SessionCompactionStartEvent; our optimistic set here
		// is the guard. We reset it ourselves only if the SDK call throws before that event arrives.
		lock(session.SessionEventLock)
		{
			if(session.IsCompacting)
			{
				return;
			}

			session.IsCompacting = true;
		}

		if(!_sdkRegistry.TryGet(session.Id, out CopilotSession? sdkSession))
		{
			_logger.LogWarning("CompactContextAsync: no live SDK session for {SessionId}", session.Id);
			lock(session.SessionEventLock)
			{
				session.IsCompacting = false;
			}
			return;
		}

		try
		{
			_logger.LogInformation("Requesting context compaction for session {SessionId}", session.Id);
			GitHub.Copilot.Rpc.HistoryCompactResult result = await sdkSession.Rpc.History.CompactAsync();
			if(!result.Success)
			{
				_logger.LogWarning("Context compaction did not succeed for session {SessionId}", session.Id);
				_toastService.Warning("Context compaction did not complete successfully.");
			}
		}
		catch(Exception ex)
		{
			// SDK call failed before SessionCompactionStartEvent — reset the flag we set optimistically.
			lock(session.SessionEventLock)
			{
				session.IsCompacting = false;
			}
			_logger.LogError(ex, "Failed to compact context for session {SessionId}", session.Id);
			_toastService.Error($"Failed to compact context: {ex.Message}");
		}
	}
}
