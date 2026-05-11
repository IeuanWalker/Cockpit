using Cockpit.Features.Permissions.Models;

namespace Cockpit.Features.Permissions;

/// <summary>
/// Minimal abstraction over <see cref="PermissionFeature"/> that exposes only the event
/// used by subscribers such as <c>SoundFeature</c>. Enables testability without taking
/// a hard dependency on the full concrete service.
/// </summary>
public interface IPermissionEventSource
{
	event Action<string, PermissionRequestModel>? OnPermissionRequested;
}
