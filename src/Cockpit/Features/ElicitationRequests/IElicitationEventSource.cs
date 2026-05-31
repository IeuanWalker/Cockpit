namespace Cockpit.Features.ElicitationRequests;

/// <summary>
/// Minimal abstraction over <see cref="ElicitationFeature"/> that exposes only the event
/// used by subscribers. Enables testability without taking a hard dependency on the full concrete service.
/// </summary>
public interface IElicitationEventSource
{
	event Action<string, ElicitationRequestModel>? OnElicitationRequested;
}
