using Cockpit.Features.Byok;
using Cockpit.Features.Sdk;
using GitHub.Copilot.SDK;
using Microsoft.Extensions.Logging;

namespace Cockpit.Features.Models;

public sealed partial class ModelFeature : IModelFeature
{
	readonly CopilotClientFeature _clientFeature;
	readonly IByokFeature _byokFeature;
	readonly ILogger<ModelFeature> _logger;

	public ModelFeature(CopilotClientFeature clientFeature, IByokFeature byokFeature, ILogger<ModelFeature> logger)
	{
		_clientFeature = clientFeature;
		_byokFeature = byokFeature;
		_logger = logger;
	}

	// volatile ensures the un-guarded early-exit read sees the latest value
	// written under the semaphore without requiring a full lock acquire.
	volatile IReadOnlyList<ModelInfo>? _models;
	readonly SemaphoreSlim _fetchLock = new(1, 1);

	/// <inheritdoc />
	public async ValueTask<IReadOnlyList<ModelInfo>> GetModels(CancellationToken cancellationToken = default)
	{
		if(_models is not null)
		{
			return BuildMergedModelList(_models);
		}

		await _fetchLock.WaitAsync(cancellationToken);
		try
		{
			// Re-check after acquiring lock — another caller may have populated it already.
			if(_models is not null)
			{
				return BuildMergedModelList(_models);
			}

			CopilotClient client = await _clientFeature.GetClientAsync(cancellationToken);
			IList<ModelInfo> fetched = await client.ListModelsAsync(cancellationToken);
			_models = [.. fetched]; // snapshot to IReadOnlyList to prevent external mutation
		}
		catch(Exception ex) when(ex is not OperationCanceledException)
		{
			_logger.LogError(ex, "Failed to fetch model list from Copilot SDK");
			throw;
		}
		finally
		{
			_fetchLock.Release();
		}

		return BuildMergedModelList(_models!);
	}

	/// <inheritdoc />
	public async ValueTask<ModelInfo> GetDefaultModel(CancellationToken cancellationToken = default)
	{
		IReadOnlyList<ModelInfo> models = _models ?? await GetModels(cancellationToken);
		return SelectDefaultModel(models);
	}

	/// <summary>
	/// Selects the default model from <paramref name="models"/> using the free-tier preference
	/// heuristic: second free model → first free model → first model overall.
	/// Extracted as an <see langword="internal"/> static so unit tests can verify the logic
	/// without requiring a live SDK connection.
	/// </summary>
	internal static ModelInfo SelectDefaultModel(IReadOnlyList<ModelInfo> models)
	{
		if(models.Count == 0)
		{
			throw new InvalidOperationException("No models available from the Copilot API.");
		}

		// Single-pass scan: track first and second free-tier models to avoid allocating
		// an intermediate list just for index access.
		ModelInfo? firstFree = null;
		ModelInfo? secondFree = null;

		foreach(ModelInfo m in models)
		{
			if(m.Billing?.Multiplier != 0)
			{
				continue;
			}

			if(firstFree is null)
			{
				firstFree = m;
			}
			else
			{
				secondFree = m;
				break;
			}
		}

		// Prefer second free-tier → first free-tier → first model overall.
		return secondFree ?? firstFree ?? models[0];
	}

	/// <inheritdoc />
	public ValueTask<ProviderConfig?> GetProviderConfig(string modelId, CancellationToken cancellationToken = default)
	{
		ProviderConfig? config = _byokFeature.TryGetProviderConfig(modelId);
		return ValueTask.FromResult(config);
	}

	List<ModelInfo> BuildMergedModelList(IReadOnlyList<ModelInfo> sdkModels)
	{
		IReadOnlyList<ByokModelConfig> byokConfigs = _byokFeature.GetAll();
		if(byokConfigs.Count == 0)
		{
			return [.. sdkModels];
		}

		HashSet<string> byokModelIds = new(StringComparer.OrdinalIgnoreCase);
		foreach(ByokModelConfig config in byokConfigs)
		{
			byokModelIds.Add(config.ModelId);
		}

		List<ModelInfo> merged = [];
		foreach(ModelInfo model in sdkModels)
		{
			if(!byokModelIds.Contains(model.Id))
			{
				merged.Add(model);
			}
		}

		foreach(ByokModelConfig config in byokConfigs)
		{
			merged.Add(config.ToModelInfo());
		}

		return merged;
	}
}
