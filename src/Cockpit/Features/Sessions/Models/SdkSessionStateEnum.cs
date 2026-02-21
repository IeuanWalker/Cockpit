namespace Cockpit.Features.Sessions.Models;

/// <summary>
/// Tracks the SDK connection lifecycle of a session.
/// </summary>
public enum SdkSessionStateEnum
{
	/// <summary>Session exists in the list but has no SDK connection yet.</summary>
	NotLoaded,

	/// <summary>History has been replayed and an SDK session registered with <c>DisableResume=true</c>.</summary>
	Loading,

	/// <summary>Loaded and ready; SDK session connected with <c>DisableResume=true</c>.</summary>
	Loaded,

	/// <summary>Fully resumed; SDK session connected normally and ready to send messages.</summary>
	Resumed,
}
