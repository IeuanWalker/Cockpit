using Cockpit.Extensions;
using Cockpit.Features.Sessions.Models;
using Microsoft.Extensions.Logging;

namespace Cockpit.Features.Sessions;

public class SessionModePersistence
{
	const string SettingsDirectoryName = "Cockpit";
	const string SessionAgentModeFileName = "session-agentmode.json";

	readonly ILogger<SessionModePersistence> _logger;

	public SessionModePersistence(ILogger<SessionModePersistence> logger)
	{
		_logger = logger;
	}

	public async Task SaveSessionModeAsync(SessionModel session, CancellationToken cancellationToken = default)
	{
		string? filePath = GetFilePath(session);
		if(string.IsNullOrWhiteSpace(filePath))
		{
			return;
		}

		try
		{
			string? directory = Path.GetDirectoryName(filePath);
			if(string.IsNullOrWhiteSpace(directory))
			{
				return;
			}

			Directory.CreateDirectory(directory);
			Dictionary<string, string> settings = new()
			{
				["AgentMode"] = session.Context.SelectedAgentMode.ToString()
			};
			string json = settings.SerializeJson()!;
			await File.WriteAllTextAsync(filePath, json, cancellationToken);
		}
		catch(OperationCanceledException)
		{
			throw;
		}
		catch(Exception ex)
		{
			_logger.LogWarning(ex, "Failed to save agent mode for session {SessionId}", session.Id);
		}
	}

	public async Task<bool> TryRestoreSessionModeAsync(SessionModel session, CancellationToken cancellationToken = default)
	{
		string? filePath = GetFilePath(session);
		if(string.IsNullOrWhiteSpace(filePath) || !File.Exists(filePath))
		{
			return false;
		}

		try
		{
			string json = await File.ReadAllTextAsync(filePath, cancellationToken);
			Dictionary<string, string>? settings = json.DeserializeJson<Dictionary<string, string>>();

			if(settings is null || !settings.TryGetValue("AgentMode", out string? modeStr) || string.IsNullOrWhiteSpace(modeStr))
			{
				return false;
			}

			if(!Enum.TryParse(modeStr, out SessionAgentModeEnum mode))
			{
				return false;
			}

			session.Context.SelectedAgentMode = mode;
			return true;
		}
		catch(OperationCanceledException)
		{
			throw;
		}
		catch(Exception ex)
		{
			_logger.LogWarning(ex, "Failed to restore agent mode for session {SessionId}", session.Id);
			return false;
		}
	}

	static string? GetFilePath(SessionModel session)
	{
		if(string.IsNullOrWhiteSpace(session.Context.WorkspacePath))
		{
			return null;
		}

		return Path.Combine(session.Context.WorkspacePath, SettingsDirectoryName, SessionAgentModeFileName);
	}
}
