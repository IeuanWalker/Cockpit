using System.Text.Json;
using Microsoft.AspNetCore.Components;

namespace Cockpit.Components.Controls;

public partial class EventJsonPopup
{
	PopupBase? _popup;
	[Parameter] public string? Json { get; set; }
	public void Open() => _popup?.Open();

	string FormatAsJson(string? json)
	{
		if(string.IsNullOrWhiteSpace(json))
		{
			return string.Empty;
		}

		try
		{
			JsonDocument parsed = JsonDocument.Parse(json);
			return JsonSerializer.Serialize(parsed, new JsonSerializerOptions { WriteIndented = true });
		}
		catch
		{
			return json ?? string.Empty;
		}
	}
}