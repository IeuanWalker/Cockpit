using System.Collections.Concurrent;
using System.Text.Json;
using System.Text.RegularExpressions;
using Cockpit.Models;
using Microsoft.Extensions.Logging;

namespace Cockpit.Services;

/// <summary>
/// Service for managing tool execution permissions
/// </summary>
public class PermissionService
{
	readonly ILogger<PermissionService> _logger;
	readonly string _permissionsFilePath;

	// In-memory cache of permissions
	readonly List<ToolPermission> _globalPermissions = [];
	readonly ConcurrentDictionary<string, List<ToolPermission>> _sessionPermissions = new();
	readonly ConcurrentDictionary<string, PermissionRequest> _pendingRequests = new(); // Key: request.Id
	readonly Lock _permissionsLock = new();

	// Events for UI updates
	public event Action<string, PermissionRequest>? OnPermissionRequested;
	public event Action<string, string, PermissionDecision>? OnPermissionResolved; // sessionId, requestId, decision
	public event Action? OnPermissionsChanged;

	public PermissionService(ILogger<PermissionService> logger)
	{
		_logger = logger;

		// Store permissions in app data folder
		string appDataFolder = FileSystem.AppDataDirectory;
		_permissionsFilePath = Path.Combine(appDataFolder, "permissions.json");

		// Load existing permissions
		LoadPermissions();
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
			foreach(ToolPermission permission in _globalPermissions.Where(p => p.IsAllowed))
			{
				if(MatchesPattern(normalizedId, permission))
				{
					_logger.LogDebug("Command approved by global permission: {NormalizedId}", normalizedId);
					return new PermissionDecision
					{
						IsApproved = true,
						Scope = PermissionScope.Global
					};
				}
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

		if(!_pendingRequests.TryGetValue(requestId, out PermissionRequest? request))
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

			if(decision.Scope == PermissionScope.Global)
			{
				AddGlobalPermission(permission);
			}
			else if(decision.Scope == PermissionScope.Session)
			{
				AddSessionPermission(sessionId, permission);
			}
			// PermissionScope.Once doesn't save anything
		}

		// Complete the TaskCompletionSource
		request.CompletionSource.TrySetResult(decision);

		// Notify UI with requestId so it can be removed from session list
		OnPermissionResolved?.Invoke(sessionId, request.Id, decision);
	}

	/// <summary>
	/// Get pending permission request by request ID
	/// </summary>
	public PermissionRequest? GetPendingRequest(string requestId)
	{
		_pendingRequests.TryGetValue(requestId, out PermissionRequest? request);
		return request;
	}

	/// <summary>
	/// Get all pending permission requests for a specific session (for UI isolation)
	/// </summary>
	public List<PermissionRequest> GetPendingRequestsForSession(string sessionId)
	{
		return _pendingRequests.Values
			.Where(r => r.SessionId == sessionId)
			.OrderBy(r => r.Timestamp)
			.ToList();
	}

	/// <summary>
	/// Add a global permission
	/// </summary>
	public void AddGlobalPermission(ToolPermission permission)
	{
		lock(_permissionsLock)
		{
			permission.Scope = PermissionScope.Global;
			_globalPermissions.Add(permission);
			SavePermissions();
		}

		OnPermissionsChanged?.Invoke();
	}

	/// <summary>
	/// Remove a global permission
	/// </summary>
	public void RemoveGlobalPermission(ToolPermission permission)
	{
		lock(_permissionsLock)
		{
			_globalPermissions.Remove(permission);
			SavePermissions();
		}

		OnPermissionsChanged?.Invoke();
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
	/// Clear session permissions when session ends
	/// </summary>
	public void ClearSessionPermissions(string sessionId)
	{
		if(_sessionPermissions.TryRemove(sessionId, out _))
		{
			_logger.LogDebug("Cleared session permissions for {SessionId}", sessionId);
		}
	}

	/// <summary>
	/// Get all global permissions
	/// </summary>
	public List<ToolPermission> GetGlobalPermissions()
	{
		lock(_permissionsLock)
		{
			return [.. _globalPermissions];
		}
	}

	/// <summary>
	/// Get session permissions
	/// </summary>
	public List<ToolPermission> GetSessionPermissions(string sessionId)
	{
		if(_sessionPermissions.TryGetValue(sessionId, out List<ToolPermission>? perms))
		{
			return [.. perms];
		}

		return [];
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
	/// Load permissions from file
	/// </summary>
	void LoadPermissions()
	{
		try
		{
			if(!File.Exists(_permissionsFilePath))
			{
				_logger.LogInformation("No permissions file found, starting with empty permissions");
				return;
			}

			string json = File.ReadAllText(_permissionsFilePath);
			PermissionsFile? file = JsonSerializer.Deserialize<PermissionsFile>(json);

			if(file is not null)
			{
				lock(_permissionsLock)
				{
					_globalPermissions.Clear();
					// Only load allowlist - denylist removed as per Cooper's approach
					_globalPermissions.AddRange(file.GlobalAllowlist ?? []);
				}

				_logger.LogInformation(
					"Loaded {Count} global permissions from {Path}",
					_globalPermissions.Count,
					_permissionsFilePath);
			}
		}
		catch(Exception ex)
		{
			_logger.LogError(ex, "Failed to load permissions from {Path}", _permissionsFilePath);
		}
	}

	/// <summary>
	/// Save permissions to file
	/// </summary>
	void SavePermissions()
	{
		try
		{
			lock(_permissionsLock)
			{
				PermissionsFile file = new()
				{
					Version = "1.0",
					// Only save allowlist - denylist removed
					GlobalAllowlist = [.. _globalPermissions.Where(p => p.IsAllowed)]
				};

				string json = JsonSerializer.Serialize(file, new JsonSerializerOptions
				{
					WriteIndented = true
				});

				File.WriteAllText(_permissionsFilePath, json);

				_logger.LogDebug("Saved permissions to {Path}", _permissionsFilePath);
			}
		}
		catch(Exception ex)
		{
			_logger.LogError(ex, "Failed to save permissions to {Path}", _permissionsFilePath);
		}
	}

	/// <summary>
	/// File format for storing permissions
	/// </summary>
	class PermissionsFile
	{
		public string Version { get; set; } = "1.0";
		public List<ToolPermission>? GlobalAllowlist { get; set; }
	}
}
