using System.Collections.Concurrent;
using Blazor.Sonner.Services;
using Cockpit.Features.Canvas;
using Cockpit.Features.Agents;
using Cockpit.Features.AppSettings;
using Cockpit.Features.ElicitationRequests;
using Cockpit.Features.Git;
using Cockpit.Features.Hooks;
using Cockpit.Features.Instructions;
using Cockpit.Features.Mcp;
using Cockpit.Features.Models;
using Cockpit.Features.Permissions;
using Cockpit.Features.Plugins;
using Cockpit.Features.Sdk;
using Cockpit.Features.SessionEvents;
using Cockpit.Features.SessionEvents.Handlers;
using Cockpit.Features.SessionEvents.Models;
using Cockpit.Features.Sessions.Models;
using Cockpit.Features.Skills;
using Cockpit.Features.Terminal;
using Cockpit.Features.UserInputRequests;
using GitHub.Copilot;
using Microsoft.Extensions.Logging;

namespace Cockpit.Features.Sessions;

public sealed partial class SessionFeature : IDisposable
{
	readonly CopilotClientFeature _clientFeature;
	readonly ILogger<SessionFeature> _logger;
	readonly ToastService _toastService;
	readonly IModelFeature _modelFeature;
	readonly SessionEventProcessor _processor;
	readonly TerminalFeature _terminalFeature;
	readonly SessionListFeature _sessionListFeature;
	readonly IPermissionHandler _permissionHandler;
	readonly IUserInputHandler _userInputHandler;
	readonly IElicitationHandler _elicitationHandler;
	readonly GitFeature _gitFeature;
	readonly SdkSessionRegistry _sdkRegistry;
	readonly AgentPersistence _agentPersistence;
	readonly AgentFeature _agentFeature;
	readonly SessionModePersistence _sessionModePersistence;
	readonly InstructionsFeature _instructionsFeature;
	readonly McpFeature _mcpFeature;
	readonly SkillsFeature _skillsFeature;
	readonly PluginsFeature _pluginsFeature;
	readonly IAppSettingsFeature _appSettingsFeature;
	readonly SessionHooksFactory _hooksFactory;
	readonly CanvasWindowManager _canvasWindowManager;

	public SessionFeature(
		CopilotClientFeature clientFeature,
		ILogger<SessionFeature> logger,
		ToastService toastService,
		IModelFeature modelFeature,
		TerminalFeature terminalFeature,
		SessionEventProcessor processor,
		SessionListFeature sessionListFeature,
		IPermissionHandler permissionHandler,
		IUserInputHandler userInputHandler,
		IElicitationHandler elicitationHandler,
		GitFeature gitFeature,
		SdkSessionRegistry sdkRegistry,
		AgentPersistence agentPersistence,
		AgentFeature agentFeature,
		SessionModePersistence sessionModePersistence,
		InstructionsFeature instructionsFeature,
		McpFeature mcpFeature,
		SkillsFeature skillsFeature,
		PluginsFeature pluginsFeature,
		IAppSettingsFeature appSettingsFeature,
		SessionHooksFactory hooksFactory,
		CanvasWindowManager canvasWindowManager)
	{
		_clientFeature = clientFeature;
		_logger = logger;
		_toastService = toastService;
		_modelFeature = modelFeature;
		_terminalFeature = terminalFeature;
		_processor = processor;
		_sessionListFeature = sessionListFeature;
		_permissionHandler = permissionHandler;
		_userInputHandler = userInputHandler;
		_elicitationHandler = elicitationHandler;
		_gitFeature = gitFeature;
		_sdkRegistry = sdkRegistry;
		_agentPersistence = agentPersistence;
		_agentFeature = agentFeature;
		_sessionModePersistence = sessionModePersistence;
		_instructionsFeature = instructionsFeature;
		_mcpFeature = mcpFeature;
		_skillsFeature = skillsFeature;
		_pluginsFeature = pluginsFeature;
		_appSettingsFeature = appSettingsFeature;
		_hooksFactory = hooksFactory;
		_canvasWindowManager = canvasWindowManager;

		_clientFeature.OnConnectionStateChanged += HandleConnectionStateChanged;
		StartEvictionLoop();
		StartReconnectLoop();
	}

	readonly CancellationTokenSource _evictionCts = new();

	/// <summary>
	/// Tracks the ByokConfigId that was active when each SDK session was last created or resumed.
	/// Used to detect provider changes (BYOK ↔ built-in) by comparing against the new model selection
	/// in <see cref="SendMessageAsync"/>. The value is null for built-in sessions.
	/// </summary>
	readonly ConcurrentDictionary<string, string?> _sdkSessionByokId = new();

	IDisposable? _currentWatcher;

	public SessionModel? CurrentSession => _sessionListFeature.CurrentSession;
	public IReadOnlyList<SessionModel> Sessions => _sessionListFeature.Sessions;
	public event Action? OnStateChanged
	{
		add => _sessionListFeature.OnStateChanged += value;
		remove => _sessionListFeature.OnStateChanged -= value;
	}
	public ActivityGroupModel? ActiveWorkingGroup => CurrentSession?.ActiveWorkingGroup;
	public bool IsWorking => CurrentSession?.Status == SessionStatusEnum.Running
		|| (CurrentSession?.ActiveWorkingGroup is not null && CurrentSession.ActiveWorkingGroup.Status == GroupStatusEnum.Running);

	void HandleSessionEvent(string sessionId, SessionEvent evt)
	{
		SessionModel? session = _sessionListFeature.Sessions.FirstOrDefault(s => s.Id == sessionId);

		if(session is null)
		{
			return;
		}

		Func<ChatMessageModel, string, Task>? streamCallback = session == _sessionListFeature.CurrentSession
			? (msg, text) => SessionEventHelpers.StreamSummaryTextAsync(msg, text, _sessionListFeature.NotifyStateChanged)
			: null;

		lock(session.SessionEventLock)
		{
			_processor.Process(session, evt, streamCallback);
			session.MessagesSnapshot = [.. session.Messages];
		}

		if(session == _sessionListFeature.CurrentSession)
		{
			_sessionListFeature.NotifyStateChanged();
		}
	}

	public void Dispose()
	{
		_clientFeature.OnConnectionStateChanged -= HandleConnectionStateChanged;
		_currentWatcher?.Dispose();
		_evictionCts.Cancel();
		_evictionCts.Dispose();
	}
}
