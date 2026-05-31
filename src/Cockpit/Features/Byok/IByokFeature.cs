using GitHub.Copilot.SDK;

namespace Cockpit.Features.Byok;

public interface IByokFeature
{
	IReadOnlyList<ByokModelConfig> GetAll();
	Task AddAsync(ByokModelConfig config);
	Task RemoveAsync(string id);
	ProviderConfig? TryGetProviderConfig(string modelId);
	event Action? OnChanged;
}
