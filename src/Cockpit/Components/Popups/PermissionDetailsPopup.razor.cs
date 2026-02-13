using System.Text.Json;
using Cockpit.Models;
using Microsoft.AspNetCore.Components;

namespace Cockpit.Components.Popups;

public partial class PermissionDetailsPopup
{
	[Parameter]
	public bool IsOpen { get; set; }

	[Parameter]
	public PermissionRequest? Request { get; set; }

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
			// If not valid JSON, try to wrap it as a string value
			try
			{
				var wrapper = new { command = input };
				return JsonSerializer.Serialize(wrapper, new JsonSerializerOptions
				{
					WriteIndented = true
				});
			}
			catch
			{
				// Fallback: return as-is
				return input;
			}
		}
	}

	string FormatArgumentsAsJson(Dictionary<string, object>? arguments)
	{
		if(arguments is null)
		{
			return "{}";
		}

		try
		{
			return JsonSerializer.Serialize(arguments, new JsonSerializerOptions
			{
				WriteIndented = true
			});
		}
		catch
		{
			return "{}";
		}
	}
}