using Cockpit.Features.UIState;
using Microsoft.AspNetCore.Components;

namespace Cockpit.Components.Popups.Settings;

public partial class InputSettings
{
	[Inject] UIStateFeature _uiState { get; set; } = default!;
}