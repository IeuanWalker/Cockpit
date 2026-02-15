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
				Command = "read",
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
				// TODO: Get first and second command parts for better intention generation (e.g., "git" and "git push" for "git push origin main")
				string[] cmdParts = cmdStr.Split(' ', StringSplitOptions.RemoveEmptyEntries);
				cmdParts = [.. cmdParts.Take(2)]; // Take only first two parts for intention generation

				string shortCmd = string.Join(' ', cmdParts);

				return new PermissionRequestModel
				{
					SessionId = sessionId,
					Command = shortCmd,
					RequestTitle = $"Allow running `{shortCmd}`",
					Intention = intention,
					CanApproveGlobally = true,
					CanApproveForSession = true,
					FullRequestJson = JsonSerializer.Serialize(request)
				};
			}
		}

		//// Fallback to generic messages
		//return Request.ToolName.ToLowerInvariant() switch
		//{
		//	"write" or "edit" or "create" => "Allow file changes?",
		//	"bash" or "powershell" or "cmd" or "shell" => "Allow terminal commands?",
		//	"execute" => "Allow command execution?",
		//	"git" => "Allow git operations?",
		//	"read" => "Allow file access?",
		//	_ => $"Allow {Request.ToolName}?"
		//};

		return new PermissionRequestModel
		{
			SessionId = sessionId,
			Command = request.Kind,
			RequestTitle = $"Allow `{request.Kind}`?",
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

			_logger.LogInformation("Permission request: Kind={Kind}, Command={Command}, SessionId={SessionId}",
				request.Kind, permissionRequest.Command, session.Id);

			// Check permission through our service
			PermissionDecisionEnum decision = await CheckPermissionAsync(permissionRequest, session.IsYolo);

			// Convert our decision to SDK format
			string resultKind = decision.Equals(PermissionDecisionEnum.Denied) ? "denied-interactively-by-user" : "approved";

			_logger.LogInformation("Permission decision: {Decision} for {Command}", resultKind, permissionRequest.Command);

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
			_logger.LogInformation("YOLO mode enabled - auto-approving: {Command}", request.Command);
			return PermissionDecisionEnum.Once;
		}

		lock(_permissionsLock)
		{
			// Priority 1: Check session allowlist
			if(_sessionPermissionFeature.HasPermission(request.SessionId, request.Command))
			{
				_logger.LogDebug("Command approved by session permission: {Command}", request.Command);
				return PermissionDecisionEnum.Session;
			}

			// Priority 2: Check global allowlist
			if(_globalPermissionFeature.HasPermission(request.Command))
			{
				_logger.LogDebug("Command approved by global permission: {Command}", request.Command);
				return PermissionDecisionEnum.Global;
			}
		}

		// No matching permission found - need user approval
		_logger.LogInformation("No permission found for command, requesting user approval: {NormalizedId}", request.Command);
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
			"Permission {Decision} for session {SessionId}: {Command}",
			decision.Equals(PermissionDecisionEnum.Denied) ? "denied" : "granted",
			request.SessionId,
			request.Command);

		// If approved, save permission based on scope
		if(!decision.Equals(PermissionDecisionEnum.Denied))
		{
			_logger.LogDebug("Saving permission with scope: {Scope}, command: {Command}", decision, request.Command);

			if(decision.Equals(PermissionDecisionEnum.Global))
			{
				_globalPermissionFeature.Add(request.Command);
				_logger.LogDebug("Added global permission");
			}
			else if(decision.Equals(PermissionDecisionEnum.Session))
			{
				_sessionPermissionFeature.Add(request.SessionId, request.Command);
				_logger.LogDebug("Added session permission");
			}
			// PermissionScope.Once doesn't save anything

			// Check if other pending requests for this session can now be auto-approved
			if(decision.Equals(PermissionDecisionEnum.Global) || decision.Equals(PermissionDecisionEnum.Session))
			{
				AutoResolveMatchingRequests(request.SessionId, request.Command, decision);
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
	void AutoResolveMatchingRequests(string sessionId, string command, PermissionDecisionEnum decision)
	{
		if(decision.Equals(PermissionDecisionEnum.Denied))
		{
			return;
		}

		List<PermissionRequestModel> pendingForSession = [.. _pendingRequests.Values.OrderBy(r => r.Requested)];

		_logger.LogInformation("Checking {Count} pending requests for auto-approval with pattern: {Pattern}", pendingForSession.Count, command);

		foreach(PermissionRequestModel pendingRequest in pendingForSession)
		{
			// Skip if already removed (in case of concurrent processing)
			if(!_pendingRequests.ContainsKey(pendingRequest.Id))
			{
				continue;
			}

			if(!pendingRequest.Command.Equals(command))
			{
				continue;
			}

			if(decision.Equals(PermissionDecisionEnum.Session) && pendingRequest.SessionId != sessionId)
			{
				continue;
			}

			_logger.LogInformation("Auto-approving pending request {RequestId} ({Command}) - matches {command}",
				pendingRequest.Id, pendingRequest.Command, command);

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
