using GitHub.Copilot.SDK;

namespace Cockpit.Features.Permissions;

public interface IPermissionHandler
{
	Task<PermissionRequestResult> HandlePermissionRequest(PermissionRequest request, PermissionInvocation invocation);
}
