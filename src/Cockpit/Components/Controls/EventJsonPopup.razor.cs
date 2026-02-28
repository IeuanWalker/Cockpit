using Cockpit.Utilities;
using Microsoft.AspNetCore.Components;

namespace Cockpit.Components.Controls;

public partial class EventJsonPopup
{
	PopupBase? _popup;
	[Parameter] public List<string>? JsonList { get; set; }
	public void Open() => _popup?.Open();

	string FormatAsJson(List<string>? jsonList) => JsonUtil.FormatJsonList(jsonList);
}