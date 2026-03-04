using System.Text.Json;
using Cockpit.Features.Agents.Models;
using Cockpit.Features.Sessions.Models;

namespace Cockpit.Features.Agents;

public static class AgentPersistence
{
	public static string? GetAgentFilePath(SessionModel session)
	{
		if(string.IsNullOrWhiteSpace(session.Context.WorkspacePath))
		{
			return null;
		}

		return Path.Combine(session.Context.WorkspacePath, "Cockpit", "session-agent.json");
	}

	public static async Task SaveSessionAgentAsync(SessionModel session)
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
			string json = JsonSerializer.Serialize(agentSettings, new JsonSerializerOptions { WriteIndented = true });
			await File.WriteAllTextAsync(agentFilePath, json);
		}
		catch { /* best-effort */ }
	}

	public static async Task<AgentProfile?> TryRestoreSessionAgentAsync(SessionModel session, IEnumerable<AgentProfile> allAgents)
	{
		string? agentFilePath = GetAgentFilePath(session);
		if(string.IsNullOrWhiteSpace(agentFilePath) || !File.Exists(agentFilePath))
		{
			return null;
		}

		try
		{
			string json = await File.ReadAllTextAsync(agentFilePath);
			Dictionary<string, string>? agentSettings = JsonSerializer.Deserialize<Dictionary<string, string>>(json);
			if(agentSettings is null || !agentSettings.TryGetValue("AgentName", out string? agentName) || string.IsNullOrWhiteSpace(agentName))
			{
				return null;
			}

			AgentProfile? match = agentSettings.TryGetValue("AgentSource", out string? agentSourceStr) && Enum.TryParse<AgentSource>(agentSourceStr, out AgentSource agentSource)
				? allAgents.FirstOrDefault(a => string.Equals(a.Config.Name, agentName, StringComparison.OrdinalIgnoreCase) && a.Source == agentSource)
				: allAgents.FirstOrDefault(a => string.Equals(a.Config.Name, agentName, StringComparison.OrdinalIgnoreCase));

			return match;
		}
		catch
		{
			return null;
		}
	}
}
