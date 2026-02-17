using System.Collections.Concurrent;
using System.Text.Json;
using Cockpit.Features.Permissions.Models;
using Cockpit.Models;
using Cockpit.Services;
using GitHub.Copilot.SDK;
using Microsoft.Extensions.Logging;

namespace Cockpit.Features.Permissions;

/// <summary>
/// Service for managing tool execution permissions
/// </summary>
public class PermissionFeature
{
	readonly GlobalPermissionFeature _globalPermissionFeature;
	readonly SessionPermissionFeature _sessionPermissionFeature;
	readonly ISessionStateProvider _sessionStateProvider;
	readonly ILogger<PermissionFeature> _logger;

	// In-memory cache of permissions
	readonly ConcurrentDictionary<string, PermissionRequestModel> _pendingRequests = new(); // Key: request.Id
	readonly Lock _permissionsLock = new();

	// Events for UI updates
	public event Action<string, PermissionRequestModel>? OnPermissionRequested;
	public event Action<string, string>? OnPermissionResolved;

	public PermissionFeature(
		GlobalPermissionFeature globalPermissionFeature,
		SessionPermissionFeature sessionPermissionFeature,
		ISessionStateProvider sessionStateProvider,
		ILogger<PermissionFeature> logger)
	{
		_globalPermissionFeature = globalPermissionFeature;
		_sessionPermissionFeature = sessionPermissionFeature;
		_sessionStateProvider = sessionStateProvider;
		_logger = logger;

		OnPermissionRequested += HandlePermissionRequested;
		OnPermissionResolved += HandlePermissionResolved;
	}

	PermissionRequestModel ToRequestModel(PermissionRequest request, string sessionId)
	{
		// TODO: Get intention
		string intention = string.Empty;

		if(request.Kind.Equals("read", StringComparison.InvariantCultureIgnoreCase))
		{
			return new PermissionRequestModel
			{
				SessionId = sessionId,
				Commands = ["read"],
				RequestTitle = "Allow read file(s)",
				Intention = intention,
				CanApproveGlobally = true,
				CanApproveForSession = true,
				FullRequestJson = JsonSerializer.Serialize(request)
			};
		}

		if(request.Kind.Equals("write", StringComparison.InvariantCultureIgnoreCase))
		{
			// TODO: Get path of write file

			// TODO: Get current working diretory that the request belongs to

			// if in current working directory

			// if outside current working directory
		}

		// Try to extract command from various possible fields
		if(request.ExtensionData?.TryGetValue("command", out object? cmdObj) ?? false)
		{
			string cmdStr = cmdObj?.ToString() ?? string.Empty;

			// If it's a JSON object with fullCommandText, extract that
			if(cmdStr.StartsWith('{') && cmdStr.Contains("fullCommandText"))
			{
				try
				{
					using JsonDocument doc = JsonDocument.Parse(cmdStr);
					if(doc.RootElement.TryGetProperty("fullCommandText", out JsonElement fullCmd))
					{
						cmdStr = fullCmd.GetString() ?? request.Kind;
					}
				}
				catch
				{
					// Fall through to return as-is
				}
			}

			if(!string.IsNullOrEmpty(cmdStr))
			{
				// Extract meaningful executables (filters out cd, pwd, etc.)
				List<string> meaningfulExecutables = CommandExtractor.ExtractMeaningfulExecutables(cmdStr);

				// Use meaningful executables if found, otherwise fall back to all executables
				List<string> commands;
				if(meaningfulExecutables.Count > 0)
				{
					commands = meaningfulExecutables;
				}
				else
				{
					// Only navigation commands - use all executables
					commands = CommandExtractor.ExtractExecutables(cmdStr);
					if(commands.Count == 0)
					{
						// Fallback to first word
						commands = [cmdStr.Split(' ', StringSplitOptions.RemoveEmptyEntries).FirstOrDefault() ?? cmdStr];
					}
				}

				// Create comma-separated list of commands for title
				string commandList = string.Join("`, `", commands);

				// Check if this is a destructive command
				bool isDestructive = CommandExtractor.ContainsDestructiveCommand(cmdStr);
				List<string> filesToDelete = isDestructive ? CommandExtractor.ExtractFilesToDelete(cmdStr) : [];

				string requestTitle = isDestructive
					? $"⚠️ Allow destructive command `{commandList}`"
					: $"Allow running `{commandList}`";

				if(filesToDelete.Count > 0)
				{
					requestTitle += $" (deletes {filesToDelete.Count} file(s))";
				}

				return new PermissionRequestModel
				{
					SessionId = sessionId,
					Commands = commands,
					RequestTitle = requestTitle,
					Intention = intention,
					CanApproveGlobally = !isDestructive, // Destructive commands can't be globally approved
					CanApproveForSession = true,
					FullRequestJson = JsonSerializer.Serialize(request),
					IsDestructive = isDestructive,
					FilesToDelete = [.. filesToDelete]
				};
			}
		}

		// Fallback to generic messages
		return new PermissionRequestModel
		{
			SessionId = sessionId,
			Commands = [request.Kind],
			RequestTitle = request.Kind.ToLowerInvariant() switch
			{
				"write" or "edit" or "create" => "Allow file changes?",
				"bash" or "powershell" or "cmd" or "shell" => "Allow terminal commands?",
				"execute" => "Allow command execution?",
				"git" => "Allow git operations?",
				"read" => "Allow file access?",
				_ => $"Allow {request.Kind}?"
			},
			Intention = intention,
			CanApproveGlobally = true,
			CanApproveForSession = true,
			FullRequestJson = JsonSerializer.Serialize(request)
		};
	}

