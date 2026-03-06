using Blazor.Sonner.Services;
using Cockpit.Features.Agents;
using Cockpit.Features.Git;
using Cockpit.Features.Models;
using Cockpit.Features.Permissions;
using Cockpit.Features.Sdk;
using Cockpit.Features.SessionEvents;
using Cockpit.Features.SessionEvents.Models;
using Cockpit.Features.Sessions.Models;
using Cockpit.Features.Terminal;
using Cockpit.Features.UserInputRequests;
using GitHub.Copilot.SDK;
using Microsoft.Extensions.Logging;

namespace Cockpit.Features.Sessions;

public sealed partial class SessionFeature : IDisposable
{
	readonly CopilotClientFeature _clientFeature;
	readonly ILogger<SessionFeature> _logger;
	readonly ToastService _toastService;
	readonly ModelFeature _modelFeature;
	readonly SessionEventProcessor _processor;
	readonly TerminalFeature _terminalFeature;
	readonly SessionListFeature _sessionListFeature;
	readonly IPermissionHandler _permissionHandler;
	readonly UserInputFeature _userInputHandler;
	readonly GitFeature _gitFeature;
	readonly SdkSessionRegistry _sdkRegistry;
	readonly AgentPersistence _agentPersistence;
	readonly GlobalAgentFeature _globalAgentFeature;
	readonly SessionAgentFeature _sessionAgentFeature;

	public SessionFeature(
		CopilotClientFeature clientFeature,
		ILogger<SessionFeature> logger,
		ToastService toastService,
		ModelFeature modelFeature,
		TerminalFeature terminalFeature,
		SessionEventProcessor processor,
		SessionListFeature sessionListFeature,
		IPermissionHandler permissionHandler,
		UserInputFeature userInputHandler,
		GitFeature gitFeature,
		SdkSessionRegistry sdkRegistry,
		AgentPersistence agentPersistence,
		GlobalAgentFeature globalAgentFeature,
		SessionAgentFeature sessionAgentFeature)
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
		_gitFeature = gitFeature;
		_sdkRegistry = sdkRegistry;
		_agentPersistence = agentPersistence;
		_globalAgentFeature = globalAgentFeature;
		_sessionAgentFeature = sessionAgentFeature;
	}

	IDisposable? _currentWatcher;

	public SessionModel? CurrentSession => _sessionListFeature.CurrentSession;
	public IReadOnlyList<SessionModel> Sessions => _sessionListFeature.Sessions;
	public event Action? OnStateChanged
	{
		add => _sessionListFeature.OnStateChanged += value;
		remove => _sessionListFeature.OnStateChanged -= value;
	}
	public ActivityGroupModel? ActiveWorkingGroup => CurrentSession?.ActiveWorkingGroup;
	public bool IsWorking => CurrentSession?.ActiveWorkingGroup is not null && CurrentSession.ActiveWorkingGroup.Status == GroupStatusEnum.Running;

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
		_currentWatcher?.Dispose();
		GC.SuppressFinalize(this);
	}
}
