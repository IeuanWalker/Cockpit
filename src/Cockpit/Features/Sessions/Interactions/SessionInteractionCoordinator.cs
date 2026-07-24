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
		interactions => interactions.TryAddPermission(request));

	public void AddUserInput(string sessionId, UserInputRequestModel request) => Add(
		sessionId,
		request.Id,
		"user input",
		SessionStatusEnum.NeedsUserInput,
		interactions => interactions.TryAddUserInput(request));

	public void AddElicitation(string sessionId, ElicitationRequestModel request) => Add(
		sessionId,
		request.Id,
		"elicitation",
		SessionStatusEnum.NeedsElicitation,
		interactions => interactions.TryAddElicitation(request));

	public void ResolvePermission(string sessionId, string requestId) => Resolve(
		sessionId,
		requestId,
		"permission",
		interactions => interactions.TryRemovePermission(requestId));

	public void ResolveUserInput(string sessionId, string requestId) => Resolve(
		sessionId,
		requestId,
		"user input",
		interactions => interactions.TryRemoveUserInput(requestId));

	public void ResolveElicitation(string sessionId, string requestId) => Resolve(
		sessionId,
		requestId,
		"elicitation",
		interactions => interactions.TryRemoveElicitation(requestId));

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

		PendingInteractionState interactions = session.PendingInteractions;
		lock(interactions.SyncRoot)
		{
			if(interactionKinds.HasFlag(PendingInteractionKinds.Permissions))
			{
				interactions.ClearPermissions();
			}

			if(interactionKinds.HasFlag(PendingInteractionKinds.UserInputs))
			{
				interactions.ClearUserInputs();
			}

			if(interactionKinds.HasFlag(PendingInteractionKinds.Elicitations))
			{
				interactions.ClearElicitations();
			}

			interactions.DisplayStatus = GetPendingInteractionStatus(interactions);
		}
	}

	void Add(
		string sessionId,
		string requestId,
		string interactionType,
		SessionStatusEnum blockingStatus,
		Func<PendingInteractionState, bool> tryAdd)
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

		PendingInteractionState interactions = session.PendingInteractions;
		lock(interactions.SyncRoot)
		{
			if(!tryAdd(interactions))
			{
				_logger.LogWarning(
					"{InteractionType} request {RequestId} already exists for session {SessionId}",
					interactionType,
					requestId,
					sessionId);
				return;
			}

			interactions.DisplayStatus = blockingStatus;
		}

		_sessionStateProvider.NotifyStateChanged();
	}

	void Resolve(
		string sessionId,
		string requestId,
		string interactionType,
		Func<PendingInteractionState, bool> tryRemove)
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

		PendingInteractionState interactions = session.PendingInteractions;
		lock(interactions.SyncRoot)
		{
			if(!tryRemove(interactions))
			{
				_logger.LogDebug(
					"{InteractionType} request {RequestId} is no longer pending for session {SessionId}",
					interactionType,
					requestId,
					sessionId);
				return;
			}

			interactions.DisplayStatus = GetPendingInteractionStatus(interactions);
		}

		_sessionStateProvider.NotifyStateChanged();
	}

	SessionModel? FindSession(string sessionId)
		=> _sessionStateProvider.Sessions.FirstOrDefault(session => session.Id == sessionId);

	static SessionStatusEnum? GetPendingInteractionStatus(PendingInteractionState interactions)
	{
		if(interactions.HasPermissions)
		{
			return SessionStatusEnum.NeedsPermission;
		}

		if(interactions.HasUserInputs)
		{
			return SessionStatusEnum.NeedsUserInput;
		}

		if(interactions.HasElicitations)
		{
			return SessionStatusEnum.NeedsElicitation;
		}

		return null;
	}
}
