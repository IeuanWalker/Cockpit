using System.Text.Json;
using Cockpit.Features.Agents.Models;
using Cockpit.Features.Canvas;
using Cockpit.Features.Permissions;
using Cockpit.Features.SessionEvents.Models;
using Cockpit.Features.Sessions.Models;
using Cockpit.Features.SystemMessage;
using GitHub.Copilot;
using GitHub.Copilot.Rpc;
using Microsoft.Extensions.Logging;
using SdkPlugin = GitHub.Copilot.Rpc.Plugin;

namespace Cockpit.Features.Sessions;

public sealed partial class SessionFeature
{
	/// <summary>
	/// Loads a session by replaying its history into the UI with <c>DisableResume=true</c>, which
	/// suppresses the <c>session.resume</c> event so that merely viewing a session does not update
	/// <c>LastActivity</c>. The SDK session is registered and ready to send messages; calling
	/// <see cref="ResumeSession"/> on first message send simply promotes the flags.
	/// </summary>
	public async Task<bool> LoadSession(string sessionId)
	{
		SessionModel? session = _sessionListFeature.Sessions.FirstOrDefault(s => s.Id == sessionId);
		if(session is null)
		{
			_logger.LogWarning("Session {SessionId} not found", sessionId);
			return false;
		}

		await session.Lifecycle.SdkTransitionGate.WaitAsync();
		try
		{
			// A caller can wait behind another load while the session is deleted or its ID is
			// replaced by a model-provider restart. Do not operate on a detached model.
			if(!_sessionListFeature.Sessions.Contains(session) || session.Id != sessionId)
			{
				return false;
			}

			return await LoadSessionCore(sessionId, session);
		}
		finally
		{
			session.Lifecycle.SdkTransitionGate.Release();
		}
	}

