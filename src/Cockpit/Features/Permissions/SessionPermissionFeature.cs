using System.Collections.Concurrent;

namespace Cockpit.Features.Permissions;

public class SessionPermissionFeature
{
	readonly ConcurrentDictionary<string, ConcurrentDictionary<string, byte>> _commands = new();

	public bool HasPermission(string sessionId, string command)
	{
		if(_commands.TryGetValue(sessionId, out ConcurrentDictionary<string, byte>? sessionPerms))
		{
			return sessionPerms.ContainsKey(command);
		}
		return false;
	}

	public bool HasPermissions(string sessionId, List<string> commands)
	{
		if(_commands.TryGetValue(sessionId, out ConcurrentDictionary<string, byte>? sessionPerms))
		{
			return commands.All(cmd => sessionPerms.ContainsKey(cmd));
		}
		return false;
	}

	public void Add(string sessionId, string command)
	{
		ConcurrentDictionary<string, byte> sessionPerms = _commands.GetOrAdd(sessionId, _ => new());
		sessionPerms.TryAdd(command, 0);
	}

	public void Add(string sessionId, List<string> commands)
	{
		ConcurrentDictionary<string, byte> sessionPerms = _commands.GetOrAdd(sessionId, _ => new());
		foreach(string command in commands)
		{
			sessionPerms.TryAdd(command, 0);
		}
	}

	public List<string> GetAll(string sessionId)
	{
		if(_commands.TryGetValue(sessionId, out ConcurrentDictionary<string, byte>? sessionPerms))
		{
			return [.. sessionPerms.Keys];
		}
		return [];
	}

	public void Clear(string sessionId)
	{
		_commands.TryRemove(sessionId, out _);
	}
}
