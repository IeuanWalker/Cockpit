using Cockpit.Services;
using Microsoft.AspNetCore.Components;

namespace Cockpit.Components.Popups.Settings;

public partial class InputSettings
{
	[Inject] UIStateService _uiState { get; set; } = default!;
}