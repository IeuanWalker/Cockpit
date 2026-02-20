using Cockpit.Features.Theme;

namespace Cockpit.Features.AppSettings;

public interface IAppSettingsFeature
{
	ThemeEnum Theme { get; set; }
	string AccentColor { get; set; }
	string AccentHoverColor { get; set; }
	bool SendOnEnter { get; set; }
	int LeftSidebarWidth { get; set; }
	int RightSidebarWidth { get; set; }
	bool TextToSpeechEnabled { get; set; }
	float VoiceVolume { get; set; }
	float VoicePitch { get; set; }
	float VoiceRate { get; set; }
	string VoiceLocale { get; set; }
}
