using System.Text.Json;
using Microsoft.Extensions.Logging;

namespace Cockpit.Features.Permissions;

public sealed class GlobalPermissionFeature : IDisposable
{
	readonly ILogger<GlobalPermissionFeature> _logger;
	readonly string _permissionsFilePath;

	public GlobalPermissionFeature(ILogger<GlobalPermissionFeature> logger, string? permissionsFilePath = null)
	{
		_logger = logger;

		_permissionsFilePath = permissionsFilePath ?? Path.Combine(FileSystem.AppDataDirectory, "global-commands.json");

		Load();
	}

	readonly List<string> _commands = [];
	readonly ReaderWriterLockSlim _permissionsLock = new();
	public event Action? OnPermissionsChanged;

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
			return [.. _commands];
		}
		finally
		{
			_permissionsLock.ExitReadLock();
		}
	}

	public void Add(string command)
	{
		_permissionsLock.EnterWriteLock();
		try
		{
			if(!_commands.Contains(command))
			{
				_commands.Add(command);

				Save();
			}
		}
		finally
		{
			_permissionsLock.ExitWriteLock();
		}

		OnPermissionsChanged?.Invoke();
	}

	public void Add(List<string> commands)
	{
		_permissionsLock.EnterWriteLock();
		try
		{
			bool modified = false;
			foreach(string command in commands)
			{
				if(!_commands.Contains(command))
				{
					_commands.Add(command);
					modified = true;
				}
			}

			if(modified)
			{
				Save();
			}
		}
		finally
		{
			_permissionsLock.ExitWriteLock();
		}

		OnPermissionsChanged?.Invoke();
	}

	public void Remove(string command)
	{
		_permissionsLock.EnterWriteLock();
		try
		{
			_commands.Remove(command);

			Save();
		}
		finally
		{
			_permissionsLock.ExitWriteLock();
		}

		OnPermissionsChanged?.Invoke();
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
				_logger.LogInformation("No permissions file found, starting with empty permissions");
				return;
			}

			string json = File.ReadAllText(_permissionsFilePath);
			List<string>? file = JsonSerializer.Deserialize<List<string>>(json);

			if(file is not null)
			{
				_permissionsLock.EnterWriteLock();
				try
				{
					_commands.Clear();
					// Only load allowlist - denylist removed as per Cooper's approach
					_commands.AddRange(file);
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

	void Save()
	{
		try
		{
			// Lock is already held by caller (Add/Remove methods)
			// This method should only be called from within a write lock
			string json = JsonSerializer.Serialize(_commands, new JsonSerializerOptions
			{
				WriteIndented = true
			});

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
		_permissionsLock?.Dispose();
	}
}