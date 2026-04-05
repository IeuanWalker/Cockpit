using System.Collections.Concurrent;
using Cockpit.Features.Permissions.Models;
using Cockpit.Features.Sessions;
using Cockpit.Features.Sessions.Models;
using GitHub.Copilot.SDK;
using Microsoft.Extensions.Logging;

namespace Cockpit.Features.Permissions;

/// <summary>
/// Service for managing tool execution permissions
/// </summary>
public sealed partial class PermissionFeature : IPermissionHandler, IDisposable
{
	readonly GlobalPermissionFeature _globalPermissionFeature;
	readonly GlobalDenyFeature _globalDenyFeature;
	readonly SessionPermissionFeature _sessionPermissionFeature;
	readonly ISessionStateProvider _sessionStateProvider;
	readonly ILogger<PermissionFeature> _logger;

	// In-memory cache of permissions
	readonly ConcurrentDictionary<string, PermissionRequestModel> _pendingRequests = new(); // Key: request.Id
	readonly ReaderWriterLockSlim _permissionsLock = new();

	// Events for UI updates
	public event Action<string, PermissionRequestModel>? OnPermissionRequested;
	public event Action<string, string>? OnPermissionResolved;

	public PermissionFeature(
		GlobalPermissionFeature globalPermissionFeature,
		GlobalDenyFeature globalDenyFeature,
		SessionPermissionFeature sessionPermissionFeature,
		ISessionStateProvider sessionStateProvider,
		ILogger<PermissionFeature> logger)
	{
		_globalPermissionFeature = globalPermissionFeature;
		_globalDenyFeature = globalDenyFeature;
		_sessionPermissionFeature = sessionPermissionFeature;
		_sessionStateProvider = sessionStateProvider;
		_logger = logger;
	}

	public async Task<PermissionRequestResult> HandlePermissionRequest(PermissionRequest request, PermissionInvocation invocation)
	{
		try
		{
			SessionModel? session = _sessionStateProvider.Sessions.FirstOrDefault(s => s.Id == invocation.SessionId);
			if(session is null)
			{
				_logger.LogWarning("SessionModel not found for SDK session {SessionId}", invocation.SessionId);
				return new PermissionRequestResult
				{
					Kind = PermissionRequestResultKind.DeniedCouldNotRequestFromUser
				};
			}

			PermissionRequestModel permissionRequest = ToRequestModel(request, session);

			_logger.LogInformation("Permission request: Kind={Kind}, Commands={Commands}, SessionId={SessionId}", request.Kind, string.Join(", ", permissionRequest.Commands), session.Id);

			// Check permission through our service
			PermissionDecisionEnum decision = await CheckPermissionAsync(permissionRequest, session.IsYolo);

			// Convert our decision to SDK format
			PermissionRequestResultKind resultKind = decision.Equals(PermissionDecisionEnum.Denied) ? PermissionRequestResultKind.DeniedInteractivelyByUser : PermissionRequestResultKind.Approved;

			_logger.LogInformation("Permission decision: {Decision} for {Commands}", resultKind, string.Join(", ", permissionRequest.Commands));

			return new PermissionRequestResult { Kind = resultKind };
		}
		catch(Exception ex)
		{
			_logger.LogError(ex, "Error in permission handler");
			return new PermissionRequestResult { Kind = PermissionRequestResultKind.DeniedCouldNotRequestFromUser };
		}
	}

	void UpdateSessionOnPermissionResolved(string sessionId, string requestId)
	{
		SessionModel? session = _sessionStateProvider.Sessions.FirstOrDefault(s => s.Id == sessionId);
		if(session is null)
		{
			return;
		}

		_logger.LogInformation("Permission resolved - Removing request ID: {RequestId} from session {SessionId}", requestId, sessionId);

		lock(session.StatusHistoryLock)
		{
			session.PendingPermissionRequests.TryRemove(requestId, out _);

			session.Status = session.PendingPermissionRequests.IsEmpty && session.PendingUserInputRequests.IsEmpty
				? session.StatusHistory.TryPop(out SessionStatusEnum prev) ? prev : SessionStatusEnum.Idle
				: session.PendingPermissionRequests.IsEmpty
					? SessionStatusEnum.NeedsUserInput
					: SessionStatusEnum.NeedsPermission;
		}

		// Notify UI (outside lock to avoid potential deadlocks)
		_sessionStateProvider.NotifyStateChanged();
	}

	void UpdateSessionOnPermissionRequested(string sessionId, PermissionRequestModel request)
	{
		SessionModel? session = _sessionStateProvider.Sessions.FirstOrDefault(s => s.Id == sessionId);
		if(session is null)
		{
			return;
		}

		_logger.LogInformation("Permission requested - Adding request ID: {RequestId} to session {SessionId}", request.Id, sessionId);

		lock(session.StatusHistoryLock)
		{
			if(!session.PendingPermissionRequests.TryAdd(request.Id, request))
			{
				_logger.LogWarning("Permission request {RequestId} already exists for session {SessionId}", request.Id, sessionId);
				return;
			}

			// Only push to history on the first blocking request (i.e. when not already in a blocking status).
			// Subsequent concurrent requests see NeedsPermission/NeedsUserInput and skip the push, preventing duplicates.
			if(session.Status is not SessionStatusEnum.NeedsPermission and not SessionStatusEnum.NeedsUserInput)
			{
				session.StatusHistory.Push(session.Status);
			}
			session.Status = SessionStatusEnum.NeedsPermission;
		}

		// Notify UI (outside lock to avoid potential deadlocks)
		_sessionStateProvider.NotifyStateChanged();
	}

