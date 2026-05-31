using Cockpit.Features.MessageMode;
using Cockpit.Features.Theme;

namespace Cockpit.Features.AppSettings;

public interface IAppSettingsFeature
{
	ThemeEnum Theme { get; set; }
	string AccentColor { get; set; }
	string AccentHoverColor { get; set; }
	MessageTurnModeEnum MessageTurnMode { get; set; }
	bool SendOnEnter { get; set; }
	int LeftSidebarWidth { get; set; }
	int RightSidebarWidth { get; set; }
	bool DiffSplitView { get; set; }
	bool DiffTreeView { get; set; }
	bool SoundPermissionEnabled { get; set; }
	float SoundPermissionVolume { get; set; }
	bool SoundUserInputEnabled { get; set; }
	float SoundUserInputVolume { get; set; }
	bool SoundFinishedEnabled { get; set; }
	float SoundFinishedVolume { get; set; }
	string SoundPermissionCustomFileName { get; set; }
	string SoundUserInputCustomFileName { get; set; }
	string SoundFinishedCustomFileName { get; set; }
	bool TextToSpeechEnabled { get; set; }
	float VoiceVolume { get; set; }
	float VoicePitch { get; set; }
	float VoiceRate { get; set; }
	string VoiceLocale { get; set; }
	bool KeepAlive { get; set; }
	bool CanvasEnabled { get; set; }
}
