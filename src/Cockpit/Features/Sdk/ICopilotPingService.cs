using GitHub.Copilot.SDK;

namespace Cockpit.Features.Sdk;

/// <summary>
/// Abstraction over the Copilot ping operation, enabling testing without a live SDK client.
/// </summary>
public interface ICopilotPingService
{
	Task<PingResponse?> PingAsync(CancellationToken cancellationToken = default);
}
