using Cockpit.Extensions;
using GitHub.Copilot.SDK;
using Microsoft.Extensions.Logging;

namespace Cockpit.Features.Byok;

sealed class ByokFeature : IByokFeature
{
	readonly ILogger<ByokFeature> _logger;
	readonly ISecureStorageProvider _secureStorage;
	readonly string _filePath;
	readonly SemaphoreSlim _lock = new(1, 1);
	volatile List<ByokModelConfig> _configs = [];

	public event Action? OnChanged;

	public ByokFeature(ILogger<ByokFeature> logger, ISecureStorageProvider secureStorage)
	{
		_logger = logger;
		_secureStorage = secureStorage;
		_filePath = Path.Combine(FileSystem.AppDataDirectory, "byok-models.json");
		_ = LoadAsync();
	}

	public IReadOnlyList<ByokModelConfig> GetAll() => _configs;

	public async Task AddAsync(ByokModelConfig config)
	{
		await _lock.WaitAsync();
		try
		{
			// Clean up secrets for any config being evicted (matched by Id or ModelId).
			foreach(ByokModelConfig evicted in _configs.Where(c =>
			string.Equals(c.Id, config.Id, StringComparison.OrdinalIgnoreCase) ||
			string.Equals(c.ModelId, config.ModelId, StringComparison.OrdinalIgnoreCase)))
			{
				_secureStorage.Remove(ApiKeyStorageKey(evicted.Id));
				_secureStorage.Remove(BearerTokenStorageKey(evicted.Id));
			}

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
			_configs = [.. _configs.Where(c => !string.Equals(c.Id, id, StringComparison.OrdinalIgnoreCase))];
			_secureStorage.Remove(ApiKeyStorageKey(id));
			_secureStorage.Remove(BearerTokenStorageKey(id));
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

			List<ByokModelConfigMeta>? loaded = json.DeserializeJson<List<ByokModelConfigMeta>>();
			if(loaded is null)
			{
				return;
			}

			// Resolve secrets from secure storage before acquiring the write lock so
			// async I/O is not performed while the semaphore is held.
			List<ByokModelConfig> resolved = new(loaded.Count);

			foreach(ByokModelConfigMeta c in loaded)
			{
				string? apiKey = await _secureStorage.GetAsync(ApiKeyStorageKey(c.Id));
				string? bearerToken = await _secureStorage.GetAsync(BearerTokenStorageKey(c.Id));
				resolved.Add(FromMeta(c, apiKey, bearerToken));
			}

			await _lock.WaitAsync();
			try
			{
				// Merge to avoid clobbering configs added before the async load completed.
				// In-memory configs (added via AddAsync during load) take precedence.
				Dictionary<string, ByokModelConfig> merged = new(StringComparer.OrdinalIgnoreCase);
				foreach(ByokModelConfig c in resolved)
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
			if(!string.IsNullOrWhiteSpace(dir))
			{
				Directory.CreateDirectory(dir);
			}

			// Sync secrets to secure storage, then write metadata-only JSON (no secrets on disk).
			foreach(ByokModelConfig config in _configs)
			{
				string apiKeyKey = ApiKeyStorageKey(config.Id);
				string bearerKey = BearerTokenStorageKey(config.Id);

				if(!string.IsNullOrEmpty(config.ApiKey))
				{
					await _secureStorage.SetAsync(apiKeyKey, config.ApiKey);
				}
				else
				{
					_secureStorage.Remove(apiKeyKey);
				}

				if(!string.IsNullOrEmpty(config.BearerToken))
				{
					await _secureStorage.SetAsync(bearerKey, config.BearerToken);
				}
				else
				{
					_secureStorage.Remove(bearerKey);
				}
			}

			string? json = _configs.Select(ToMeta).ToList().SerializeJson();
			if(json is null)
			{
				return;
			}

			string tempPath = _filePath + ".tmp";
			await File.WriteAllTextAsync(tempPath, json);
			File.Move(tempPath, _filePath, overwrite: true);
		}
		catch(Exception ex)
		{
			_logger.LogWarning(ex, "Failed to save BYOK configs to {Path}", _filePath);
		}
	}

	static string ApiKeyStorageKey(string id) => $"byok:{id}:apikey";
	static string BearerTokenStorageKey(string id) => $"byok:{id}:bearertoken";

	static ByokModelConfigMeta ToMeta(ByokModelConfig c) => new()
	{
		Id = c.Id,
		Name = c.Name,
		ModelId = c.ModelId,
		ProviderType = c.ProviderType,
		BaseUrl = c.BaseUrl,
		WireApi = c.WireApi,
		SupportsVision = c.SupportsVision,
		SupportsReasoning = c.SupportsReasoning,
		MaxContextWindowTokens = c.MaxContextWindowTokens
	};

	static ByokModelConfig FromMeta(ByokModelConfigMeta m, string? apiKey, string? bearerToken) => new()
	{
		Id = m.Id,
		Name = m.Name,
		ModelId = m.ModelId,
		ProviderType = m.ProviderType,
		BaseUrl = m.BaseUrl,
		WireApi = m.WireApi,
		SupportsVision = m.SupportsVision,
		SupportsReasoning = m.SupportsReasoning,
		MaxContextWindowTokens = m.MaxContextWindowTokens,
		ApiKey = apiKey,
		BearerToken = bearerToken
	};

	/// <summary>
	/// JSON-serialisation DTO that intentionally omits secret fields (<c>ApiKey</c>, <c>BearerToken</c>).
	/// Secrets are stored separately via <see cref="ISecureStorageProvider"/>.
	/// </summary>
	sealed class ByokModelConfigMeta
	{
		public required string Id { get; init; }
		public required string Name { get; init; }
		public required string ModelId { get; init; }
		public required string ProviderType { get; init; }
		public required string BaseUrl { get; init; }
		public string WireApi { get; init; } = "completions";
		public bool SupportsVision { get; init; }
		public bool SupportsReasoning { get; init; }
		public int? MaxContextWindowTokens { get; init; }
	}
}
