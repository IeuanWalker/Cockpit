using GitHub.Copilot.SDK;
using Microsoft.Extensions.Logging;

namespace Cockpit.Features.Models;

public sealed partial class ModelFeature : IModelFeature, IDisposable
{
	readonly ILogger<ModelFeature> _logger;
	public ModelFeature(ILogger<ModelFeature> logger)
	{
		_logger = logger;
	}

	// volatile ensures the un-guarded early-exit read on line ~23 sees the latest value
	// written under the semaphore without requiring a full lock acquire.
	volatile IReadOnlyList<ModelInfo>? _models;
	readonly SemaphoreSlim _fetchLock = new(1, 1);
	// volatile so cross-thread disposal checks see the latest write immediately.
	volatile bool _disposed;

	/// <inheritdoc />
	public async ValueTask<IReadOnlyList<ModelInfo>> GetModels(CancellationToken cancellationToken = default)
	{
		ObjectDisposedException.ThrowIf(_disposed, this);

		if(_models is not null)
		{
			return _models;
		}

		await _fetchLock.WaitAsync(cancellationToken);
		try
		{
			// Re-check after acquiring lock — another caller may have populated it already.
			if(_models is not null)
			{
				return _models;
			}

			// A short-lived client is used here so model listing does not depend on the
			// singleton CopilotClientFeature being initialised first (e.g. during splash).
			await using CopilotClient client = new();
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

		return _models!;
	}

	/// <inheritdoc />
	public async ValueTask<ModelInfo> GetDefaultModel(CancellationToken cancellationToken = default)
	{
		ObjectDisposedException.ThrowIf(_disposed, this);
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

	public void Dispose()
	{
		if(_disposed)
		{
			return;
		}

		_disposed = true;
		_fetchLock.Dispose();
		_persistLock.Dispose();
		GC.SuppressFinalize(this);
	}
}
