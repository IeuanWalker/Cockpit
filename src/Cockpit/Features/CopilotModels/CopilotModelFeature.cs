using GitHub.Copilot.SDK;

namespace Cockpit.Features.CopilotModels;

public sealed partial class CopilotModelFeature : IDisposable
{
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

	public void Dispose()
	{
		_fetchLock.Dispose();
		GC.SuppressFinalize(this);
	}
}
