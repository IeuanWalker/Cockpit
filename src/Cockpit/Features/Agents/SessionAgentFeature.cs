using Cockpit.Features.Agents.Models;
using Cockpit.Features.Sessions;
using Cockpit.Features.Sessions.Models;
using Microsoft.Extensions.Logging;

namespace Cockpit.Features.Agents;

public sealed class SessionAgentFeature
{
	readonly ISessionStateProvider _sessionStateProvider;
	readonly ILogger<SessionAgentFeature> _logger;

	public SessionAgentFeature(ISessionStateProvider sessionStateProvider, ILogger<SessionAgentFeature> logger)
	{
		_sessionStateProvider = sessionStateProvider;
		_logger = logger;
	}

	/// <summary>
	/// Scans the repo's .github/agents/ directory and stores the results in the session context.
	/// </summary>
	public async Task<List<AgentProfile>> Load(string? gitRoot)
	{
		if(string.IsNullOrWhiteSpace(gitRoot))
		{
			return [];
		}

		string agentsDir = Path.Combine(gitRoot, ".github", "agents");

		if(!Directory.Exists(agentsDir))
		{
			_logger.LogDebug("Repo agents directory not found for session: {Path}", agentsDir);
			return [];
		}

		List<AgentProfile> loaded = [];
		try
		{
			IEnumerable<string> files = Directory.EnumerateFiles(agentsDir, "*.agent.md", SearchOption.TopDirectoryOnly);

			foreach(string file in files)
			{
				AgentProfile? profile = await AgentFileParser.TryParse(file, AgentSource.Repo);
				if(profile is not null)
				{
					loaded.Add(profile);
					_logger.LogDebug("Loaded repo agent '{Name}' for session, from {Path}", profile.Config.Name, file);
				}
				else
				{
					_logger.LogWarning("Failed to parse repo agent file: {Path}", file);
				}
			}

			_logger.LogInformation("Loaded {Count} repo agents for session in {Path}", loaded.Count, agentsDir);

			return loaded;
		}
		catch(Exception ex)
		{
			_logger.LogError(ex, "Failed to load repo agents for session from {Path}", agentsDir);

			return [];
		}
	}

	/// <summary>
	/// Returns the repo agents for the given session.
	/// </summary>
	public IReadOnlyList<AgentProfile> GetAgents(string sessionId)
	{
		SessionModel? session = _sessionStateProvider.Sessions.FirstOrDefault(s => s.Id == sessionId);
		return session?.Context.RepoAgents ?? [];
	}
}