	public async Task<PermissionRequestResult> HandlePermissionRequest(PermissionRequest request, PermissionInvocation invocation)
	{
		try
		{
			ChatSession? session = _sessionStateProvider.GetSessions().FirstOrDefault(s => s.Id == invocation.SessionId);
			if(session is null)
			{
				_logger.LogWarning("ChatSession not found for SDK session {SessionId}", invocation.SessionId);
				return new PermissionRequestResult
				{
					Kind = "denied"
				};
			}

			PermissionRequestModel permissionRequest = ToRequestModel(request, invocation.SessionId);

			_logger.LogInformation("Permission request: Kind={Kind}, Commands={Commands}, SessionId={SessionId}",
				request.Kind, string.Join(", ", permissionRequest.Commands), session.Id);

			// Check permission through our service
			PermissionDecisionEnum decision = await CheckPermissionAsync(permissionRequest, session.IsYolo);

			// Convert our decision to SDK format
			string resultKind = decision.Equals(PermissionDecisionEnum.Denied) ? "denied-interactively-by-user" : "approved";

			_logger.LogInformation("Permission decision: {Decision} for {Commands}", resultKind, string.Join(", ", permissionRequest.Commands));

			return new PermissionRequestResult { Kind = resultKind };
		}
		catch(Exception ex)
		{
			_logger.LogError(ex, "Error in permission handler");
			return new PermissionRequestResult { Kind = "denied" };
		}
	}

	public void HandlePermissionResolved(string sessionId, string requestId)
	{
		ChatSession? session = _sessionStateProvider.GetSessions().FirstOrDefault(s => s.Id == sessionId);
		if(session is null)
		{
			return;
		}

		_logger.LogInformation("HandlePermissionResolved - Removing request ID: {RequestId} from session {SessionId}", requestId, sessionId);

		// Use lock to ensure atomic remove-check-restore operation
		lock(session.PermissionRequestsLock)
		{
			// Remove the specific resolved request from the collection atomically
			session.PendingPermissionRequests.TryRemove(requestId, out _);

			// Restore previous status only when all permissions are resolved
			if(session.PendingPermissionRequests.IsEmpty)
			{
				session.Status = session.PreviousStatus ?? SessionStatus.Idle;
			}
		}

		// Notify UI (outside lock to avoid potential deadlocks)
		_sessionStateProvider.NotifyStateChanged();
	}

	public void HandlePermissionRequested(string sessionId, PermissionRequestModel request)
	{
		ChatSession? session = _sessionStateProvider.GetSessions().FirstOrDefault(s => s.Id == sessionId);
		if(session is null)
		{
			return;
		}

		_logger.LogInformation("HandlePermissionRequested - Adding request ID: {RequestId} to session {SessionId}", request.Id, sessionId);

		// Use lock to ensure atomic check-add-set operation
		lock(session.PermissionRequestsLock)
		{
			// Add to pending requests collection atomically
			if(!session.PendingPermissionRequests.TryAdd(request.Id, request))
			{
				_logger.LogWarning("Permission request {RequestId} already exists for session {SessionId}", request.Id, sessionId);
				return;
			}

			// Set status to NeedsPermission if this was the first request
			if(session.PendingPermissionRequests.Count == 1)
			{
				session.PreviousStatus = session.Status;
				session.Status = SessionStatus.NeedsPermission;
			}
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

		lock(_permissionsLock)
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
				AutoResolveMatchingRequests(request.SessionId, request.Commands, decision);
			}
		}

		_logger.LogDebug("Completing TaskCompletionSource for session {SessionId}", request.SessionId);
		// Complete the TaskCompletionSource
		bool completed = request.CompletionSource.TrySetResult(decision);
		_logger.LogDebug("TaskCompletionSource completed: {Completed}", completed);

		// Notify UI with requestId so it can be removed from session list
		OnPermissionResolved?.Invoke(request.SessionId, request.Id);
	}

	/// <summary>
	/// Auto-resolve pending requests that match a newly added permission
	/// </summary>
	void AutoResolveMatchingRequests(string sessionId, List<string> commands, PermissionDecisionEnum decision)
	{
		if(decision.Equals(PermissionDecisionEnum.Denied))
		{
			return;
		}

		List<PermissionRequestModel> pendingForSession = [.. _pendingRequests.Values.OrderBy(r => r.Requested)];

		_logger.LogInformation("Checking {Count} pending requests for auto-approval with patterns: {Patterns}", pendingForSession.Count, string.Join(", ", commands));

		foreach(PermissionRequestModel pendingRequest in pendingForSession)
		{
			// Skip if already removed (in case of concurrent processing)
			if(!_pendingRequests.ContainsKey(pendingRequest.Id))
			{
				continue;
			}

			// Check if ALL commands in the pending request are in the newly granted commands
			if(!pendingRequest.Commands.All(cmd => commands.Contains(cmd)))
			{
				continue;
			}

			if(decision.Equals(PermissionDecisionEnum.Session) && pendingRequest.SessionId != sessionId)
			{
				continue;
			}

			_logger.LogInformation("Auto-approving pending request {RequestId} ({Commands}) - all commands match",
				pendingRequest.Id, string.Join(", ", pendingRequest.Commands));

			// Remove from pending requests atomically before completing
			if(_pendingRequests.TryRemove(pendingRequest.Id, out _))
			{
				// Complete the TaskCompletionSource to unblock the waiting permission check
				pendingRequest.CompletionSource.TrySetResult(decision);

				// Notify UI that this request was resolved
				OnPermissionResolved?.Invoke(sessionId, pendingRequest.Id);
			}
		}
	}
}