	/// <summary>
	/// Check if a tool execution is permitted
	/// </summary>
	/// <param name="request">Permission request</param>
	/// <param name="isYolo">If true, auto-approve all requests</param>
	/// <returns>Permission decision</returns>
	public async Task<PermissionDecisionEnum> CheckPermissionAsync(PermissionRequestModel request, bool isYolo = false)
	{
		// YOLO mode - auto-approve everything
		if(isYolo)
		{
			_logger.LogInformation("YOLO mode enabled - auto-approving: {Commands}", string.Join(", ", request.Commands));
			return PermissionDecisionEnum.Once;
		}

		// Safe commands - auto-approve without prompting the user
		if(!request.IsDestructive && CommandExtractor.AreAllCommandsSafe(request.Commands))
		{
			_logger.LogDebug("All commands are safe, auto-approving: {Commands}", string.Join(", ", request.Commands));
			return PermissionDecisionEnum.Once;
		}

		_permissionsLock.EnterReadLock();
		bool needsFiltering = false;
		List<string> unapprovedCommands = [];

		try
		{
			// Priority 1: Check session allowlist - all commands must be allowed
			if(_sessionPermissionFeature.HasPermissions(request.SessionId, request.Commands))
			{
				_logger.LogDebug("Commands approved by session permission: {Commands}", string.Join(", ", request.Commands));
				return PermissionDecisionEnum.Session;
			}

			// Priority 2: Check global allowlist - all commands must be allowed
			if(_globalPermissionFeature.HasPermissions(request.Commands))
			{
				_logger.LogDebug("Commands approved by global permission: {Commands}", string.Join(", ", request.Commands));
				return PermissionDecisionEnum.Global;
			}

			// Filter out already-approved commands; for destructive requests every command
			// reaches the dialog regardless of the safe list, matching the pre-approval behaviour.
			unapprovedCommands = [.. request.Commands
				.Where(cmd => (request.IsDestructive || !CommandExtractor.IsCommandSafe(cmd)) &&
							  !_sessionPermissionFeature.HasPermission(request.SessionId, cmd) &&
							  !_globalPermissionFeature.HasPermission(cmd))];

			// If all commands are approved, auto-approve
			if(unapprovedCommands.Count == 0)
			{
				_logger.LogDebug("All commands already approved individually");
				return PermissionDecisionEnum.Once;
			}

			// If some commands were filtered out, we need to update the request
			needsFiltering = unapprovedCommands.Count < request.Commands.Count;
		}
		finally
		{
			_permissionsLock.ExitReadLock();
		}

		// Update request to only show unapproved commands (outside lock)
		if(needsFiltering)
		{
			_logger.LogInformation("Filtered approved commands. Original: {Original}, Unapproved: {Unapproved}",
				string.Join(", ", request.Commands), string.Join(", ", unapprovedCommands));

			// Update request to only show unapproved commands
			request.Commands.Clear();
			request.Commands.AddRange(unapprovedCommands);

			// Update title using the same format as original
			string commandList = string.Join("`, `", unapprovedCommands);
			request.RequestTitle = request.IsDestructive
				? $"⚠️ Allow destructive command `{commandList}`"
				: $"Allow running `{commandList}`";

			// Re-append file deletion info if present
			if(request.FilesToDelete.Count > 0)
			{
				request.RequestTitle += $" (deletes {request.FilesToDelete.Count} file(s))";
			}
		}

		// No matching permission found - need user approval
		_logger.LogInformation("No permission found for commands, requesting user approval: {Commands}", string.Join(", ", request.Commands));
		return await RequestUserApprovalAsync(request);
	}

	/// <summary>
	/// Request user approval for a tool execution
	/// </summary>
	async Task<PermissionDecisionEnum> RequestUserApprovalAsync(PermissionRequestModel request)
	{
		// Store pending request using unique request ID
		_pendingRequests[request.Id] = request;

		// Notify UI
		UpdateSessionOnPermissionRequested(request.SessionId, request);
		OnPermissionRequested?.Invoke(request.SessionId, request);

		// Wait for user decision
		try
		{
			PermissionDecisionEnum decision = await request.GetDecisionAsync();
			return decision;
		}
		catch(Exception ex)
		{
			_logger.LogError(ex, "Error waiting for permission decision");
			return PermissionDecisionEnum.Denied;
		}
		finally
		{
			// Clean up pending request using request ID
			_pendingRequests.TryRemove(request.Id, out _);
		}
	}

