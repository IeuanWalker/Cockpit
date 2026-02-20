using System.Text.Json;
using Cockpit.Features.Sessions;
using Cockpit.Models;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace Cockpit.Features.Permissions;

public sealed class SessionPermissionFeature
{
	readonly ISessionStateProvider _sessionStateProvider;
	readonly ILogger<SessionPermissionFeature> _logger;

	public SessionPermissionFeature(ISessionStateProvider sessionStateProvider, ILogger<SessionPermissionFeature>? logger = null)
	{
		_sessionStateProvider = sessionStateProvider;
		_logger = logger ?? NullLogger<SessionPermissionFeature>.Instance;
	}

	ChatSession? GetSession(string sessionId)
	{
		return _sessionStateProvider.GetSessions().FirstOrDefault(s => s.Id == sessionId);
	}

	static SessionContext GetOrCreateContext(ChatSession session)
	{
		session.Context ??= SessionContext.CreateDefault(session.WorkingDirectory ?? session.WorkspacePath);
		return session.Context;
	}

	static string? GetCommandsFilePath(ChatSession session)
	{
		if(string.IsNullOrWhiteSpace(session.WorkspacePath))
		{
			return null;
		}

		return Path.Combine(session.WorkspacePath, "Cockpit", "session-commands.json");
	}

	public static bool TryRestoreSessionCommands(ChatSession session, ILogger logger)
	{
		string? commandsFilePath = GetCommandsFilePath(session);
		if(string.IsNullOrWhiteSpace(commandsFilePath))
		{
			return false;
		}

		string? commandsDirectory = Path.GetDirectoryName(commandsFilePath);
		if(string.IsNullOrWhiteSpace(commandsDirectory) || !Directory.Exists(commandsDirectory))
		{
			return false;
		}

		if(!File.Exists(commandsFilePath))
		{
			return false;
		}

		try
		{
			string json = File.ReadAllText(commandsFilePath);
			List<string>? commands = JsonSerializer.Deserialize<List<string>>(json);

			SessionContext context = GetOrCreateContext(session);
			lock(context.SessionPermissionCommandsLock)
			{
				context.SessionPermissionCommands.Clear();
				if(commands is not null)
				{
					foreach(string command in commands)
					{
						if(!context.SessionPermissionCommands.Contains(command))
						{
							context.SessionPermissionCommands.Add(command);
						}
					}
				}
			}

			return true;
		}
		catch(Exception ex)
		{
			logger.LogWarning(ex, "Failed to restore session commands for session {SessionId} from {Path}", session.Id, commandsFilePath);
			return false;
		}
	}

	void SaveSessionCommands(ChatSession session)
	{
		string? commandsFilePath = GetCommandsFilePath(session);
		if(string.IsNullOrWhiteSpace(commandsFilePath))
		{
			return;
		}

		try
		{
			string? commandsDirectory = Path.GetDirectoryName(commandsFilePath);
			if(string.IsNullOrWhiteSpace(commandsDirectory))
			{
				return;
			}

			Directory.CreateDirectory(commandsDirectory);

			List<string> commandsSnapshot;
			SessionContext context = GetOrCreateContext(session);
			lock(context.SessionPermissionCommandsLock)
			{
				commandsSnapshot = [.. context.SessionPermissionCommands];
			}

			string json = JsonSerializer.Serialize(commandsSnapshot, new JsonSerializerOptions
			{
				WriteIndented = true
			});

			File.WriteAllText(commandsFilePath, json);
		}
		catch(Exception ex)
		{
			_logger.LogWarning(ex, "Failed to persist session commands for session {SessionId}", session.Id);
		}
	}

	public bool HasPermission(string sessionId, string command)
	{
		ChatSession? session = GetSession(sessionId);
		if(session is null)
		{
			return false;
		}

		SessionContext context = GetOrCreateContext(session);
		lock(context.SessionPermissionCommandsLock)
		{
			return context.SessionPermissionCommands.Contains(command);
		}
	}

	public bool HasPermissions(string sessionId, List<string> commands)
	{
		ChatSession? session = GetSession(sessionId);
		if(session is null)
		{
			return false;
		}

		SessionContext context = GetOrCreateContext(session);
		lock(context.SessionPermissionCommandsLock)
		{
			return commands.All(cmd => context.SessionPermissionCommands.Contains(cmd));
		}
	}

	public void Add(string sessionId, string command)
	{
		ChatSession? session = GetSession(sessionId);
		if(session is null)
		{
			return;
		}

		bool modified = false;
		SessionContext context = GetOrCreateContext(session);
		lock(context.SessionPermissionCommandsLock)
		{
			if(!context.SessionPermissionCommands.Contains(command))
			{
				context.SessionPermissionCommands.Add(command);
				modified = true;
			}
		}

		if(modified)
		{
			SaveSessionCommands(session);
			_sessionStateProvider.NotifyStateChanged();
		}
	}

	public void Add(string sessionId, List<string> commands)
	{
		ChatSession? session = GetSession(sessionId);
		if(session is null)
		{
			return;
		}

		bool modified = false;
		SessionContext context = GetOrCreateContext(session);
		lock(context.SessionPermissionCommandsLock)
		{
			foreach(string command in commands)
			{
				if(!context.SessionPermissionCommands.Contains(command))
				{
					context.SessionPermissionCommands.Add(command);
					modified = true;
				}
			}
		}

		if(modified)
		{
			SaveSessionCommands(session);
			_sessionStateProvider.NotifyStateChanged();
		}
	}

	public void Remove(string sessionId, string command)
	{
		ChatSession? session = GetSession(sessionId);
		if(session is null)
		{
			return;
		}

		bool modified = false;
		SessionContext context = GetOrCreateContext(session);
		lock(context.SessionPermissionCommandsLock)
		{
			modified = context.SessionPermissionCommands.Remove(command);
		}

		if(modified)
		{
			SaveSessionCommands(session);
			_sessionStateProvider.NotifyStateChanged();
		}
	}

	public List<string> GetAll(string sessionId)
	{
		ChatSession? session = GetSession(sessionId);
		if(session is null)
		{
			return [];
		}

		SessionContext context = GetOrCreateContext(session);
		lock(context.SessionPermissionCommandsLock)
		{
			return [.. context.SessionPermissionCommands];
		}
	}

	public void Clear(string sessionId)
	{
		ChatSession? session = GetSession(sessionId);
		if(session is null)
		{
			return;
		}

		bool modified = false;
		SessionContext context = GetOrCreateContext(session);
		lock(context.SessionPermissionCommandsLock)
		{
			if(context.SessionPermissionCommands.Count > 0)
			{
				context.SessionPermissionCommands.Clear();
				modified = true;
			}
		}

		if(modified)
		{
			SaveSessionCommands(session);
			_sessionStateProvider.NotifyStateChanged();
		}
	}
}
