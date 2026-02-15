using System.Collections.Concurrent;
using System.Text.Json;
using System.Text.RegularExpressions;
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
	readonly ISessionStateProvider _sessionStateProvider;
	readonly ILogger<PermissionFeature> _logger;

	// In-memory cache of permissions
	readonly ConcurrentDictionary<string, List<ToolPermission>> _sessionPermissions = new();
	readonly ConcurrentDictionary<string, Models.PermissionRequest> _pendingRequests = new(); // Key: request.Id
	readonly Lock _permissionsLock = new();

	// Events for UI updates
	public event Action<string, Models.PermissionRequest>? OnPermissionRequested;
	public event Action<string, string, PermissionDecision>? OnPermissionResolved; // sessionId, requestId, decision

	public PermissionFeature(GlobalPermissionFeature globalPermissionFeature, ISessionStateProvider sessionStateProvider, ILogger<PermissionFeature> logger)
	{
		_globalPermissionFeature = globalPermissionFeature;
		_sessionStateProvider = sessionStateProvider;
		_logger = logger;
	}

	public async Task<PermissionRequestResult> HandlePermissionRequest(GitHub.Copilot.SDK.PermissionRequest request, PermissionInvocation invocation)
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

			// Extract tool information from the permission request
			// The SDK sends different "kinds" of permission requests (e.g., "write", "execute", etc.)
			string toolName = request.Kind; // e.g., "write", "execute", etc.
			string command = ExtractCommandFromRequest(request);

			// Pass ExtensionData as arguments for detailed message generation
			Dictionary<string, object>? arguments = request.ExtensionData != null
				? new Dictionary<string, object>(request.ExtensionData)
				: null;


			_logger.LogInformation("Permission request: Kind={Kind}, Command={Command}, SessionId={SessionId}",
				toolName, command, session.Id);

			// Check permission through our service
			PermissionDecision decision = await CheckPermissionAsync(
				session.Id,
				toolName,
				command,
				arguments: arguments,
				kind: request.Kind,
				isYolo: session.IsYolo);

			// Convert our decision to SDK format
			string resultKind = decision.IsApproved ? "approved" : "denied-interactively-by-user";

			_logger.LogInformation("Permission decision: {Decision} for {Command}", resultKind, command);

			return new PermissionRequestResult { Kind = resultKind };
		}
		catch(Exception ex)
		{
			_logger.LogError(ex, "Error in permission handler");
			return new PermissionRequestResult { Kind = "denied" };
		}





		static string ExtractCommandFromRequest(GitHub.Copilot.SDK.PermissionRequest request)
		{
			// The SDK puts additional context in ExtensionData
			if(request.ExtensionData is null)
			{
				return request.Kind;
			}

			// Try to extract command from various possible fields
			if(request.ExtensionData.TryGetValue("command", out object? cmdObj))
			{
				string cmdStr = cmdObj?.ToString() ?? "";

				// If it's a JSON object with fullCommandText, extract that
				if(cmdStr.StartsWith('{') && cmdStr.Contains("fullCommandText"))
				{
					try
					{
						using JsonDocument doc = JsonDocument.Parse(cmdStr);
						if(doc.RootElement.TryGetProperty("fullCommandText", out JsonElement fullCmd))
						{
							return fullCmd.GetString() ?? request.Kind;
						}
					}
					catch
					{
						// Fall through to return as-is
					}
				}

				return cmdStr;
			}

			if(request.ExtensionData.TryGetValue("path", out object? pathObj))
			{
				return pathObj?.ToString() ?? request.Kind;
			}

			if(request.ExtensionData.TryGetValue("args", out object? argsObj))
			{
				return argsObj?.ToString() ?? request.Kind;
			}

			// Fallback: return kind
			return request.Kind;
		}
	}

	public void HandlePermissionResolved(string sessionId, string requestId, PermissionDecision decision)
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

	public void HandlePermissionRequested(string sessionId, Models.PermissionRequest request)
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
	/// Normalize a permission identifier similar to Cooper's approach
	/// </summary>
	static string NormalizePermissionIdentifier(string? kind, string toolName, string command)
	{
		// For write kind: always just "write" (applies to all file edits)
		if(kind == "write")
		{
			return "write";
		}

		// For read kind: always just "read" (safe command, no granularity needed)
		if(kind == "read")
		{
			return "read";
		}

		// For shell/execute: return just the executable name (first word)
		if(kind == "shell" || kind == "execute" || toolName.Contains("bash") || toolName.Contains("powershell"))
		{
			string[] parts = command.Trim().Split(' ', StringSplitOptions.RemoveEmptyEntries);
			if(parts.Length > 0)
			{
				return parts[0]; // Just the executable for now
			}
		}

		// Default: use toolName + command
		return $"{toolName} {command}";
	}

	/// <summary>
	/// Check if a tool execution is permitted
	/// </summary>
	/// <param name="sessionId">Session requesting the permission</param>
	/// <param name="toolName">Name of the tool</param>
	/// <param name="command">Command to execute</param>
	/// <param name="arguments">Optional arguments</param>
	/// <param name="kind">Kind of permission (write, read, shell, url)</param>
	/// <param name="isDestructive">Whether the command is destructive</param>
	/// <param name="isYolo">If true, auto-approve all requests</param>
	/// <returns>Permission decision</returns>
	public async Task<PermissionDecision> CheckPermissionAsync(
		string sessionId,
		string toolName,
		string command,
		Dictionary<string, object>? arguments = null,
		string? kind = null,
		bool isYolo = false)
	{
		// YOLO mode - auto-approve everything
		if(isYolo)
		{
			_logger.LogInformation("YOLO mode enabled - auto-approving: {ToolName} {Command}", toolName, command);
			return new PermissionDecision
			{
				IsApproved = true,
				Scope = PermissionScope.Once
			};
		}

		// Normalize the identifier for matching
		string normalizedId = NormalizePermissionIdentifier(kind, toolName, command);

		lock(_permissionsLock)
		{
			// Priority 1: Check session allowlist
			if(_sessionPermissions.TryGetValue(sessionId, out List<ToolPermission>? sessionPerms))
			{
				foreach(ToolPermission permission in sessionPerms.Where(p => p.IsAllowed))
				{
					if(MatchesPattern(normalizedId, permission))
					{
						_logger.LogDebug("Command approved by session permission: {NormalizedId}", normalizedId);
						return new PermissionDecision
						{
							IsApproved = true,
							Scope = PermissionScope.Session
						};
					}
				}
			}

			// Priority 2: Check global allowlist
			if(_globalPermissionFeature.HasPermission(normalizedId))
			{
				_logger.LogDebug("Command approved by global permission: {NormalizedId}", normalizedId);
				return new PermissionDecision
				{
					IsApproved = true,
					Scope = PermissionScope.Global
				};
			}
		}

		// No matching permission found - need user approval
		_logger.LogInformation("No permission found for command, requesting user approval: {NormalizedId}", normalizedId);
		return await RequestUserApprovalAsync(sessionId, toolName, command, arguments, kind);
	}

	/// <summary>
	/// Request user approval for a tool execution
	/// </summary>
	async Task<PermissionDecision> RequestUserApprovalAsync(
		string sessionId,
		string toolName,
		string command,
		Dictionary<string, object>? arguments,
		string? kind)
	{
		// Create pending request
		Models.PermissionRequest request = new()
		{
			SessionId = sessionId,
			ToolName = toolName,
			Command = command,
			Arguments = arguments,
			Kind = kind
		};

		// Store pending request using unique request ID
		_pendingRequests[request.Id] = request;

		// Notify UI
		OnPermissionRequested?.Invoke(sessionId, request);

		// Wait for user decision
		try
		{
			PermissionDecision decision = await request.GetDecisionAsync();
			return decision;
		}
		catch(Exception ex)
		{
			_logger.LogError(ex, "Error waiting for permission decision");
			return new PermissionDecision { IsApproved = false, Reason = "Error requesting permission" };
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
	public void ResolvePermissionRequest(string requestId, PermissionDecision decision)
	{
		_logger.LogInformation("ResolvePermissionRequest called with requestId: {RequestId}, IsApproved: {IsApproved}", requestId, decision.IsApproved);

		if(!_pendingRequests.TryGetValue(requestId, out Models.PermissionRequest? request))
		{
			_logger.LogWarning("No pending permission request found for request ID {RequestId}. Current pending count: {Count}", requestId, _pendingRequests.Count);
			// Log all pending request IDs for debugging
			foreach(var kvp in _pendingRequests)
			{
				_logger.LogDebug("Pending request ID: {Id}, SessionId: {SessionId}", kvp.Key, kvp.Value.SessionId);
			}
			return;
		}

		string sessionId = request.SessionId;

		_logger.LogInformation(
			"Permission {Decision} for session {SessionId}: {Command}",
			decision.IsApproved ? "granted" : "denied",
			sessionId,
			request.Command);

		// If approved, save permission based on scope
		if(decision.IsApproved)
		{
			// Normalize the identifier before storing (similar to Cooper)
			// Pass the scope so normalization can be scope-aware (e.g., global read vs session read)
			string normalizedId = NormalizePermissionIdentifier(request.Kind, request.ToolName, request.Command);

			ToolPermission permission = new()
			{
				Pattern = normalizedId,
				Type = PatternType.Exact,
				IsAllowed = true,
				Scope = decision.Scope
			};

			_logger.LogDebug("Saving permission with scope: {Scope}, normalized ID: {NormalizedId}", decision.Scope, normalizedId);

			if(decision.Scope == PermissionScope.Global)
			{
				_globalPermissionFeature.Add(normalizedId);
				_logger.LogDebug("Added global permission");
			}
			else if(decision.Scope == PermissionScope.Session)
			{
				AddSessionPermission(sessionId, permission);
				_logger.LogDebug("Added session permission");
			}
			// PermissionScope.Once doesn't save anything

			// Check if other pending requests for this session can now be auto-approved
			if(decision.Scope == PermissionScope.Global || decision.Scope == PermissionScope.Session)
			{
				AutoResolveMatchingRequests(sessionId, permission.Pattern, decision.Scope);
			}
		}

		_logger.LogDebug("Completing TaskCompletionSource for session {SessionId}", sessionId);
		// Complete the TaskCompletionSource
		bool completed = request.CompletionSource.TrySetResult(decision);
		_logger.LogDebug("TaskCompletionSource completed: {Completed}", completed);

		// Notify UI with requestId so it can be removed from session list
		OnPermissionResolved?.Invoke(sessionId, request.Id, decision);
	}

	/// <summary>
	/// Add a session-level permission
	/// </summary>
	public void AddSessionPermission(string sessionId, ToolPermission permission)
	{
		permission.Scope = PermissionScope.Session;

		List<ToolPermission> sessionPerms = _sessionPermissions.GetOrAdd(sessionId, _ => []);
		sessionPerms.Add(permission);

		_logger.LogDebug("Added session permission for {SessionId}: {Pattern}", sessionId, permission.Pattern);
	}

	/// <summary>
	/// Check if a command matches a permission pattern
	/// </summary>
	static bool MatchesPattern(string command, ToolPermission permission)
	{
		return permission.Type switch
		{
			PatternType.Exact => command.Equals(permission.Pattern, StringComparison.OrdinalIgnoreCase),
			PatternType.Contains => command.Contains(permission.Pattern, StringComparison.OrdinalIgnoreCase),
			PatternType.StartsWith => command.StartsWith(permission.Pattern, StringComparison.OrdinalIgnoreCase),
			PatternType.Regex => Regex.IsMatch(command, permission.Pattern, RegexOptions.IgnoreCase),
			_ => false
		};
	}

	/// <summary>
	/// Auto-resolve pending requests that match a newly added permission
	/// </summary>
	void AutoResolveMatchingRequests(string sessionId, string command, PermissionScope scope)
	{
		List<Models.PermissionRequest> pendingForSession = [.. _pendingRequests.Values.OrderBy(r => r.Timestamp)];

		_logger.LogInformation("Checking {Count} pending requests for auto-approval with pattern: {Pattern}", pendingForSession.Count, command);

		foreach(Models.PermissionRequest pendingRequest in pendingForSession)
		{
			// Skip if already removed (in case of concurrent processing)
			if(!_pendingRequests.ContainsKey(pendingRequest.Id))
			{
				continue;
			}

			string normalizedId = NormalizePermissionIdentifier(pendingRequest.Kind, pendingRequest.ToolName, pendingRequest.Command);

			if(!normalizedId.Equals(command))
			{
				continue;
			}

			if(scope.Equals(PermissionScope.Session) && pendingRequest.SessionId != sessionId)
			{
				continue;
			}

			_logger.LogInformation("Auto-approving pending request {RequestId} ({Command}) - matches {command}",
				pendingRequest.Id, pendingRequest.Command, command);

			// Auto-approve with the same scope as the permission that matched
			PermissionDecision autoDecision = new()
			{
				IsApproved = true,
				Scope = scope
			};

			// Remove from pending requests atomically before completing
			if(_pendingRequests.TryRemove(pendingRequest.Id, out _))
			{
				// Complete the TaskCompletionSource to unblock the waiting permission check
				pendingRequest.CompletionSource.TrySetResult(autoDecision);

				// Notify UI that this request was resolved
				OnPermissionResolved?.Invoke(sessionId, pendingRequest.Id, autoDecision);
			}
		}
	}
}
