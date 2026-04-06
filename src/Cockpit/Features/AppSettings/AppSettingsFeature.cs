using Cockpit.Features.Theme;

namespace Cockpit.Features.AppSettings;

public class AppSettingsFeature : IAppSettingsFeature
{
	public ThemeEnum Theme
	{
		get => UserAppSettings.Theme;
		set => UserAppSettings.Theme = value;
	}

	public string AccentColor
	{
		get => UserAppSettings.AccentColor;
		set => UserAppSettings.AccentColor = value;
	}

	public string AccentHoverColor
	{
		get => UserAppSettings.AccentHoverColor;
		set => UserAppSettings.AccentHoverColor = value;
	}

	public bool SendOnEnter
	{
		get => UserAppSettings.SendOnEnter;
		set => UserAppSettings.SendOnEnter = value;
	}

	public int LeftSidebarWidth
	{
		get => UserAppSettings.LeftSidebarWidth;
		set => UserAppSettings.LeftSidebarWidth = value;
	}

	public int RightSidebarWidth
	{
		get => UserAppSettings.RightSidebarWidth;
		set => UserAppSettings.RightSidebarWidth = value;
	}

	public bool DiffSplitView
	{
		get => UserAppSettings.DiffSplitView;
		set => UserAppSettings.DiffSplitView = value;
	}

	public bool SoundPermissionEnabled
	{
		get => UserAppSettings.SoundPermissionEnabled;
		set => UserAppSettings.SoundPermissionEnabled = value;
	}

	public float SoundPermissionVolume
	{
		get => UserAppSettings.SoundPermissionVolume;
		set => UserAppSettings.SoundPermissionVolume = value;
	}

	public bool SoundUserInputEnabled
	{
		get => UserAppSettings.SoundUserInputEnabled;
		set => UserAppSettings.SoundUserInputEnabled = value;
	}

	public float SoundUserInputVolume
	{
		get => UserAppSettings.SoundUserInputVolume;
		set => UserAppSettings.SoundUserInputVolume = value;
	}

	public bool SoundFinishedEnabled
	{
		get => UserAppSettings.SoundFinishedEnabled;
		set => UserAppSettings.SoundFinishedEnabled = value;
	}

	public float SoundFinishedVolume
	{
		get => UserAppSettings.SoundFinishedVolume;
		set => UserAppSettings.SoundFinishedVolume = value;
	}

	public string SoundPermissionCustomFileName
	{
		get => UserAppSettings.SoundPermissionCustomFileName;
		set => UserAppSettings.SoundPermissionCustomFileName = value;
	}

	public string SoundUserInputCustomFileName
	{
		get => UserAppSettings.SoundUserInputCustomFileName;
		set => UserAppSettings.SoundUserInputCustomFileName = value;
	}

	public string SoundFinishedCustomFileName
	{
		get => UserAppSettings.SoundFinishedCustomFileName;
		set => UserAppSettings.SoundFinishedCustomFileName = value;
	}

	public bool TextToSpeechEnabled
	{
		get => UserAppSettings.TextToSpeechEnabled;
		set => UserAppSettings.TextToSpeechEnabled = value;
	}

	public float VoiceVolume
	{
		get => UserAppSettings.VoiceVolume;
		set => UserAppSettings.VoiceVolume = value;
	}

	public float VoicePitch
	{
		get => UserAppSettings.VoicePitch;
		set => UserAppSettings.VoicePitch = value;
	}

	public float VoiceRate
	{
		get => UserAppSettings.VoiceRate;
		set => UserAppSettings.VoiceRate = value;
	}

	public string VoiceLocale
	{
		get => UserAppSettings.VoiceLocale;
		set => UserAppSettings.VoiceLocale = value;
	}
}
