using System.Text.Json;
using Cockpit.Features.Permissions.Models;
using Cockpit.Features.Sessions.Models;
using GitHub.Copilot.SDK;

namespace Cockpit.Features.Permissions;

public sealed partial class PermissionFeature
{
	/// <summary>
	/// When <see langword="true"/>, <see cref="ToRequestModel"/> throws <see cref="InvalidOperationException"/>
	/// for any <see cref="PermissionRequest"/> subtype not explicitly handled in the switch, instead of
	/// falling back to <see cref="HandleUnknown"/>. Intended for use in unit tests to catch missing handlers.
	/// </summary>
	public bool ThrowOnUnhandledPermissionType { get; set; }

	PermissionRequestModel ToRequestModel(PermissionRequest request, SessionModel session) =>
		request switch
		{
			PermissionRequestShell shell => HandleShell(shell, session),
			PermissionRequestWrite write => HandleWrite(write, session),
			PermissionRequestRead read => HandleRead(read, session),
			PermissionRequestMcp mcp => HandleMcp(mcp, session),
			PermissionRequestMemory memory => HandleMemory(memory, session),
			PermissionRequestCustomTool customTool => HandleCustomTool(customTool, session),
			PermissionRequestHook hook => HandleHook(hook, session),
			PermissionRequestUrl url => HandleUrl(url, session),
			_ => ThrowOnUnhandledPermissionType
				? throw new InvalidOperationException(
					$"No handler for SDK permission type '{request.GetType().Name}' (kind='{request.Kind}'). " +
					$"Add a case to the switch in {nameof(ToRequestModel)}.")
				: HandleUnknown(request, session)
		};

	PermissionRequestModel HandleShell(PermissionRequestShell request, SessionModel session)
	{
		string fullCommand = request.FullCommandText ?? string.Empty;
		string intention = request.Intention ?? string.Empty;

		List<string> meaningfulExecutables = CommandExtractor.ExtractMeaningfulExecutables(fullCommand);
		List<string> commands;
		if(meaningfulExecutables.Count > 0)
		{
			commands = meaningfulExecutables;
		}
		else
		{
			commands = CommandExtractor.ExtractExecutables(fullCommand);
			if(commands.Count == 0)
			{
				commands = [fullCommand.Split(' ', StringSplitOptions.RemoveEmptyEntries).FirstOrDefault() ?? fullCommand];
			}
		}

		string commandList = string.Join("`, `", commands);
		bool isDestructive = CommandExtractor.ContainsDestructiveCommand(fullCommand);
		List<string> filesToDelete = isDestructive ? CommandExtractor.ExtractFilesToDelete(fullCommand) : [];

		string requestTitle = isDestructive
			? $"⚠️ Allow destructive command `{commandList}`"
			: $"Allow running `{commandList}`";

		if(filesToDelete.Count > 0)
		{
			requestTitle += $" (deletes {filesToDelete.Count} file(s))";
		}

		bool canApproveGlobally = !isDestructive && !_globalDenyFeature.AnyDenied(commands);

		return new PermissionRequestModel
		{
			SessionId = session.Id,
			FullCommand = fullCommand,
			Commands = commands,
			RequestTitle = requestTitle,
			Intention = intention,
			CanApproveGlobally = canApproveGlobally,
			CanApproveForSession = true,
			FullRequestJson = JsonSerializer.Serialize(request),
			IsDestructive = isDestructive,
			FilesToDelete = [.. filesToDelete]
		};
	}

	PermissionRequestModel HandleWrite(PermissionRequestWrite request, SessionModel session)
	{
		string path = request.FileName ?? string.Empty;
		string intention = request.Intention ?? string.Empty;

		if(string.IsNullOrWhiteSpace(path))
		{
			return FallbackModel(request, session, intention);
		}

		FilePathCategory category = ClassifyFilePath(path, session.Context.CurrentWorkingDirectory);
		(string title, bool canApproveForSession, bool canApproveGlobally) = FilePathPermissionInfo("write", category);

		return new PermissionRequestModel
		{
			SessionId = session.Id,
			FullCommand = path,
			Commands = [$"write - {category}"],
			RequestTitle = title,
			Intention = intention,
			CanApproveGlobally = canApproveGlobally,
			CanApproveForSession = canApproveForSession,
			FullRequestJson = JsonSerializer.Serialize(request)
		};
	}

	PermissionRequestModel HandleRead(PermissionRequestRead request, SessionModel session)
	{
		string path = request.Path ?? string.Empty;
		string intention = request.Intention ?? string.Empty;

		if(string.IsNullOrWhiteSpace(path))
		{
			return FallbackModel(request, session, intention);
		}

		FilePathCategory category = ClassifyFilePath(path, session.Context.CurrentWorkingDirectory);
		(string title, bool canApproveForSession, bool canApproveGlobally) = FilePathPermissionInfo("read", category);

		return new PermissionRequestModel
		{
			SessionId = session.Id,
			FullCommand = path,
			Commands = [$"read - {category}"],
			RequestTitle = title,
			Intention = intention,
			CanApproveGlobally = canApproveGlobally,
			CanApproveForSession = canApproveForSession,
			FullRequestJson = JsonSerializer.Serialize(request)
		};
	}

