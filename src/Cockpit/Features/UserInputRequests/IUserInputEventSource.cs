namespace Cockpit.Features.UserInputRequests;

/// <summary>
/// Minimal abstraction over <see cref="UserInputFeature"/> that exposes only the event
/// used by subscribers such as <c>SoundFeature</c>. Enables testability without taking
/// a hard dependency on the full concrete service.
/// </summary>
public interface IUserInputEventSource
{
	event Action<string, UserInputRequestModel>? OnUserInputRequested;
}
