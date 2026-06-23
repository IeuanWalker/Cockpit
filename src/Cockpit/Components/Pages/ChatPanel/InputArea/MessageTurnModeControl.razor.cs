using Cockpit.Features.AppSettings;
using Cockpit.Features.MessageMode;
using Microsoft.AspNetCore.Components;

namespace Cockpit.Components.Pages.ChatPanel.InputArea;

public partial class MessageTurnModeControl : ComponentBase
{
	readonly IAppSettingsFeature _appSettings;

	public MessageTurnModeControl(IAppSettingsFeature appSettings)
	{
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

	async Task OpenMoreInfoAsync()
	{
		_isOpen = false;
		try
		{
			await Microsoft.Maui.ApplicationModel.Launcher.OpenAsync(new Uri("https://github.com/github/copilot-sdk/blob/main/docs/features/steering-and-queueing.md"));
		}
		catch (Exception ex)
		{
			System.Diagnostics.Debug.WriteLine($"[MessageTurnModeControl] Failed to open more info URL: {ex.Message}");
		}
	}
}
