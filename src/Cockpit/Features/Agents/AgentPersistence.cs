using Cockpit.Extensions;
using Cockpit.Features.Agents.Models;
using Cockpit.Features.Sessions.Models;

namespace Cockpit.Features.Agents;

public class AgentPersistence
{
	readonly GlobalAgentFeature _globalAgentFeature;

	public AgentPersistence(GlobalAgentFeature globalAgentFeature)
	{
		_globalAgentFeature = globalAgentFeature;
	}
	public string? GetAgentFilePath(SessionModel session)
	{
		if(string.IsNullOrWhiteSpace(session.Context.WorkspacePath))
		{
			return null;
		}

		return Path.Combine(session.Context.WorkspacePath, "Cockpit", "session-agent.json");
	}

	public async Task SaveSessionAgent(SessionModel session)
	{
		string? agentFilePath = GetAgentFilePath(session);
		if(string.IsNullOrWhiteSpace(agentFilePath))
		{
			return;
		}

		try
		{
			string? agentDirectory = Path.GetDirectoryName(agentFilePath);
			if(string.IsNullOrWhiteSpace(agentDirectory))
			{
				return;
			}

			Directory.CreateDirectory(agentDirectory);
			AgentProfile? agent = session.Context.SelectedAgent;
			Dictionary<string, string> agentSettings = new()
			{
				["AgentName"] = agent?.Config.Name ?? string.Empty,
				["AgentSource"] = agent?.Source.ToString() ?? string.Empty
			};
			string json = agentSettings.SerializeJson()!;
			await File.WriteAllTextAsync(agentFilePath, json);
		}
		catch { /* best-effort */ }
	}

	public async Task<bool> TryRestoreSessionAgentAsync(SessionModel session)
	{
		string? agentFilePath = GetAgentFilePath(session);
		if(string.IsNullOrWhiteSpace(agentFilePath) || !File.Exists(agentFilePath))
		{
			return false;
		}

		IEnumerable<AgentProfile> allAgents = [.. _globalAgentFeature.Agents, .. session.Context.RepoAgents];

		try
		{
			string json = await File.ReadAllTextAsync(agentFilePath);
			Dictionary<string, string>? agentSettings = json.DeserializeJson<Dictionary<string, string>>();
			if(agentSettings is null || !agentSettings.TryGetValue("AgentName", out string? agentName) || string.IsNullOrWhiteSpace(agentName))
			{
				return false;
			}

			AgentProfile? match = agentSettings.TryGetValue("AgentSource", out string? agentSourceStr) && Enum.TryParse(agentSourceStr, out AgentSource agentSource)
				? allAgents.FirstOrDefault(a => string.Equals(a.Config.Name, agentName, StringComparison.OrdinalIgnoreCase) && a.Source == agentSource)
				: allAgents.FirstOrDefault(a => string.Equals(a.Config.Name, agentName, StringComparison.OrdinalIgnoreCase));

			session.Context.SelectedAgent = match;

			return true;
		}
		catch
		{
			return false;
		}
	}
}
