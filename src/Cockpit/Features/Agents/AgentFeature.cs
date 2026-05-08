using Cockpit.Features.Agents.Models;
using GitHub.Copilot.SDK;
using GitHub.Copilot.SDK.Rpc;
using Microsoft.Extensions.Logging;

namespace Cockpit.Features.Agents;

public sealed class AgentFeature
{
	readonly ILogger<AgentFeature> _logger;

	public AgentFeature(ILogger<AgentFeature> logger)
	{
		_logger = logger;
	}

	/// <summary>
	/// Calls the SDK to list all discovered agents and maps them to AgentProfile instances.
	/// Agents with no file path or a file that no longer exists on disk are excluded.
	/// </summary>
#pragma warning disable GHCP001
	public async Task<List<AgentProfile>> LoadSessionAgentsAsync(CopilotSession sdkSession, string? gitRoot)
	{
		try
		{
			AgentList agentList = await sdkSession.Rpc.Agent.ListAsync();
			List<AgentProfile> profiles = [.. agentList.Agents
				.Select(info => MapToProfile(info, gitRoot))
				.OfType<AgentProfile>()];
			_logger.LogInformation("Discovered {Count} agents for session", profiles.Count);
			return profiles;
		}
		catch(Exception ex)
		{
			_logger.LogError(ex, "Failed to load agents from SDK");
			return [];
		}
	}
#pragma warning restore GHCP001

	static AgentProfile? MapToProfile(AgentInfo info, string? gitRoot)
	{
		if(string.IsNullOrEmpty(info.Path) || !File.Exists(info.Path))
		{
			return null;
		}

		return new AgentProfile
		{
			Name = info.Name,
			DisplayName = string.IsNullOrWhiteSpace(info.DisplayName) ? null : info.DisplayName,
			Description = string.IsNullOrWhiteSpace(info.Description) ? null : info.Description,
			FilePath = info.Path,
			Source = DetermineSource(info.Path, gitRoot)
		};
	}

	/// <summary>
	/// Determines whether an agent (with a known-valid file path) belongs to the current
	/// repo (<see cref="AgentSource.Repo"/>) or is a user-level global agent
	/// (<see cref="AgentSource.Global"/>).
	/// </summary>
	public static AgentSource DetermineSource(string? path, string? gitRoot)
	{
		if(!string.IsNullOrEmpty(path) && !string.IsNullOrEmpty(gitRoot))
		{
			try
			{
				string normalizedPath = Path.GetFullPath(path);
				string normalizedGitRoot = Path.GetFullPath(gitRoot);
				string relativeToRepo = Path.GetRelativePath(normalizedGitRoot, normalizedPath);
				if(!relativeToRepo.StartsWith("..", StringComparison.Ordinal) &&
					!Path.IsPathRooted(relativeToRepo))
				{
					return AgentSource.Repo;
				}
			}
			catch
			{
				// Fall through to Global for any path parsing issues
			}
		}

		return AgentSource.Global;
	}
}
