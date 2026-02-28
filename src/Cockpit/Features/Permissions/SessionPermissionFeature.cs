using System.Text.Json;
using Cockpit.Features.Sessions;
using Cockpit.Features.Sessions.Models;
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

	SessionModel? GetSession(string sessionId)
	{
		return _sessionStateProvider.Sessions.FirstOrDefault(s => s.Id == sessionId);
	}

	static string? GetCommandsFilePath(SessionModel session)
	{
		if(string.IsNullOrWhiteSpace(session.Context.WorkspacePath))
		{
			return null;
		}

		return Path.Combine(session.Context.WorkspacePath, "Cockpit", "session-commands.json");
	}

	public static bool TryRestoreSessionCommands(SessionModel session, ILogger logger)
	{
		string? commandsFilePath = GetCommandsFilePath(session);
		if(string.IsNullOrWhiteSpace(commandsFilePath))
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

			lock(session.Context.SessionPermissionCommandsLock)
			{
				session.Context.SessionPermissionCommands.Clear();
				if(commands is not null)
				{
					foreach(string command in commands)
					{
						if(!session.Context.SessionPermissionCommands.Contains(command))
						{
							session.Context.SessionPermissionCommands.Add(command);
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

	void SaveSessionCommands(SessionModel session)
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
			lock(session.Context.SessionPermissionCommandsLock)
			{
				commandsSnapshot = [.. session.Context.SessionPermissionCommands];
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
		SessionModel? session = GetSession(sessionId);
		if(session is null)
		{
			return false;
		}

		lock(session.Context.SessionPermissionCommandsLock)
		{
			return session.Context.SessionPermissionCommands.Contains(command);
		}
	}

	public bool HasPermissions(string sessionId, List<string> commands)
	{
		SessionModel? session = GetSession(sessionId);
		if(session is null)
		{
			return false;
		}

		lock(session.Context.SessionPermissionCommandsLock)
		{
			return commands.All(cmd => session.Context.SessionPermissionCommands.Contains(cmd));
		}
	}

	public void Add(string sessionId, string command)
	{
		SessionModel? session = GetSession(sessionId);
		if(session is null)
		{
			return;
		}

		bool modified = false;
		lock(session.Context.SessionPermissionCommandsLock)
		{
			if(!session.Context.SessionPermissionCommands.Contains(command))
			{
				session.Context.SessionPermissionCommands.Add(command);
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
		SessionModel? session = GetSession(sessionId);
		if(session is null)
		{
			return;
		}

		bool modified = false;
		lock(session.Context.SessionPermissionCommandsLock)
		{
			foreach(string command in commands)
			{
				if(!session.Context.SessionPermissionCommands.Contains(command))
				{
					session.Context.SessionPermissionCommands.Add(command);
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
		SessionModel? session = GetSession(sessionId);
		if(session is null)
		{
			return;
		}

		bool modified = false;
		lock(session.Context.SessionPermissionCommandsLock)
		{
			modified = session.Context.SessionPermissionCommands.Remove(command);
		}

		if(modified)
		{
			SaveSessionCommands(session);
			_sessionStateProvider.NotifyStateChanged();
		}
	}

	public List<string> GetAll(string sessionId)
	{
		SessionModel? session = GetSession(sessionId);
		if(session is null)
		{
			return [];
		}

		lock(session.Context.SessionPermissionCommandsLock)
		{
			return [.. session.Context.SessionPermissionCommands.Order()];
		}
	}

	public void Clear(string sessionId)
	{
		SessionModel? session = GetSession(sessionId);
		if(session is null)
		{
			return;
		}

		bool modified = false;
		lock(session.Context.SessionPermissionCommandsLock)
		{
			if(session.Context.SessionPermissionCommands.Count > 0)
			{
				session.Context.SessionPermissionCommands.Clear();
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