	async Task<bool> LoadSessionCore(string sessionId, SessionModel session)
	{
		SdkLifecycleTransition loadTransition = default;
		bool loadClaimed = false;

		try
		{
			if(session.Lifecycle.SdkState != SdkSessionStateEnum.NotLoaded)
			{
				_logger.LogInformation("Session {SessionId} already loaded or loading, switching to it", sessionId);
				await SwitchCurrentSessionAsync(session);

				// Guard: eviction may have cleared the session between the state check and SwitchCurrentSessionAsync.
				// If state is now NotLoaded, fall through to perform a full reload.
				if(session.Lifecycle.SdkState != SdkSessionStateEnum.NotLoaded)
				{
					return true;
				}

				_logger.LogInformation("Session {SessionId} was evicted during switch; performing full load", sessionId);
			}

			_logger.LogInformation("Loading session {SessionId}", sessionId);

			session.Context.CurrentWorkingDirectory = SessionWorkingDirectoryNormalizer.Normalize(session.Context.CurrentWorkingDirectory);

			if(string.IsNullOrWhiteSpace(session.Context.CurrentWorkingDirectory) || !Directory.Exists(session.Context.CurrentWorkingDirectory))
			{
				session.Context.CurrentWorkingDirectory = null;
			}

			SessionWorkingDirectoryNormalizer.ApplyContextConsistency(session.Context);

			GitHub.Copilot.ProviderConfig? providerConfig = await _modelFeature.GetProviderConfig(session.Model.Id);

			// BYOK providers don't support Copilot-specific reasoning effort; always pass null for them.
			// This also guards against stale "medium" values loaded from pre-switch sessions before
			// TryRestoreModelSettings has had a chance to clear the effort.
			string? effectiveReasoningEffort = providerConfig is null ? session.ReasoningEffort : null;

			ResumeSessionConfig config = new()
			{
				ClientName = "Cockpit",
				EnableConfigDiscovery = true,
				Model = session.Model.Id,
				ReasoningEffort = effectiveReasoningEffort,
				Streaming = true,
				SuppressResumeEvent = true,
				WorkingDirectory = session.Context.CurrentWorkingDirectory,
				OnPermissionRequest = _permissionHandler.HandlePermissionRequest,
				OnUserInputRequest = _userInputHandler.HandleUserInputRequest,
				OnElicitationRequest = _elicitationHandler.HandleElicitationRequest,
				Hooks = _hooksFactory.CreateHooks(session.Model.Id, effectiveReasoningEffort, session.Context.CurrentWorkingDirectory, disableResume: true),
				Provider = providerConfig
			};

			ApplySystemMessageCustomization(config, session.Model);

			if(_appSettingsFeature.CanvasEnabled)
			{
				config.RequestCanvasRenderer = true;
				config.RequestExtensions = true;
				config.ExtensionInfo = new ExtensionInfo { Source = "cockpit", Name = "canvas-provider" };
				config.Canvases =
				[
					CreateCockpitCanvasDeclaration()
				];
				config.CanvasHandler = new SessionCanvasHandler(_canvasWindowManager);
			}

			if(!session.Lifecycle.TryBeginSdkTransition(
				SdkSessionStateEnum.NotLoaded,
				SdkSessionStateEnum.Loading,
				out loadTransition))
			{
				_logger.LogInformation("Session {SessionId} load was claimed by another operation", sessionId);
				return true;
			}
			loadClaimed = true;
			_sessionListFeature.NotifyStateChanged();

			CopilotClient client = await _clientFeature.GetClientAsync();
			CopilotSession sdkSession = await client.ResumeSessionAsync(sessionId, config);

			// The context-panel load and the event replay below are independent: the replay rebuilds
			// session.Messages and may mutate session.Context through SessionContextChangedEvent
			// (which processes during replay), while LoadContextPanelDataAsync also writes
			// session.Context. To prevent concurrent mutations, pass replay a snapshot of Context
			// rather than the live reference. Replayed context mutations are discarded after replay
			// completes, since LoadContextPanelDataAsync provides the authoritative values.
			// Run the panel's SDK round-trips concurrently with the (length-dependent) event fetch +
			// replay so they hide under it instead of adding to resume time. Joined before the restore
			// section below, which reads session.Context.
			Task contextPanelTask = LoadContextPanelDataAsync(session, sdkSession);

			bool registered = false;
			try
			{
				IReadOnlyList<SessionEvent> events = await sdkSession.GetEventsAsync(CancellationToken.None);
				_logger.LogInformation("Loading {Count} events for session {SessionId}", events.Count, sessionId);

				// Create a snapshot of the context for replay to mutate independently, avoiding
				// concurrent writes with LoadContextPanelDataAsync.
				Models.SessionContext replayContext = new()
				{
					CurrentWorkingDirectory = session.Context.CurrentWorkingDirectory,
					WorkspacePath = session.Context.WorkspacePath,
					GitRoot = session.Context.GitRoot,
					Repository = session.Context.Repository,
					Branch = session.Context.Branch,
					EditedFiles = [],
					AllowedCommands = [],
					SessionPermissionCommands = []
				};

				SessionModel tempSession = new()
				{
					Id = sessionId,
					Title = session.Title,
					AgentRunState = AgentRunStateEnum.Idle,
					Model = session.Model,
					ReasoningEffort = session.ReasoningEffort,
					Context = replayContext,
					LastActivity = session.LastActivity,
					CreatedAt = session.CreatedAt,
					SuppressFinishedNotification = true
				};

				await Task.Run(() =>
				{
					// This temporary session is never rendered. Reconstruct the complete history
					// before publishing its immutable snapshot so long histories do not copy the
					// growing message list after every structural event.
					_processor.ProcessBatch(tempSession, events, finalizeOpenGroup: true);
				});
				tempSession.Lifecycle.SetSuppressFinishedNotification(false);

				// Any message still IsPending after replay was sent while the session was
				// mid-turn and never picked up by a subsequent assistant.turn_start (the session
				// was interrupted). Clear the flag so history renders in the correct order and
				// without the "Pending…" indicator.
				foreach(ChatMessageModel msg in tempSession.Messages)
				{
					msg.IsPending = false;
				}

				session.Conversation.ReplaceMessages(tempSession.Conversation.Messages);
				session.ActiveWorkingGroup = null;
				if(session.Title != tempSession.Title)
				{
					session.Title = tempSession.Title;
				}

				// Join the context-panel load before the restore section below, which reads
				// session.Context (e.g. resolving the selected agent against the loaded agent list).
				await contextPanelTask;

				session.Context.WorkspacePath = sdkSession.WorkspacePath;
				SessionPermissionFeature.TryRestoreSessionCommands(session, _logger);
				await _modelFeature.TryRestoreModelSettings(session);
				await _agentPersistence.TryRestoreSessionAgent(session);
				if(session.Context.SelectedAgent is not null)
				{
					await sdkSession.Rpc.Agent.SelectAsync(session.Context.SelectedAgent.Name);
				}

				await _sessionModePersistence.TryRestoreSessionMode(session);
				if(session.Context.SelectedAgentMode != Models.SessionAgentModeEnum.Interactive)
				{
					await sdkSession.Rpc.Mode.SetAsync(session.Context.SelectedAgentMode.ToSdkSessionMode());
				}

				// Complete the load transition before registering to ensure the session is fully
				// live before incoming events can be processed.
				if(!session.Lifecycle.TryCompleteLoad(loadTransition))
				{
					return false;
				}

				_sdkRegistry.Register(sdkSession, evt =>
				{
					_logger.LogDebug("Session {SessionId} event: {EventType}", sdkSession.SessionId, evt.Type);
					HandleSessionEvent(sdkSession, evt);
				});
				registered = true;
				_sdkSessionByokId[sessionId] = session.ByokConfigId;

				// Switching to a different session already makes ChatMessages rebuild its window from
				// the newly selected conversation. When reconnecting/reloading the session that is
				// already selected, however, CurrentSession alone is intentionally ignored by the
				// message component. Preserve that distinction and publish a reset only after every
				// fallible load/switch step has succeeded.
				SessionChangeKind successfulHistoryReplacementKind =
					SessionLoadNotificationPolicy.GetSuccessfulHistoryReplacementKind(
						_sessionListFeature.CurrentSession?.Id,
						sessionId);
				await SwitchCurrentSessionAsync(session);
				_sessionListFeature.NotifyStateChanged(sessionId, successfulHistoryReplacementKind);
				_logger.LogInformation("Successfully loaded session {SessionId} with {MessageCount} messages", sessionId, session.Messages.Count);
				return true;
			}
			finally
			{
				if(!registered)
				{
					_sdkRegistry.TryRemove(session.Id, sdkSession);

					// The context-panel load may still be in flight on an error path. Wait for it
					// (observing any failure) before disposing the SDK session it reads from.
					try { await contextPanelTask; } catch { /* surfaced via the outer catch / loaders */ }
					await sdkSession.DisposeAsync();
					if(loadClaimed)
					{
						session.Lifecycle.TryCompleteSdkTransition(
							loadTransition,
							SdkSessionStateEnum.Loading,
							SdkSessionStateEnum.NotLoaded);
					}
				}
			}
		}
		catch(Exception ex) when(ex.Message.Contains("Session file is corrupted or incompatible", StringComparison.Ordinal))
		{
			_logger.LogError(ex, "Session {SessionId} is corrupted or incompatible", sessionId);
			if(loadClaimed)
			{
				session.Lifecycle.TryCompleteSdkTransition(
					loadTransition,
					SdkSessionStateEnum.Loading,
					SdkSessionStateEnum.NotLoaded);
			}
			_sessionListFeature.NotifyStateChanged();
			_toastService.Error("Session Unavailable", opts =>
			{
				opts.Description = "The session file may be corrupted, incompatible, or in use by another instance. You may need to delete or exit the session running elsewhere";
			});
			return false;
		}
		catch(Exception ex)
		{
			_logger.LogError(ex, "Failed to load session {SessionId}", sessionId);
			if(loadClaimed)
			{
				session.Lifecycle.TryCompleteSdkTransition(
					loadTransition,
					SdkSessionStateEnum.Loading,
					SdkSessionStateEnum.NotLoaded);
			}
			_sessionListFeature.NotifyStateChanged();
			return false;
		}
	}


