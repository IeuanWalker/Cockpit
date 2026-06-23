using Cockpit.Features.MessageMode;
using Cockpit.Features.SystemMessage;
using Cockpit.Features.Theme;

namespace Cockpit.Features.AppSettings;

public sealed class AppSettingsFeature : IAppSettingsFeature
{
	readonly UserAppSettings _settings;

	public AppSettingsFeature(UserAppSettings settings)
	{
		_settings = settings;
	}

	public ThemeEnum Theme
	{
		get => _settings.Theme;
		set => _settings.Theme = value;
	}

	public string AccentColor
	{
		get => _settings.AccentColor;
		set => _settings.AccentColor = value;
	}

	public string AccentHoverColor
	{
		get => _settings.AccentHoverColor;
		set => _settings.AccentHoverColor = value;
	}

	public MessageTurnModeEnum MessageTurnMode
	{
		get => _settings.MessageTurnMode;
		set => _settings.MessageTurnMode = value;
	}

	public bool SendOnEnter
	{
		get => _settings.SendOnEnter;
		set => _settings.SendOnEnter = value;
	}

	public int LeftSidebarWidth
	{
		get => _settings.LeftSidebarWidth;
		set => _settings.LeftSidebarWidth = value;
	}

	public int RightSidebarWidth
	{
		get => _settings.RightSidebarWidth;
		set => _settings.RightSidebarWidth = value;
	}

	public bool DiffSplitView
	{
		get => _settings.DiffSplitView;
		set => _settings.DiffSplitView = value;
	}

	public bool DiffTreeView
	{
		get => _settings.DiffTreeView;
		set => _settings.DiffTreeView = value;
	}

	public bool SoundPermissionEnabled
	{
		get => _settings.SoundPermissionEnabled;
		set => _settings.SoundPermissionEnabled = value;
	}

	public float SoundPermissionVolume
	{
		get => _settings.SoundPermissionVolume;
		set => _settings.SoundPermissionVolume = value;
	}

	public bool SoundUserInputEnabled
	{
		get => _settings.SoundUserInputEnabled;
		set => _settings.SoundUserInputEnabled = value;
	}

	public float SoundUserInputVolume
	{
		get => _settings.SoundUserInputVolume;
		set => _settings.SoundUserInputVolume = value;
	}

	public bool SoundFinishedEnabled
	{
		get => _settings.SoundFinishedEnabled;
		set => _settings.SoundFinishedEnabled = value;
	}

	public float SoundFinishedVolume
	{
		get => _settings.SoundFinishedVolume;
		set => _settings.SoundFinishedVolume = value;
	}

	public string SoundPermissionCustomFileName
	{
		get => _settings.SoundPermissionCustomFileName;
		set => _settings.SoundPermissionCustomFileName = value;
	}

	public string SoundUserInputCustomFileName
	{
		get => _settings.SoundUserInputCustomFileName;
		set => _settings.SoundUserInputCustomFileName = value;
	}

	public string SoundFinishedCustomFileName
	{
		get => _settings.SoundFinishedCustomFileName;
		set => _settings.SoundFinishedCustomFileName = value;
	}

	public bool TextToSpeechEnabled
	{
		get => _settings.TextToSpeechEnabled;
		set => _settings.TextToSpeechEnabled = value;
	}

	public float VoiceVolume
	{
		get => _settings.VoiceVolume;
		set => _settings.VoiceVolume = value;
	}

	public float VoicePitch
	{
		get => _settings.VoicePitch;
		set => _settings.VoicePitch = value;
	}

	public float VoiceRate
	{
		get => _settings.VoiceRate;
		set => _settings.VoiceRate = value;
	}

	public string VoiceLocale
	{
		get => _settings.VoiceLocale;
		set => _settings.VoiceLocale = value;
	}

	public bool KeepAlive
	{
		get => _settings.KeepAlive;
		set => _settings.KeepAlive = value;
	}

	public bool CanvasEnabled
	{
		get => _settings.CanvasEnabled;
		set => _settings.CanvasEnabled = value;
	}

	public string SessionListGroupBy
	{
		get => _settings.SessionListGroupBy;
		set => _settings.SessionListGroupBy = value;
	}

	public Dictionary<string, SystemMessageSectionSetting> SystemMessageSectionOverrides
	{
		get => _settings.SystemMessageSectionOverrides;
		set => _settings.SystemMessageSectionOverrides = value;
	}
}
