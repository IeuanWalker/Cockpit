using System.Text.Json;
using Microsoft.AspNetCore.Components;

namespace Cockpit.Components.Controls;

public partial class EventJsonPopup
{
	PopupBase? _popup;
	[Parameter] public List<string>? JsonList { get; set; }
	public void Open() => _popup?.Open();

	string FormatAsJson(List<string>? jsonList)
	{
		if(jsonList == null || jsonList.Count == 0)
		{
			return string.Empty;
		}
		try
		{
			var parsedList = jsonList.Select(json => JsonDocument.Parse(json).RootElement).ToList();
			return JsonSerializer.Serialize(parsedList, new JsonSerializerOptions { WriteIndented = true });
		}
		catch
		{
			return string.Join("\n\n", jsonList);
		}
	}
}