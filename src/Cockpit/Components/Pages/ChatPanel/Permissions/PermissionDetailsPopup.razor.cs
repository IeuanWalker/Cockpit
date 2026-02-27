using System.Text.Json;
using Cockpit.Components.Controls;
using Cockpit.Features.Permissions.Models;
using Microsoft.AspNetCore.Components;

namespace Cockpit.Components.Pages.ChatPanel.Permissions;

public partial class PermissionDetailsPopup
{
	[Parameter] public PermissionRequestModel? Request { get; set; }

	[Parameter] public EventCallback OnClose { get; set; }

	PopupBase _popup = default!;

	public void Open() => _popup.Open();

	static string FormatAsJson(string input)
	{
		try
		{
			// Try to parse as JSON and format it
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