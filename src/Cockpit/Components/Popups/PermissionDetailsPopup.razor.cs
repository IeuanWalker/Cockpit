using System.Text.Json;
using Cockpit.Features.Permissions.Models;
using Microsoft.AspNetCore.Components;

namespace Cockpit.Components.Popups;

public partial class PermissionDetailsPopup
{
	[Parameter]
	public bool IsOpen { get; set; }

	[Parameter]
	public PermissionRequestModel? Request { get; set; }

	[Parameter]
	public EventCallback OnClose { get; set; }

	async Task Close()
	{
		await OnClose.InvokeAsync();
	}

	string FormatAsJson(string input)
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