	async Task LoadContextPanelDataAsync(SessionModel session, CopilotSession sdkSession)
	{
		Task<List<AgentProfile>> agentsTask = _agentFeature.LoadSessionAgentsAsync(sdkSession, session.Context.GitRoot);
		Task<List<InstructionSource>> instructionsTask = _instructionsFeature.LoadSessionInstructionsAsync(sdkSession);
		Task<List<McpServer>> mcpTask = _mcpFeature.LoadSessionMcpServersAsync(sdkSession);
		Task<List<Skill>> skillsTask = _skillsFeature.LoadSessionSkillsAsync(sdkSession);
		Task<List<SdkPlugin>> pluginsTask = _pluginsFeature.LoadSessionPluginsAsync(sdkSession);

		await Task.WhenAll(agentsTask, instructionsTask, mcpTask, skillsTask, pluginsTask);

		session.Context.Agents = agentsTask.Result;
		session.Context.Instructions = instructionsTask.Result;
		session.Context.McpServers = mcpTask.Result;
		session.Context.Skills = skillsTask.Result;
		session.Context.Plugins = pluginsTask.Result;
	}

	static SectionOverride? MapToSectionOverride(SystemMessageSectionSetting setting)
		=> setting.Action switch
		{
			SystemMessageOverrideAction.Replace => new SectionOverride { Action = SectionOverrideAction.Replace, Content = setting.Content },
			SystemMessageOverrideAction.Remove => new SectionOverride { Action = SectionOverrideAction.Remove },
			SystemMessageOverrideAction.Append => new SectionOverride { Action = SectionOverrideAction.Append, Content = setting.Content },
			SystemMessageOverrideAction.Prepend => new SectionOverride { Action = SectionOverrideAction.Prepend, Content = setting.Content },
			_ => null  // None → skip
		};

