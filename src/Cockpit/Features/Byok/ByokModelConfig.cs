using GitHub.Copilot.SDK;

namespace Cockpit.Features.Byok;

public sealed class ByokModelConfig
{
	public required string Id { get; init; }
	public required string Name { get; init; }
	public required string ModelId { get; init; }
	public required string ProviderType { get; init; }
	public required string BaseUrl { get; init; }
	public string? ApiKey { get; init; }
	public string? BearerToken { get; init; }
	public string WireApi { get; init; } = "completions";
	public bool SupportsVision { get; init; }
	public bool SupportsReasoning { get; init; }
	public int? MaxContextWindowTokens { get; init; }

	public ProviderConfig ToProviderConfig() => new()
	{
		Type = ProviderType,
		BaseUrl = BaseUrl,
		ApiKey = ApiKey,
		BearerToken = BearerToken,
		WireApi = WireApi
	};

	/// <summary>
	/// Returns a copy of this config with the supplied secret values applied.
	/// Used by <see cref="ByokFeature"/> to reconstruct configs after loading secrets
	/// from platform secure storage.
	/// </summary>
	public ByokModelConfig WithSecrets(string? apiKey, string? bearerToken) => new()
	{
		Id = Id,
		Name = Name,
		ModelId = ModelId,
		ProviderType = ProviderType,
		BaseUrl = BaseUrl,
		ApiKey = apiKey,
		BearerToken = bearerToken,
		WireApi = WireApi,
		SupportsVision = SupportsVision,
		SupportsReasoning = SupportsReasoning,
		MaxContextWindowTokens = MaxContextWindowTokens
	};

	public ModelInfo ToModelInfo()
	{
		ModelInfo info = new()
		{
			Id = ModelId,
			Name = Name
		};

		if (MaxContextWindowTokens.HasValue || SupportsVision || SupportsReasoning)
		{
			ModelCapabilities capabilities = new()
			{
				Supports = new ModelSupports
				{
					Vision = SupportsVision,
					ReasoningEffort = SupportsReasoning
				}
			};

			if (MaxContextWindowTokens.HasValue)
			{
				capabilities.Limits = new ModelLimits { MaxContextWindowTokens = MaxContextWindowTokens.Value };
			}

			info.Capabilities = capabilities;
		}

		return info;
	}
}
