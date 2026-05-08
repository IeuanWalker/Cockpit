using Cockpit.Features.MessageMode;
using Microsoft.AspNetCore.Components;

namespace Cockpit.Components.Pages.ChatPanel.InputArea;

public partial class MessageTurnModeControl : ComponentBase
{
	bool _isOpen;

	MessageTurnModeEnum CurrentMode => UserAppSettings.MessageTurnMode;

	void Toggle() => _isOpen = !_isOpen;

	void Close()
	{
		_isOpen = false;
		StateHasChanged();
	}

	void Select(MessageTurnModeEnum mode)
	{
		UserAppSettings.MessageTurnMode = mode;
		_isOpen = false;
		StateHasChanged();
	}

	string GetTitle() => CurrentMode switch
	{
		MessageTurnModeEnum.Enqueue => "Message mode: Enqueue — messages are queued behind any in-flight turn",
		_ => "Message mode: Immediate — messages are sent immediately"
	};
}
