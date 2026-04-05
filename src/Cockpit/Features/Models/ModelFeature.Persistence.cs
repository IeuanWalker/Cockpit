using System.Text.Json;
using Cockpit.Extensions;
using Cockpit.Features.Sessions.Models;
using GitHub.Copilot.SDK;
using Microsoft.Extensions.Logging;

namespace Cockpit.Features.Models;

public partial class ModelFeature
{
	public async Task SaveSessionModel(SessionModel session)
	{
		string? modelFilePath = GetModelFilePath(session);
		if(string.IsNullOrWhiteSpace(modelFilePath))
		{
			return;
		}

		try
		{
			string? modelDirectory = Path.GetDirectoryName(modelFilePath);
			if(string.IsNullOrWhiteSpace(modelDirectory))
			{
				return;
			}

			Directory.CreateDirectory(modelDirectory);
			Dictionary<string, string> modelSettings = new()
			{
				["ModelId"] = session.Model.Id,
				["ReasoningEffort"] = session.ReasoningEffort ?? string.Empty,
			};
			string json = modelSettings.SerializeJson()!;
			await File.WriteAllTextAsync(modelFilePath, json);
		}
		catch(Exception ex)
		{
			_logger.LogWarning(ex, "Failed to save model settings for session {SessionId} to {Path}", session.Id, modelFilePath);
			/* best-effort */
		}
	}

	public async Task<bool> TryRestoreModelSettings(SessionModel session)
	{
		string? modelSettingsFilePath = GetModelFilePath(session);
		if(string.IsNullOrWhiteSpace(modelSettingsFilePath))
		{
			return false;
		}

		if(!File.Exists(modelSettingsFilePath))
		{
			return false;
		}

		try
		{
			string json = await File.ReadAllTextAsync(modelSettingsFilePath);
			Dictionary<string, string>? modelSettings = JsonSerializer.Deserialize<Dictionary<string, string>>(json);

			if(modelSettings is null)
			{
				return false;
			}

			List<ModelInfo> models = await GetModels();

			if(modelSettings.TryGetValue("ModelId", out string? modelId) && !string.IsNullOrWhiteSpace(modelId))
			{
				ModelInfo? model = models.FirstOrDefault(m => m.Id == modelId);

				if(model is null)
				{
					return false;
				}

				if(session.Model.Id != model.Id)
				{
					session.Model = model;
					session.ModelChanged = true;
				}

				if(session.Model.SupportedReasoningEfforts is null || session.Model.SupportedReasoningEfforts.Count == 0)
				{
					return true;
				}

				if(modelSettings.TryGetValue("ReasoningEffort", out string? reasoningEffort))
				{
					if(string.IsNullOrWhiteSpace(reasoningEffort))
					{
						return true;
					}

					if(session.Model.SupportedReasoningEfforts?.Contains(reasoningEffort) ?? false)
					{
						if(session.ReasoningEffort != reasoningEffort)
						{
							session.ReasoningEffort = reasoningEffort;
							session.ModelChanged = true;
						}

						return true;
					}
				}
			}

			return true;
		}
		catch(Exception ex)
		{
			_logger.LogWarning(ex, "Failed to restore model settings for session {SessionId} from {Path}", session.Id, modelSettingsFilePath);
			return false;
		}
	}

	static string? GetModelFilePath(SessionModel session)
	{
		if(string.IsNullOrWhiteSpace(session.Context.WorkspacePath))
		{
			return null;
		}

		return Path.Combine(session.Context.WorkspacePath, "Cockpit", "session-model.json");
	}
}
