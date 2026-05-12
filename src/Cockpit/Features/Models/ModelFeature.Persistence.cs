using Cockpit.Extensions;
using Cockpit.Features.Sessions.Models;
using GitHub.Copilot.SDK;
using Microsoft.Extensions.Logging;

namespace Cockpit.Features.Models;

public partial class ModelFeature
{
	// Serialises concurrent saves to the same workspace file to avoid interleaved writes.
	readonly SemaphoreSlim _persistLock = new(1, 1);

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

			// Write to a temp file then atomically replace to avoid corrupted reads.
			string tempPath = modelFilePath + ".tmp";
			await _persistLock.WaitAsync();
			try
			{
				await File.WriteAllTextAsync(tempPath, json);
				File.Move(tempPath, modelFilePath, overwrite: true);
			}
			finally
			{
				_persistLock.Release();
			}
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
		if(string.IsNullOrWhiteSpace(modelSettingsFilePath) || !File.Exists(modelSettingsFilePath))
		{
			return false;
		}

		try
		{
			string json = await File.ReadAllTextAsync(modelSettingsFilePath);
			Dictionary<string, string>? modelSettings = json.DeserializeJson<Dictionary<string, string>>();

			if(modelSettings is null)
			{
				return false;
			}

			// A missing or blank ModelId means the file has no actionable data.
			if(!modelSettings.TryGetValue("ModelId", out string? modelId) || string.IsNullOrWhiteSpace(modelId))
			{
				return false;
			}

			IReadOnlyList<ModelInfo> models = await GetModels();
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

			// Use the freshly-fetched `model` (not `session.Model`) for all effort validation
			// so we always check against the API's current supported efforts, not a potentially
			// stale ModelInfo instance that was assigned before the latest fetch.
			if(model.SupportedReasoningEfforts is null || model.SupportedReasoningEfforts.Count == 0)
			{
				if(!string.IsNullOrEmpty(session.ReasoningEffort))
				{
					session.ReasoningEffort = null;
					session.ModelChanged = true;
				}

				return true;
			}

			if(!modelSettings.TryGetValue("ReasoningEffort", out string? reasoningEffort) || string.IsNullOrWhiteSpace(reasoningEffort))
			{
				// No saved effort preference. Clear stale effort if invalid for restored model.
				if(!string.IsNullOrEmpty(session.ReasoningEffort)
				&& !model.SupportedReasoningEfforts.Contains(session.ReasoningEffort))
				{
					session.ReasoningEffort = null;
					session.ModelChanged = true;
				}

				return true;
			}

			if(model.SupportedReasoningEfforts.Contains(reasoningEffort) && session.ReasoningEffort != reasoningEffort)
			{
				session.ReasoningEffort = reasoningEffort;
				session.ModelChanged = true;
			}
			else if(!model.SupportedReasoningEfforts.Contains(reasoningEffort))
			{
				// Saved effort is not valid for the current model. Clear any stale effort
				// on the session that is also unsupported, but leave a valid current effort untouched.
				if(!string.IsNullOrEmpty(session.ReasoningEffort)
				&& !model.SupportedReasoningEfforts.Contains(session.ReasoningEffort))
				{
					session.ReasoningEffort = null;
					session.ModelChanged = true;
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