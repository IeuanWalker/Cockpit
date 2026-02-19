using Microsoft.AspNetCore.Components;

namespace Cockpit.Components.Controls;

public partial class JsonHighlight
{
	[Parameter] public string Code { get; set; } = string.Empty;
}
