using Cockpit.Models;
using Cockpit.Services;

namespace Cockpit.Features.Permissions;

public class SessionPermissionFeature
{
	readonly ISessionStateProvider _sessionStateProvider;

	public SessionPermissionFeature(ISessionStateProvider sessionStateProvider)
	{
		_sessionStateProvider = sessionStateProvider;
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
			_sessionStateProvider.NotifyStateChanged();
		}
	}
}
