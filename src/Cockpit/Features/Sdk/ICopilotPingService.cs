using GitHub.Copilot;

namespace Cockpit.Features.Sdk;

/// <summary>
/// Abstraction over the Copilot ping operation, enabling testing without a live SDK client.
/// </summary>
public interface ICopilotPingService
{
	Task<PingResponse?> PingAsync(CancellationToken cancellationToken = default);

	/// <summary>
	/// Raised when the underlying client connection state changes (e.g. disconnect or reconnect).
	/// </summary>
	event Action<ConnectionState>? OnConnectionStateChanged;
}
