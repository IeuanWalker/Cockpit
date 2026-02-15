using System.Collections.Concurrent;
using System.Text.RegularExpressions;
using Cockpit.Features;
using Cockpit.Models;
using Microsoft.Extensions.Logging;

namespace Cockpit.Services;

/// <summary>
/// Service for managing tool execution permissions
/// </summary>
public class PermissionService
{
	readonly GlobalPermissionFeature _globalPermissionFeature;
	readonly ILogger<PermissionService> _logger;


	// In-memory cache of permissions
	readonly ConcurrentDictionary<string, List<ToolPermission>> _sessionPermissions = new();
	readonly ConcurrentDictionary<string, PermissionRequest> _pendingRequests = new();
	readonly Lock _permissionsLock = new();

	// Events for UI updates
	public event Action<string, PermissionRequest>? OnPermissionRequested;
	public event Action<string, PermissionDecision>? OnPermissionResolved;

	public PermissionService(GlobalPermissionFeature globalPermissionFeature, ILogger<PermissionService> logger)
	{
		_globalPermissionFeature = globalPermissionFeature;
		_logger = logger;

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
		PermissionRequest request = new()
		{
			SessionId = sessionId,
			ToolName = toolName,
			Command = command,
			Arguments = arguments,
			Kind = kind
		};

		// Store pending request
		_pendingRequests[sessionId] = request;

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
			// Clean up pending request
			_pendingRequests.TryRemove(sessionId, out _);
		}
	}

	/// <summary>
	/// Resolve a pending permission request with user decision
	/// </summary>
	public void ResolvePermissionRequest(string sessionId, PermissionDecision decision)
	{
		if(!_pendingRequests.TryGetValue(sessionId, out PermissionRequest? request))
		{
			_logger.LogWarning("No pending permission request found for session {SessionId}", sessionId);
			return;
		}

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
		}

		_logger.LogDebug("Completing TaskCompletionSource for session {SessionId}", sessionId);
		// Complete the TaskCompletionSource
		bool completed = request.CompletionSource.TrySetResult(decision);
		_logger.LogDebug("TaskCompletionSource completed: {Completed}", completed);

		// Notify UI
		OnPermissionResolved?.Invoke(sessionId, decision);
		_logger.LogDebug("OnPermissionResolved event fired");
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
}
