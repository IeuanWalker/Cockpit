using Cockpit.Components.Controls;
using Cockpit.Extensions;
using Cockpit.Features.UserInputRequests;
using Microsoft.AspNetCore.Components;

namespace Cockpit.Components.Pages.ChatPanel.UserInputRequest;

public partial class UserInputRequestDetailsPopup
{
	[Parameter] public UserInputRequestModel? Request { get; set; }

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
