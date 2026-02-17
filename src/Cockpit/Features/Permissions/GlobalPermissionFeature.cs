using System.Text.Json;
using Microsoft.Extensions.Logging;

namespace Cockpit.Features.Permissions;

public sealed class GlobalPermissionFeature
{
	readonly ILogger<GlobalPermissionFeature> _logger;
	readonly string _permissionsFilePath;

	public GlobalPermissionFeature(ILogger<GlobalPermissionFeature> logger)
	{
		_logger = logger;

		_permissionsFilePath = Path.Combine(FileSystem.AppDataDirectory, "global-commands.json");

		Load();
	}

	readonly List<string> _commands = [];
	readonly Lock _permissionsLock = new();
	public event Action? OnPermissionsChanged;

	public bool HasPermissions(List<string> commands)
	{
		lock(_permissionsLock)
		{
			return commands.All(cmd => _commands.Contains(cmd));
		}
	}

	public List<string> GetAll()
	{
		lock(_permissionsLock)
		{
			return [.. _commands];
		}
	}

	public void Add(string command)
	{
		lock(_permissionsLock)
		{
			if(!_commands.Contains(command))
			{
				_commands.Add(command);

				Save();
			}
		}

		OnPermissionsChanged?.Invoke();
	}

	public void Add(List<string> commands)
	{
		lock(_permissionsLock)
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

		OnPermissionsChanged?.Invoke();
	}

	public void Remove(string command)
	{
		lock(_permissionsLock)
		{
			_commands.Remove(command);

			Save();
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
				lock(_permissionsLock)
				{
					_commands.Clear();
					// Only load allowlist - denylist removed as per Cooper's approach
					_commands.AddRange(file);
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
			lock(_permissionsLock)
			{
				string json = JsonSerializer.Serialize(_commands, new JsonSerializerOptions
				{
					WriteIndented = true
				});

				File.WriteAllText(_permissionsFilePath, json);

				_logger.LogDebug("Saved permissions to {Path}", _permissionsFilePath);
			}
		}
		catch(Exception ex)
		{
			_logger.LogError(ex, "Failed to save permissions to {Path}", _permissionsFilePath);
		}
	}
}