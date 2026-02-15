using Cockpit.Models;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.Logging;

namespace Cockpit.Components;

public partial class PermissionRequestPanel
{
	[Parameter, EditorRequired]
	public required PermissionRequest Request { get; set; }

	[Parameter]
	public EventCallback<PermissionDecision> OnPermissionDecision { get; set; }

	[Inject]
	public required ILogger<PermissionRequestPanel> Logger { get; set; }

	bool _showDetailsPopup = false;

	string GetNormalizedPattern()
	{
		// Match the normalization logic from PermissionService
		string? kind = Request.Kind;
		string toolName = Request.ToolName;
		string command = Request.Command;

		// For write kind: always just "write"
		if(kind == "write")
		{
			return "write";
		}

		// For read kind: always just "read"
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
				return parts[0]; // Just the executable
			}
		}

		// Default: use toolName + command
		return $"{toolName} {command}";
	}

	string GetPermissionMessage()
	{
		// Show specific details when available
		string? kind = Request.Kind;

		// Try to extract file path for write/read operations
		if(Request.Arguments != null)
		{
			if(Request.Arguments.TryGetValue("path", out object? pathObj))
			{
				string path = pathObj?.ToString() ?? "";
				if(!string.IsNullOrEmpty(path))
				{
					if(kind == "write" || kind == "edit" || kind == "create")
					{
						return $"Allow editing {Path.GetFileName(path)}?";
					}
					if(kind == "read")
					{
						return $"Allow reading {Path.GetFileName(path)}?";
					}
				}
			}
		}

		// For shell commands, show the actual command
		if(kind == "shell" || kind == "execute" || Request.ToolName.Contains("powershell") || Request.ToolName.Contains("bash"))
		{
			// Extract just the executable for the message
			string[] parts = Request.Command.Trim().Split(' ', StringSplitOptions.RemoveEmptyEntries);
			if(parts.Length > 0)
			{
				return $"Allow running {parts[0]}?";
			}
		}

		// Fallback to generic messages
		return Request.ToolName.ToLowerInvariant() switch
		{
			"write" or "edit" or "create" => "Allow file changes?",
			"bash" or "powershell" or "cmd" or "shell" => "Allow terminal commands?",
			"execute" => "Allow command execution?",
			"git" => "Allow git operations?",
			"read" => "Allow file access?",
			_ => $"Allow {Request.ToolName}?"
		};
	}

	async Task OnDecision(bool isApproved, PermissionScope scope)
	{
		Logger.LogInformation("OnDecision called: isApproved={IsApproved}, scope={Scope}, sessionId={SessionId}", 
			isApproved, scope, Request.SessionId);

		PermissionDecision decision = new()
		{
			IsApproved = isApproved,
			Scope = scope
		};

		Logger.LogInformation("Invoking OnPermissionDecision callback");
		await OnPermissionDecision.InvokeAsync(decision);
		Logger.LogInformation("OnPermissionDecision callback completed");
	}
}