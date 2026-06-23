namespace Cockpit.Features.Sdk;

/// <summary>
/// Represents the SDK client connection state. Defined in the app because the SDK no longer
/// exposes a <c>ConnectionState</c> type as of 1.0.0-beta.10.
/// </summary>
public enum ConnectionState
{
	Connected,
	Disconnected
}
