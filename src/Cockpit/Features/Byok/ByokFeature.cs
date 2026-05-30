using Cockpit.Extensions;
using GitHub.Copilot.SDK;
using Microsoft.Extensions.Logging;

namespace Cockpit.Features.Byok;

sealed class ByokFeature : IByokFeature
{
	readonly ILogger<ByokFeature> _logger;
	readonly string _filePath;
	readonly SemaphoreSlim _lock = new(1, 1);
	volatile List<ByokModelConfig> _configs = [];

	public event Action? OnChanged;

	public ByokFeature(ILogger<ByokFeature> logger)
	{
		_logger = logger;
		_filePath = Path.Combine(FileSystem.AppDataDirectory, "byok-models.json");
		_ = LoadAsync();
	}

	public IReadOnlyList<ByokModelConfig> GetAll() => _configs;

	public async Task AddAsync(ByokModelConfig config)
	{
		await _lock.WaitAsync();
		try
		{
			_configs = [.. _configs.Where(c => !string.Equals(c.Id, config.Id, StringComparison.OrdinalIgnoreCase) && !string.Equals(c.ModelId, config.ModelId, StringComparison.OrdinalIgnoreCase)), config];
			await SaveAsync();
		}
		finally
		{
			_lock.Release();
		}
		OnChanged?.Invoke();
	}

	public async Task RemoveAsync(string id)
	{
		await _lock.WaitAsync();
		try
		{
			_configs = [.. _configs.Where(c => c.Id != id)];
			await SaveAsync();
		}
		finally
		{
			_lock.Release();
		}
		OnChanged?.Invoke();
	}

	public ProviderConfig? TryGetProviderConfig(string modelId)
	{
		ByokModelConfig? config = _configs.FirstOrDefault(c => string.Equals(c.ModelId, modelId, StringComparison.OrdinalIgnoreCase));
		return config?.ToProviderConfig();
	}

	async Task LoadAsync()
	{
		if(!File.Exists(_filePath))
		{
			return;
		}

		try
		{
			string json = await File.ReadAllTextAsync(_filePath);
			List<ByokModelConfig>? loaded = json.DeserializeJson<List<ByokModelConfig>>();
			if(loaded is null)
			{
				return;
			}

			await _lock.WaitAsync();
			try
			{
				// Merge to avoid clobbering configs added before the async load completed.
				Dictionary<string, ByokModelConfig> merged = new(StringComparer.OrdinalIgnoreCase);
				foreach(ByokModelConfig c in loaded)
				{
					merged[c.Id] = c;
				}
				foreach(ByokModelConfig c in _configs)
				{
					merged[c.Id] = c;
				}

				_configs = [.. merged.Values];
			}
			finally
			{
				_lock.Release();
			}

			OnChanged?.Invoke();
		}
		catch(Exception ex)
		{
			_logger.LogWarning(ex, "Failed to load BYOK configs from {Path}", _filePath);
		}
	}

	async Task SaveAsync()
	{
		try
		{
			string? dir = Path.GetDirectoryName(_filePath);
			if (!string.IsNullOrWhiteSpace(dir))
			{
				Directory.CreateDirectory(dir);
			}

			string? json = _configs.SerializeJson();
			if (json is null)
			{
				return;
			}

			string tempPath = _filePath + ".tmp";
			await File.WriteAllTextAsync(tempPath, json);
			File.Move(tempPath, _filePath, overwrite: true);
		}
		catch (Exception ex)
		{
			_logger.LogWarning(ex, "Failed to save BYOK configs to {Path}", _filePath);
		}
	}
}
