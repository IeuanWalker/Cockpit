using Cockpit.Features.ElicitationRequests;
using Cockpit.Features.Permissions.Models;
using Cockpit.Features.Sessions.Models;
using Cockpit.Features.UserInputRequests;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace Cockpit.Features.Sessions.Interactions;

[Flags]
public enum PendingInteractionKinds
{
	None = 0,
	Permissions = 1,
	UserInputs = 2,
	Elicitations = 4,
	All = Permissions | UserInputs | Elicitations
}

/// <summary>
/// Owns session-visible pending-interaction bookkeeping. SDK-facing features retain ownership
/// of their completion sources, while all cross-type status and collection updates are
/// serialized here to preserve the existing display-status behaviour.
/// </summary>
public sealed class SessionInteractionCoordinator
{
	readonly ISessionStateProvider _sessionStateProvider;
	readonly ILogger<SessionInteractionCoordinator> _logger;

	public SessionInteractionCoordinator(
		ISessionStateProvider sessionStateProvider,
		ILogger<SessionInteractionCoordinator>? logger = null)
	{
		_sessionStateProvider = sessionStateProvider;
		_logger = logger ?? NullLogger<SessionInteractionCoordinator>.Instance;
	}

	public void AddPermission(string sessionId, PermissionRequestModel request) => Add(
		sessionId,
		request.Id,
		"permission",
		SessionStatusEnum.NeedsPermission,
		session => session.PendingPermissionRequests.TryAdd(request.Id, request));

	public void AddUserInput(string sessionId, UserInputRequestModel request) => Add(
		sessionId,
		request.Id,
		"user input",
		SessionStatusEnum.NeedsUserInput,
		session => session.PendingUserInputRequests.TryAdd(request.Id, request));

	public void AddElicitation(string sessionId, ElicitationRequestModel request) => Add(
		sessionId,
		request.Id,
		"elicitation",
		SessionStatusEnum.NeedsElicitation,
		session => session.PendingElicitationRequests.TryAdd(request.Id, request));

	public void ResolvePermission(string sessionId, string requestId) => Resolve(
		sessionId,
		requestId,
		"permission",
		session => session.PendingPermissionRequests.TryRemove(requestId, out _));

	public void ResolveUserInput(string sessionId, string requestId) => Resolve(
		sessionId,
		requestId,
		"user input",
		session => session.PendingUserInputRequests.TryRemove(requestId, out _));

	public void ResolveElicitation(string sessionId, string requestId) => Resolve(
		sessionId,
		requestId,
		"elicitation",
		session => session.PendingElicitationRequests.TryRemove(requestId, out _));

	/// <summary>
	/// Clears session-visible bookkeeping during lifecycle cleanup. The caller retains
	/// responsibility for cancelling the SDK-facing completion sources and notifying the UI.
	/// </summary>
	public void ClearBookkeeping(
		string sessionId,
		PendingInteractionKinds interactionKinds)
	{
		SessionModel? session = FindSession(sessionId);
		if(session is null)
		{
			return;
		}

		lock(session.PendingInteractionsLock)
		{
			if(interactionKinds.HasFlag(PendingInteractionKinds.Permissions))
			{
				session.PendingPermissionRequests.Clear();
			}

			if(interactionKinds.HasFlag(PendingInteractionKinds.UserInputs))
			{
				session.PendingUserInputRequests.Clear();
			}

			if(interactionKinds.HasFlag(PendingInteractionKinds.Elicitations))
			{
				session.PendingElicitationRequests.Clear();
			}

			session.PendingInteractionStatus = GetPendingInteractionStatus(session);
		}
	}

	void Add(
		string sessionId,
		string requestId,
		string interactionType,
		SessionStatusEnum blockingStatus,
		Func<SessionModel, bool> tryAdd)
	{
		SessionModel? session = FindSession(sessionId);
		if(session is null)
		{
			return;
		}

		_logger.LogInformation(
			"{InteractionType} requested - Adding request ID: {RequestId} to session {SessionId}",
			interactionType,
			requestId,
			sessionId);

		lock(session.PendingInteractionsLock)
		{
			if(!tryAdd(session))
			{
				_logger.LogWarning(
					"{InteractionType} request {RequestId} already exists for session {SessionId}",
					interactionType,
					requestId,
					sessionId);
				return;
			}

			session.PendingInteractionStatus = blockingStatus;
		}

		_sessionStateProvider.NotifyStateChanged();
	}

	void Resolve(
		string sessionId,
		string requestId,
		string interactionType,
		Func<SessionModel, bool> tryRemove)
	{
		SessionModel? session = FindSession(sessionId);
		if(session is null)
		{
			return;
		}

		_logger.LogInformation(
			"{InteractionType} resolved - Removing request ID: {RequestId} from session {SessionId}",
			interactionType,
			requestId,
			sessionId);

		lock(session.PendingInteractionsLock)
		{
			if(!tryRemove(session))
			{
				_logger.LogDebug(
					"{InteractionType} request {RequestId} is no longer pending for session {SessionId}",
					interactionType,
					requestId,
					sessionId);
				return;
			}

			session.PendingInteractionStatus = GetPendingInteractionStatus(session);
		}

		_sessionStateProvider.NotifyStateChanged();
	}

	SessionModel? FindSession(string sessionId)
		=> _sessionStateProvider.Sessions.FirstOrDefault(session => session.Id == sessionId);

	static SessionStatusEnum? GetPendingInteractionStatus(SessionModel session)
	{
		if(!session.PendingPermissionRequests.IsEmpty)
		{
			return SessionStatusEnum.NeedsPermission;
		}

		if(!session.PendingUserInputRequests.IsEmpty)
		{
			return SessionStatusEnum.NeedsUserInput;
		}

		if(!session.PendingElicitationRequests.IsEmpty)
		{
			return SessionStatusEnum.NeedsElicitation;
		}

		return null;
	}
}
