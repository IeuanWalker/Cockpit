using Cockpit.Features.Agents.Models;
using Microsoft.Extensions.Logging;

namespace Cockpit.Features.Agents;

public sealed class GlobalAgentFeature : IDisposable
{
	readonly ILogger<GlobalAgentFeature> _logger;
	readonly string _agentsDirectory;

	readonly List<AgentProfile> _agents = [];
	readonly ReaderWriterLockSlim _lock = new();

	public event Action? OnAgentsChanged;

	public GlobalAgentFeature(ILogger<GlobalAgentFeature> logger, string? agentsDirectory = null)
	{
		_logger = logger;
		_agentsDirectory = agentsDirectory
			?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".copilot", "agents");

		Load();
	}

	public IReadOnlyList<AgentProfile> Agents
	{
		get
		{
			_lock.EnterReadLock();
			try
			{
				return [.. _agents];
			}
			finally
			{
				_lock.ExitReadLock();
			}
		}
	}

	public void Load()
	{
		try
		{
			if(!Directory.Exists(_agentsDirectory))
			{
				_logger.LogDebug("Global agents directory not found: {Path}", _agentsDirectory);
				return;
			}

			IEnumerable<string> files = Directory.EnumerateFiles(_agentsDirectory, "*.agent.md", SearchOption.TopDirectoryOnly);

			List<AgentProfile> loaded = [];
			foreach(string file in files)
			{
				AgentProfile? profile = AgentFileParser.TryParse(file, AgentSource.Global);
				if(profile is not null)
				{
					loaded.Add(profile);
					_logger.LogDebug("Loaded global agent '{Name}' from {Path}", profile.Config.Name, file);
				}
				else
				{
					_logger.LogWarning("Failed to parse global agent file: {Path}", file);
				}
			}

			_lock.EnterWriteLock();
			try
			{
				_agents.Clear();
				_agents.AddRange(loaded);
			}
			finally
			{
				_lock.ExitWriteLock();
			}

			_logger.LogInformation("Loaded {Count} global agents from {Path}", loaded.Count, _agentsDirectory);
			OnAgentsChanged?.Invoke();
		}
		catch(Exception ex)
		{
			_logger.LogError(ex, "Failed to load global agents from {Path}", _agentsDirectory);
		}
	}

	public void Dispose()
	{
		_lock.Dispose();
	}
}
