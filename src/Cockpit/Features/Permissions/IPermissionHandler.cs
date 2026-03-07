using GitHub.Copilot.SDK;

namespace Cockpit.Features.Permissions;

public interface IPermissionHandler
{
	Task<PermissionRequestResult> HandlePermissionRequest(PermissionRequest request, PermissionInvocation invocation);

	/// <summary>
	/// Cancels all pending permission requests for a session (e.g., when the session is aborted or deleted).
	/// </summary>
	void CancelPendingRequestsForSession(string sessionId);
}