	static PermissionRequestModel HandleMcp(PermissionRequestMcp request, SessionModel session)
	{
		string serverName = request.ServerName ?? string.Empty;
		string toolName = request.ToolName ?? string.Empty;
		string displayTitle = !string.IsNullOrWhiteSpace(request.ToolTitle) ? request.ToolTitle : toolName;
		string commandKey = $"{serverName}/{toolName}";

		return new PermissionRequestModel
		{
			SessionId = session.Id,
			FullCommand = commandKey,
			Commands = [commandKey],
			RequestTitle = $"Allow MCP tool `{displayTitle}` on `{serverName}`",
			Intention = string.Empty,
			CanApproveGlobally = request.ReadOnly,
			CanApproveForSession = true,
			FullRequestJson = JsonSerializer.Serialize(request)
		};
	}

	static PermissionRequestModel HandleMemory(PermissionRequestMemory request, SessionModel session)
	{
		string subject = request.Subject ?? string.Empty;

		return new PermissionRequestModel
		{
			SessionId = session.Id,
			FullCommand = subject,
			Commands = ["memory"],
			RequestTitle = string.IsNullOrWhiteSpace(subject)
				? "Allow saving memory?"
				: $"Allow saving memory: `{subject}`",
			Intention = string.Empty,
			CanApproveGlobally = true,
			CanApproveForSession = true,
			FullRequestJson = JsonSerializer.Serialize(request)
		};
	}

	static PermissionRequestModel HandleCustomTool(PermissionRequestCustomTool request, SessionModel session)
	{
		string toolName = request.ToolName ?? string.Empty;

		return new PermissionRequestModel
		{
			SessionId = session.Id,
			FullCommand = toolName,
			Commands = [$"custom:{toolName}"],
			RequestTitle = $"Allow custom tool `{toolName}`",
			Intention = string.Empty,
			CanApproveGlobally = true,
			CanApproveForSession = true,
			FullRequestJson = JsonSerializer.Serialize(request)
		};
	}

	static PermissionRequestModel HandleHook(PermissionRequestHook request, SessionModel session)
	{
		string toolName = request.ToolName ?? string.Empty;
		string hookMessage = request.HookMessage ?? string.Empty;

		return new PermissionRequestModel
		{
			SessionId = session.Id,
			FullCommand = toolName,
			Commands = [$"hook:{toolName}"],
			RequestTitle = string.IsNullOrWhiteSpace(hookMessage)
				? $"Allow hook `{toolName}`"
				: hookMessage,
			Intention = string.Empty,
			CanApproveGlobally = true,
			CanApproveForSession = true,
			FullRequestJson = JsonSerializer.Serialize(request)
		};
	}

	static PermissionRequestModel HandleUrl(PermissionRequestUrl request, SessionModel session)
	{
		string url = request.Url ?? string.Empty;
		string intention = request.Intention ?? string.Empty;

		return new PermissionRequestModel
		{
			SessionId = session.Id,
			FullCommand = url,
			Commands = ["url"],
			RequestTitle = string.IsNullOrWhiteSpace(url)
				? "Allow URL access?"
				: $"Allow accessing `{url}`",
			Intention = intention,
			CanApproveGlobally = true,
			CanApproveForSession = true,
			FullRequestJson = JsonSerializer.Serialize(request)
		};
	}

	static PermissionRequestModel HandleUnknown(PermissionRequest request, SessionModel session) =>
		FallbackModel(request, session, string.Empty);

	static PermissionRequestModel FallbackModel(PermissionRequest request, SessionModel session, string intention) =>
		new()
		{
			SessionId = session.Id,
			FullCommand = string.Empty,
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

	static (string Title, bool CanApproveForSession, bool CanApproveGlobally) FilePathPermissionInfo(string verb, FilePathCategory category) =>
		category switch
		{
			FilePathCategory.CopilotSession => ($"Allow {verb} in copilot session file", true, true),
			FilePathCategory.WorkingDirectory => ($"Allow {verb} in current working directory", true, true),
			_ => ($"Allow {verb} in file outside of current working directory", true, false)
		};

	/// <summary>
	/// Categories for file path classification in read/write permission requests.
	/// </summary>
	enum FilePathCategory
	{
		/// <summary>File is inside a .copilot sub-directory (internal agent session state).</summary>
		CopilotSession,
		/// <summary>File is inside the session's working directory.</summary>
		WorkingDirectory,
		/// <summary>File is outside the session's working directory.</summary>
		External
	}

	/// <summary>
	/// Classifies a file path relative to the session's working directory.
	/// </summary>
	static FilePathCategory ClassifyFilePath(string filePath, string workingDirectory)
	{
		if(string.IsNullOrWhiteSpace(filePath))
		{
			return FilePathCategory.External;
		}

		string[] segments = filePath.Split(['\\', '/'], StringSplitOptions.RemoveEmptyEntries);
		foreach(string segment in segments)
		{
			if(string.Equals(segment, ".copilot", StringComparison.Ordinal))
			{
				return FilePathCategory.CopilotSession;
			}
		}

		if(string.IsNullOrWhiteSpace(workingDirectory))
		{
			return FilePathCategory.External;
		}

		if(filePath.StartsWith(workingDirectory, StringComparison.OrdinalIgnoreCase))
		{
			return FilePathCategory.WorkingDirectory;
		}

		return FilePathCategory.External;
	}
}
