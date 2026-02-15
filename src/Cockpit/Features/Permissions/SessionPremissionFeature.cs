using System.Collections.Concurrent;

namespace Cockpit.Features.Permissions;

public class SessionPermissionFeature
{
	readonly ConcurrentDictionary<string, List<string>> _commands = new();

	public bool HasPermission(string sessionId, string command)
	{
		if(_commands.TryGetValue(sessionId, out List<string>? sessionPerms))
		{
			return sessionPerms.Contains(command);
		}
		return false;
	}
	public void Add(string sessionId, string command)
	{
		List<string> sessionPerms = _commands.GetOrAdd(sessionId, _ => []);
		sessionPerms.Add(command);
	}
}
