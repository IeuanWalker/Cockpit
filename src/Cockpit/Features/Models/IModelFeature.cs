using Cockpit.Features.Sessions.Models;
using GitHub.Copilot;

namespace Cockpit.Features.Models;

public interface IModelFeature
{
	ValueTask<IReadOnlyList<ModelInfo>> GetModels(CancellationToken cancellationToken = default);
	ValueTask<ModelInfo> GetDefaultModel(CancellationToken cancellationToken = default);
	Task SaveSessionModel(SessionModel session);
	Task<bool> TryRestoreModelSettings(SessionModel session);
	ValueTask<ProviderConfig?> GetProviderConfig(string modelId, CancellationToken cancellationToken = default);
}
