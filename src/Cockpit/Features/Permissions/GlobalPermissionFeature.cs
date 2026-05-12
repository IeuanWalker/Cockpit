using Cockpit.Extensions;
using Microsoft.Extensions.Logging;

namespace Cockpit.Features.Permissions;

public sealed class GlobalPermissionFeature : IDisposable
{
	readonly ILogger<GlobalPermissionFeature> _logger;
	readonly string _permissionsFilePath;
	readonly bool _useDefaults;

	public GlobalPermissionFeature(ILogger<GlobalPermissionFeature> logger, string? permissionsFilePath = null)
	{
		_logger = logger;
		_useDefaults = permissionsFilePath is null;
		_permissionsFilePath = permissionsFilePath ?? Path.Combine(FileSystem.AppDataDirectory, "global-commands.json");

		Load();
	}

	readonly HashSet<string> _commands = new(StringComparer.Ordinal);
	readonly ReaderWriterLockSlim _permissionsLock = new();
	public event Action? OnPermissionsChanged;

	public bool HasPermission(string command)
	{
		_permissionsLock.EnterReadLock();
		try
		{
			return _commands.Contains(command);
		}
		finally
		{
			_permissionsLock.ExitReadLock();
		}
	}

	public bool HasPermissions(List<string> commands)
	{
		_permissionsLock.EnterReadLock();
		try
		{
			return commands.All(cmd => _commands.Contains(cmd));
		}
		finally
		{
			_permissionsLock.ExitReadLock();
		}
	}

	public List<string> GetAll()
	{
		_permissionsLock.EnterReadLock();
		try
		{
			return [.. _commands.Order()];
		}
		finally
		{
			_permissionsLock.ExitReadLock();
		}
	}

	public void Add(string command)
	{
		List<string>? snapshot = null;

		_permissionsLock.EnterWriteLock();
		try
		{
			if(_commands.Add(command))
			{
				snapshot = [.. _commands];
			}
		}
		finally
		{
			_permissionsLock.ExitWriteLock();
		}

		if(snapshot is not null)
		{
			Save(snapshot);
			OnPermissionsChanged?.Invoke();
		}
	}

	public void Add(List<string> commands)
	{
		List<string>? snapshot = null;

		_permissionsLock.EnterWriteLock();
		try
		{
			int before = _commands.Count;
			foreach(string command in commands)
			{
				_commands.Add(command);
			}

			if(_commands.Count > before)
			{
				snapshot = [.. _commands];
			}
		}
		finally
		{
			_permissionsLock.ExitWriteLock();
		}

		if(snapshot is not null)
		{
			Save(snapshot);
			OnPermissionsChanged?.Invoke();
		}
	}

	public void Remove(string command)
	{
		List<string>? snapshot = null;

		_permissionsLock.EnterWriteLock();
		try
		{
			if(_commands.Remove(command))
			{
				snapshot = [.. _commands];
			}
		}
		finally
		{
			_permissionsLock.ExitWriteLock();
		}

		if(snapshot is not null)
		{
			Save(snapshot);
			OnPermissionsChanged?.Invoke();
		}
	}

	/// <summary>
	/// Load permissions from file
	/// </summary>
	void Load()
	{
		try
		{
			if(!File.Exists(_permissionsFilePath))
			{
				if(_useDefaults)
				{
					// Default allow list
					List<string> defaultCommands =
					[
						// Git read-only subcommands (extracted as "git <subcommand>" by CommandExtractor)
						"git status", "git log", "git diff", "git branch", "git show",
						"git remote", "git tag", "git describe",
						// npm info subcommands
						"npm list", "npm ls", "npm outdated",
						// dotnet info subcommand
						"dotnet list",
					];

					_permissionsLock.EnterWriteLock();
					try
					{
						_commands.Clear();
						_commands.UnionWith(defaultCommands);
					}
					finally
					{
						_permissionsLock.ExitWriteLock();
					}

					_logger.LogInformation("Default global permissions loaded");
				}

				return;
			}

			string json = File.ReadAllText(_permissionsFilePath);
			List<string>? file = json.DeserializeJson<List<string>>();

			if(file is not null)
			{
				_permissionsLock.EnterWriteLock();
				try
				{
					_commands.Clear();
					// Only load allowlist - denylist removed as per Cooper's approach
					_commands.UnionWith(file);
				}
				finally
				{
					_permissionsLock.ExitWriteLock();
				}

				_logger.LogInformation("Loaded {Count} global permissions from {Path}", _commands.Count, _permissionsFilePath);
			}
		}
		catch(Exception ex)
		{
			_logger.LogError(ex, "Failed to load permissions from {Path}", _permissionsFilePath);
		}
	}

	void Save(List<string> snapshot)
	{
		try
		{
			string json = snapshot.SerializeJson()!;
			File.WriteAllText(_permissionsFilePath, json);
			_logger.LogDebug("Saved permissions to {Path}", _permissionsFilePath);
		}
		catch(Exception ex)
		{
			_logger.LogError(ex, "Failed to save permissions to {Path}", _permissionsFilePath);
		}
	}


	public void Dispose()
	{
		_permissionsLock.Dispose();
	}
}