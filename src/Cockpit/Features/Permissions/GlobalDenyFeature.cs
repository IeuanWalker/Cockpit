using System.Text.Json;
using Microsoft.Extensions.Logging;

namespace Cockpit.Features.Permissions;

/// <summary>
/// Manages the global deny list — commands that are never allowed to be globally approved.
/// When a command is on this list the "Allow globally" option is suppressed in the UI.
/// </summary>
public sealed class GlobalDenyFeature : IDisposable
{
	readonly ILogger<GlobalDenyFeature> _logger;
	readonly string _denyFilePath;

	public GlobalDenyFeature(ILogger<GlobalDenyFeature> logger, string? denyFilePath = null)
	{
		_logger = logger;
		_denyFilePath = denyFilePath ?? Path.Combine(FileSystem.AppDataDirectory, "global-deny-commands.json");
		Load();
	}

	readonly List<string> _commands = [];
	readonly ReaderWriterLockSlim _lock = new();
	static readonly JsonSerializerOptions _jsonOptions = new() { WriteIndented = true };
	public event Action? OnDenyListChanged;

	/// <summary>Returns true if the command is on the deny list.</summary>
	public bool IsDenied(string command)
	{
		_lock.EnterReadLock();
		try
		{
			return _commands.Contains(command);
		}
		finally
		{
			_lock.ExitReadLock();
		}
	}

	/// <summary>Returns true if any of the supplied commands are on the deny list.</summary>
	public bool AnyDenied(List<string> commands)
	{
		_lock.EnterReadLock();
		try
		{
			return commands.Any(_commands.Contains);
		}
		finally
		{
			_lock.ExitReadLock();
		}
	}

	public List<string> GetAll()
	{
		_lock.EnterReadLock();
		try
		{
			return [.. _commands.Order()];
		}
		finally
		{
			_lock.ExitReadLock();
		}
	}

	public void Add(string command)
	{
		List<string>? snapshot = null;

		_lock.EnterWriteLock();
		try
		{
			if(!_commands.Contains(command))
			{
				_commands.Add(command);
				snapshot = [.. _commands];
			}
		}
		finally
		{
			_lock.ExitWriteLock();
		}

		if(snapshot is not null)
		{
			Save(snapshot);
			OnDenyListChanged?.Invoke();
		}
	}

	public void Remove(string command)
	{
		List<string>? snapshot = null;

		_lock.EnterWriteLock();
		try
		{
			if(_commands.Remove(command))
			{
				snapshot = [.. _commands];
			}
		}
		finally
		{
			_lock.ExitWriteLock();
		}

		if(snapshot is not null)
		{
			Save(snapshot);
			OnDenyListChanged?.Invoke();
		}
	}

	void Load()
	{
		try
		{
			if(!File.Exists(_denyFilePath))
			{
				return;
			}

			string json = File.ReadAllText(_denyFilePath);
			List<string>? file = JsonSerializer.Deserialize<List<string>>(json);

			if(file is not null)
			{
				_lock.EnterWriteLock();
				try
				{
					_commands.Clear();
					_commands.AddRange(file);
				}
				finally
				{
					_lock.ExitWriteLock();
				}

				_logger.LogInformation("Loaded {Count} global deny commands from {Path}", _commands.Count, _denyFilePath);
			}
		}
		catch(Exception ex)
		{
			_logger.LogError(ex, "Failed to load deny list from {Path}", _denyFilePath);
		}
	}

	void Save(List<string> snapshot)
	{
		try
		{
			string json = JsonSerializer.Serialize(snapshot, _jsonOptions);
			File.WriteAllText(_denyFilePath, json);
			_logger.LogDebug("Saved deny list to {Path}", _denyFilePath);
		}
		catch(Exception ex)
		{
			_logger.LogError(ex, "Failed to save deny list to {Path}", _denyFilePath);
		}
	}

	public void Dispose()
	{
		_lock?.Dispose();
	}
}
