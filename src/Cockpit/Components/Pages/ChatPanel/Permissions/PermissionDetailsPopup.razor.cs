using Cockpit.Components.Controls;
using Cockpit.Extensions;
using Cockpit.Features.Permissions.Models;
using Microsoft.AspNetCore.Components;

namespace Cockpit.Components.Pages.ChatPanel.Permissions;

public partial class PermissionDetailsPopup
{
	[Parameter] public PermissionRequestModel? Request { get; set; }

	PopupBase _popup = default!;

	public void Open() => _popup.Open();

	static string FormatAsJson(string input)
	{
		try
		{
			return input.SerializeJson() ?? input;
		}
		catch
		{
			return input;
		}
	}
}