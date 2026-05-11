using Cockpit.Features.AppSettings;
using Cockpit.Features.MessageMode;
using Microsoft.AspNetCore.Components;

namespace Cockpit.Components.Pages.ChatPanel.InputArea;

public partial class MessageTurnModeControl : ComponentBase
{
	readonly IAppSettingsFeature _appSettings;

	public MessageTurnModeControl(IAppSettingsFeature appSettings)
	{
		ArgumentNullException.ThrowIfNull(appSettings);
		_appSettings = appSettings;
	}

	bool _isOpen;

	MessageTurnModeEnum CurrentMode => _appSettings.MessageTurnMode;

	void Toggle() => _isOpen = !_isOpen;

	void Close() => _isOpen = false;

	void Select(MessageTurnModeEnum mode)
	{
		_appSettings.MessageTurnMode = mode;
		_isOpen = false;
	}

	string GetTitle() => CurrentMode switch
	{
		MessageTurnModeEnum.Enqueue => "Message mode: Enqueue — messages are queued behind any in-flight turn",
		_ => "Message mode: Immediate — messages are sent immediately"
	};
}
