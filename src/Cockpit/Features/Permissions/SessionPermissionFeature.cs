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

	public void Add(string sessionId, string command)
	{
		ConcurrentBag<string> sessionPerms = _commands.GetOrAdd(sessionId, _ => []);
		sessionPerms.Add(command);
	}
}
