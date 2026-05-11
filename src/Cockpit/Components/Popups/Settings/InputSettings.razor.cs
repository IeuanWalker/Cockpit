using Cockpit.Features.UIState;

namespace Cockpit.Components.Popups.Settings;

public partial class InputSettings
{
	readonly IUIStateFeature _uiState;
	public InputSettings(IUIStateFeature uiState)
	{
		_uiState = uiState;
	}
}