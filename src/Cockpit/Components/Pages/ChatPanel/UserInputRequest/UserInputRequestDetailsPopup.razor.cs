using System.Text.Json;
using Cockpit.Components.Controls;
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
			using JsonDocument doc = JsonDocument.Parse(input);
			return JsonSerializer.Serialize(doc, new JsonSerializerOptions
			{
				WriteIndented = true
			});
		}
		catch
		{
			return input;
		}
	}
}
