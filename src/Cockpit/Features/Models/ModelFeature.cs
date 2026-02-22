using System.Text.Json;
using Cockpit.Features.Sessions.Models;
using GitHub.Copilot.SDK;
using Microsoft.Extensions.Logging;

namespace Cockpit.Features.Models;

public sealed partial class ModelFeature : IDisposable
{
	readonly ILogger<ModelFeature> _logger;
	public ModelFeature(ILogger<ModelFeature> logger)
	{
		_logger = logger;
	}

	List<ModelInfo>? _models;
	readonly SemaphoreSlim _fetchLock = new(1, 1);

	/// <summary>
	/// Get all models
	/// </summary>
	public async ValueTask<List<ModelInfo>> GetModels()
	{
		if(_models is not null)
		{
			return _models;
		}

		await _fetchLock.WaitAsync();
		try
		{
			// Re-check after acquiring lock — another caller may have fetched already
			if(_models is not null)
			{
				return _models;
			}

			await using CopilotClient client = new();
			_models = await client.ListModelsAsync();
		}
		finally
		{
			_fetchLock.Release();
		}

		return _models!;
	}

	/// <summary>
	/// Get Default model
	/// </summary>
	/// <remarks>
	/// TODO: Implement functionality to allow the user to select the default model
	/// </remarks>
	public async ValueTask<ModelInfo> GetDefaultModel()
	{
		List<ModelInfo> models = _models ?? await GetModels();

		// Prefer the second free-tier model (index 1) if it exists, then the first free-tier, then the first model overall
		List<ModelInfo> freeModels = [.. models.Where(x => x.Billing?.Multiplier == 0)];
		if(freeModels.Count >= 2)
		{
			return freeModels[1];
		}

		if(freeModels.Count == 1)
		{
			return freeModels[0];
		}

		if(models.Count > 0)
		{
			return models[0];
		}

		throw new InvalidOperationException("No models available from the Copilot API.");
	}

	/// <summary>
	/// Save session model settings
	/// </summary>
	/// <param name="session"></param>
	public async Task SaveSessionModelSettings(SessionModel session)
	{
		string? modelSettingsFilePath = GetModelsFilePath(session);
		if(string.IsNullOrWhiteSpace(modelSettingsFilePath))
		{
			return;
		}

		try
		{
			string? modelSettingsDirectory = Path.GetDirectoryName(modelSettingsFilePath);
			if(string.IsNullOrWhiteSpace(modelSettingsDirectory))
			{
				return;
			}

			Directory.CreateDirectory(modelSettingsDirectory);

			Dictionary<string, string> modelSettings = new()
			{
				["ModelId"] = session.Model.Id,
				["ReasoningEffort"] = session.ReasoningEffort ?? string.Empty
			};

			string json = JsonSerializer.Serialize(modelSettings, new JsonSerializerOptions
			{
				WriteIndented = true
			});

			await File.WriteAllTextAsync(modelSettingsFilePath, json);
		}
		catch(Exception ex)
		{
			_logger.LogWarning(ex, "Failed to persist session commands for session {SessionId}", session.Id);
		}
	}

	public async Task<bool> TryRestoreModelSettings(SessionModel session)
	{
		string? modelSettingsFilePath = GetModelsFilePath(session);
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

				session.Model = model;

				if(session.Model.SupportedReasoningEfforts?.Count > 0)
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
						session.ReasoningEffort = reasoningEffort;
						return true;
					}
				}
			}

			return true;
		}
		catch(Exception ex)
		{
			_logger.LogWarning(ex, "Failed to restore session commands for session {SessionId} from {Path}", session.Id, modelSettingsFilePath);
			return false;
		}
	}

	static string? GetModelsFilePath(SessionModel session)
	{
		if(string.IsNullOrWhiteSpace(session.Context.WorkspacePath))
		{
			return null;
		}

		return Path.Combine(session.Context.WorkspacePath, "Cockpit", "session-model.json");
	}
	public void Dispose()
	{
		_fetchLock.Dispose();
		GC.SuppressFinalize(this);
	}
}
