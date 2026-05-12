using Cockpit.Features.Sessions.Models;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Web;

namespace Cockpit.Components.Pages.SessionsPanel;

public partial class SessionListItem
{
	[Parameter] public required SessionModel Session { get; set; }
	[Parameter] public bool IsActive { get; set; }
	[Parameter] public EventCallback<SessionModel> OnSelect { get; set; }
	[Parameter] public EventCallback<MouseEventArgs> OnDelete { get; set; }
	[Parameter] public required string TimeAgo { get; set; }

	static string GetSessionStatusClass(SessionModel session) => session.Status switch
	{
		SessionStatusEnum.NeedsPermission => "status-needs-permission",
		SessionStatusEnum.NeedsUserInput => "status-needs-user-input",
		SessionStatusEnum.Running => "status-running",
		_ => "secondary-text"
	};

	async Task HandleSelect() => await OnSelect.InvokeAsync(Session);
	async Task HandleDelete(MouseEventArgs e) => await OnDelete.InvokeAsync(e);
}