	/// <summary>
	/// Resolve a pending permission request with user decision
	/// </summary>
	public void ResolvePermissionRequest(string requestId, PermissionDecisionEnum decision)
	{
		_logger.LogInformation("ResolvePermissionRequest called with requestId: {RequestId}, IsApproved: {IsApproved}", requestId, !decision.Equals(PermissionDecisionEnum.Denied));

		if(!_pendingRequests.TryGetValue(requestId, out PermissionRequestModel? request))
		{
			_logger.LogWarning("No pending permission request found for request ID {RequestId}. Current pending count: {Count}", requestId, _pendingRequests.Count);
			// Log all pending request IDs for debugging
			foreach(KeyValuePair<string, PermissionRequestModel> kvp in _pendingRequests)
			{
				_logger.LogDebug("Pending request ID: {Id}, SessionId: {SessionId}", kvp.Key, kvp.Value.SessionId);
			}
			return;
		}

		_logger.LogInformation(
			"Permission {Decision} for session {SessionId}: {Commands}",
			decision.Equals(PermissionDecisionEnum.Denied) ? "denied" : "granted",
			request.SessionId,
			string.Join(", ", request.Commands));

		// If approved, save permission based on scope
		if(!decision.Equals(PermissionDecisionEnum.Denied))
		{
			_logger.LogDebug("Saving permission with scope: {Scope}, commands: {Commands}", decision, string.Join(", ", request.Commands));

			if(decision.Equals(PermissionDecisionEnum.Global))
			{
				_globalPermissionFeature.Add(request.Commands);
				_logger.LogDebug("Added global permission");
			}
			else if(decision.Equals(PermissionDecisionEnum.Session))
			{
				_sessionPermissionFeature.Add(request.SessionId, request.Commands);
				_logger.LogDebug("Added session permission");
			}
			// PermissionScope.Once doesn't save anything

			// Check if other pending requests for this session can now be auto-approved
			if(decision.Equals(PermissionDecisionEnum.Global) || decision.Equals(PermissionDecisionEnum.Session))
			{
				AutoResolveMatchingRequests(request.SessionId, request.Id, request.Commands, decision);
			}
		}

		_logger.LogDebug("Completing TaskCompletionSource for session {SessionId}", request.SessionId);
		// Complete the TaskCompletionSource
		bool completed = request.CompletionSource.TrySetResult(decision);
		_logger.LogDebug("TaskCompletionSource completed: {Completed}", completed);

		// Notify UI with requestId so it can be removed from session list
		UpdateSessionOnPermissionResolved(request.SessionId, request.Id);
		OnPermissionResolved?.Invoke(request.SessionId, request.Id);
	}

	void AutoResolveMatchingRequests(string sessionId, string excludeRequestId, List<string> commands, PermissionDecisionEnum decision)
	{
		if(decision.Equals(PermissionDecisionEnum.Denied))
		{
			return;
		}

		List<PermissionRequestModel> pendingForSession = [.. _pendingRequests.Values.OrderBy(r => r.Requested)];

		_logger.LogInformation("Checking {Count} pending requests for auto-approval with patterns: {Patterns}", pendingForSession.Count, string.Join(", ", commands));

		foreach(PermissionRequestModel pendingRequest in pendingForSession)
		{
			// Skip the request being resolved by the caller — it is handled directly there.
			if(pendingRequest.Id == excludeRequestId)
			{
				continue;
			}

			// Check if ANY command in the pending request is in the newly granted commands
			bool hasMatchingCommand = pendingRequest.Commands.Any(cmd => commands.Contains(cmd));
			if(!hasMatchingCommand)
			{
				continue;
			}

			if(decision.Equals(PermissionDecisionEnum.Session) && pendingRequest.SessionId != sessionId)
			{
				continue;
			}

			_logger.LogInformation("Auto-approving pending request {RequestId} ({Commands}) - commands match granted permissions",
				pendingRequest.Id, string.Join(", ", pendingRequest.Commands));

			// Remove from pending requests atomically before completing
			if(_pendingRequests.TryRemove(pendingRequest.Id, out _))
			{
				// Complete the TaskCompletionSource to unblock the waiting permission check
				pendingRequest.CompletionSource.TrySetResult(decision);

				// Update session state and notify UI
				UpdateSessionOnPermissionResolved(sessionId, pendingRequest.Id);
				OnPermissionResolved?.Invoke(sessionId, pendingRequest.Id);
			}
		}
	}

	/// <summary>
	/// Cancels all pending permission requests for a session (e.g., when the session is aborted or deleted).
	/// </summary>
	public void CancelPendingRequestsForSession(string sessionId)
	{
		List<string> requestIds = [.. _pendingRequests.Values
			.Where(r => r.SessionId == sessionId)
			.Select(r => r.Id)];

		foreach(string requestId in requestIds)
		{
			_logger.LogInformation("Cancelling pending permission request {RequestId} for aborted session {SessionId}", requestId, sessionId);
			ResolvePermissionRequest(requestId, PermissionDecisionEnum.Denied);
		}
	}

	public void Dispose()
	{
		Dispose(true);
		GC.SuppressFinalize(this);
	}

	void Dispose(bool disposing)
	{
		if(disposing)
		{
			_permissionsLock.Dispose();
		}
	}
}
