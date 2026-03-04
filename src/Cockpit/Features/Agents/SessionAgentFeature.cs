using Cockpit.Features.Agents.Models;
using Cockpit.Features.Sessions;
using Cockpit.Features.Sessions.Models;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace Cockpit.Features.Agents;

public sealed class SessionAgentFeature
{
	readonly ISessionStateProvider _sessionStateProvider;
	readonly ILogger<SessionAgentFeature> _logger;

	public SessionAgentFeature(ISessionStateProvider sessionStateProvider, ILogger<SessionAgentFeature>? logger = null)
	{
		_sessionStateProvider = sessionStateProvider;
		_logger = logger ?? NullLogger<SessionAgentFeature>.Instance;
	}

	/// <summary>
	/// Scans the repo's .github/agents/ directory and stores the results in the session context.
	/// </summary>
	public void LoadForSession(SessionModel session)
	{
		if(string.IsNullOrWhiteSpace(session.Context.GitRoot))
		{
			return;
		}

		string agentsDir = Path.Combine(session.Context.GitRoot, ".github", "agents");

		if(!Directory.Exists(agentsDir))
		{
			_logger.LogDebug("Repo agents directory not found for session {SessionId}: {Path}", session.Id, agentsDir);
			return;
		}

		List<AgentProfile> loaded = [];
		try
		{
			IEnumerable<string> files = Directory.EnumerateFiles(agentsDir, "*.agent.md", SearchOption.TopDirectoryOnly);

			foreach(string file in files)
			{
				AgentProfile? profile = AgentFileParser.TryParse(file, AgentSource.Repo);
				if(profile is not null)
				{
					loaded.Add(profile);
					_logger.LogDebug("Loaded repo agent '{Name}' for session {SessionId} from {Path}", profile.Config.Name, session.Id, file);
				}
				else
				{
					_logger.LogWarning("Failed to parse repo agent file: {Path}", file);
				}
			}

			session.Context.RepoAgents = loaded;
			_logger.LogInformation("Loaded {Count} repo agents for session {SessionId}", loaded.Count, session.Id);
		}
		catch(Exception ex)
		{
			_logger.LogError(ex, "Failed to load repo agents for session {SessionId} from {Path}", session.Id, agentsDir);
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
