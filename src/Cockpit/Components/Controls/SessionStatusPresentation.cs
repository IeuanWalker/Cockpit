using Cockpit.Features.Sessions.Models;

namespace Cockpit.Components.Controls;

static class SessionStatusPresentation
{
	internal static string GetHeaderText(SessionStatusEnum status) => status switch
	{
		SessionStatusEnum.NeedsPermission => "Permission required",
		SessionStatusEnum.NeedsUserInput or SessionStatusEnum.NeedsElicitation => "Input requested",
		_ => status.ToString()
	};

	internal static string GetHeaderClass(SessionStatusEnum status) => status.ToString().ToLowerInvariant();

	internal static string GetListClass(SessionStatusEnum status) => status switch
	{
		SessionStatusEnum.NeedsPermission => "status-needs-permission",
		SessionStatusEnum.NeedsUserInput or SessionStatusEnum.NeedsElicitation => "status-needs-user-input",
		SessionStatusEnum.Running => "status-running",
		_ => "secondary-text"
	};
}