	static CanvasDeclaration CreateCockpitCanvasDeclaration()
		=> new()
		{
			Id = "cockpit-canvas",
			DisplayName = "Cockpit Canvas",
			Description = "Opens a visual canvas window alongside your message — it is an enhancement, NOT a replacement for your reply. IMPORTANT: you must ALWAYS write a complete, self-contained assistant message in addition to invoking this canvas. The canvas is ephemeral: it is not retained when the user resumes or revisits a session, so your text message is the durable record the user will rely on. Everything you show in the canvas must also be communicated meaningfully in your message (e.g. summarise a chart's findings, include the table data as markdown, restate the diagram as prose). Never rely on the canvas as the sole delivery of information. Provide a JSON object with \"html\" (required) containing styled HTML to render, and \"title\" (optional) for the window title. Content is rendered inside a sandboxed iframe (allow-scripts only) — scripts execute but have no access to the parent window, storage, or navigation. Tailwind CSS v3 is available (all utilities) and script tags execute in insertion order. External CDN script tags are NOT supported (the sandbox blocks network requests from the null origin); use only inline scripts and the preloaded libraries (Chart.js, Mermaid). CSS vars --bg-color, --text-color, --title-color, --secondary-text, --accent-color, --border-color, --sidebar-color, --hover-color are available for theming. Provide: \"html\" (required) rich interactive HTML; \"title\" (optional) window title.",
			InputSchema = JsonSerializer.SerializeToElement(new
			{
				type = "object",
				required = new[] { "html" },
				properties = new
				{
					html = new
					{
						type = "string",
						description = "Rich HTML rendered in a sandboxed iframe inside the canvas window. IMPORTANT: the canvas is a visual enhancement only — it is ephemeral and will NOT be visible when the user resumes this session later. You must always accompany this canvas invocation with a complete text message that conveys the same information (e.g. markdown table, prose summary, or code block) so the user retains full value even without the canvas. The sandbox allows script execution but blocks access to the parent window, cookies, storage, forms, popups, and navigation. The native window chrome already displays the canvas title, so do not duplicate it in the HTML unless you intentionally want a separate in-content heading. Tailwind CSS v3 and Cockpit app.css classes are available: bg-app-bg, bg-app-sidebar, border-app-border, bg-app-hover, bg-app-active, text-app-text, text-app-title, secondary-text, accent-btn, scrollbar-thin. Mermaid (strict security level) and Chart.js are preloaded locally — do NOT add CDN script tags for these or any other external libraries (the sandboxed iframe has a null origin and cannot fetch external resources). For Mermaid, use <div class=\"mermaid\">...</div> instead of markdown fences or raw code blocks. For Chart.js, create a canvas element and instantiate new Chart(...) in a following inline script; maintainAspectRatio is always enforced to true and cannot be overridden — do NOT set maintainAspectRatio to false. Inline scripts run in insertion order. CSS vars --bg-color, --sidebar-color, --border-color, --hover-color, --active-color, --text-color, --title-color, --secondary-text, --accent-color, --button-bg, --button-hover are available for theming."
					},
					title = new { type = "string", description = "Optional window title bar text." }
				}
			})
		};

}

internal static class SessionLoadNotificationPolicy
{
	public static SessionChangeKind GetSuccessfulHistoryReplacementKind(
		string? selectedSessionId,
		string loadedSessionId) =>
		string.Equals(selectedSessionId, loadedSessionId, StringComparison.Ordinal)
			? SessionChangeKind.ConversationReset | SessionChangeKind.ConversationStructure
			: SessionChangeKind.None;
}
