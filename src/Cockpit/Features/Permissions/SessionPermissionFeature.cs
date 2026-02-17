using System.Collections.Concurrent;

namespace Cockpit.Features.Permissions;

public class SessionPermissionFeature
{
	readonly ConcurrentDictionary<string, ConcurrentBag<string>> _commands = new();

	public bool HasPermission(string sessionId, string command)
	{
		if(_commands.TryGetValue(sessionId, out ConcurrentBag<string>? sessionPerms))
		{
			return sessionPerms.Contains(command);
		}
		return false;
	}

	public bool HasPermissions(string sessionId, List<string> commands)
	{
		if(_commands.TryGetValue(sessionId, out ConcurrentBag<string>? sessionPerms))
		{
			return commands.All(cmd => sessionPerms.Contains(cmd));
		}
		return false;
	}

	public void Add(string sessionId, string command)
	{
		ConcurrentBag<string> sessionPerms = _commands.GetOrAdd(sessionId, _ => []);
		sessionPerms.Add(command);
	}

	public void Add(string sessionId, List<string> commands)
	{
		ConcurrentBag<string> sessionPerms = _commands.GetOrAdd(sessionId, _ => []);
		foreach(string command in commands)
		{
			sessionPerms.Add(command);
		}
	}
}
