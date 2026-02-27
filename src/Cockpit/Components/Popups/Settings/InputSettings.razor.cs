using Cockpit.Features.UIState;

namespace Cockpit.Components.Popups.Settings;

public partial class InputSettings
{
	readonly UIStateFeature _uiState;
	public InputSettings(UIStateFeature uiState)
	{
		_uiState = uiState;
	}
}