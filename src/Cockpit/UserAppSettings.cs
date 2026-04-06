using Cockpit.Features.TextToSpeech;
using Cockpit.Features.Theme;

namespace Cockpit;

public static class UserAppSettings
{
	public static ThemeEnum Theme
	{
		get
		{
			string? result = Preferences.Default.Get("Theme", (string?)null);

			if(result is null)
			{
				return ThemeEnum.Dark;
			}

			if(!Enum.IsDefined(typeof(ThemeEnum), result))
			{
				return ThemeEnum.Dark;
			}

			return Enum.Parse<ThemeEnum>(result);
		}

		set => Preferences.Default.Set("Theme", value.ToString());
	}
	public static string AccentColor
	{
		get => Preferences.Default.Get("AccentColor", "#005FB8");
		set => Preferences.Default.Set("AccentColor", value);
	}
	public static string AccentHoverColor
	{
		get => Preferences.Default.Get("AccentHoverColor", "#0050a0");
		set => Preferences.Default.Set("AccentHoverColor", value);
	}

	public static bool SendOnEnter
	{
		get => Preferences.Default.Get("SendOnEnter", true);
		set => Preferences.Default.Set("SendOnEnter", value);
	}

	public static int LeftSidebarWidth
	{
		get => Preferences.Default.Get("LeftSidebarWidth", 224);
		set => Preferences.Default.Set("LeftSidebarWidth", value);
	}

	public static int RightSidebarWidth
	{
		get => Preferences.Default.Get("RightSidebarWidth", 256);
		set => Preferences.Default.Set("RightSidebarWidth", value);
	}

	public static bool TextToSpeechEnabled
	{
		get => Preferences.Default.Get("TextToSpeechEnabled", false);
		set => Preferences.Default.Set("TextToSpeechEnabled", value);
	}

	public static float VoiceVolume
	{
		get => Preferences.Default.Get("VoiceVolume", TextToSpeechFeature.DefaultVoiceVolume);
		set => Preferences.Default.Set("VoiceVolume", value);
	}

	public static float VoicePitch
	{
		get => Preferences.Default.Get("VoicePitch", TextToSpeechFeature.DefaultVoicePitch);
		set => Preferences.Default.Set("VoicePitch", value);
	}

	public static float VoiceRate
	{
		get => Preferences.Default.Get("VoiceRate", TextToSpeechFeature.DefaultVoiceRate);
		set => Preferences.Default.Set("VoiceRate", value);
	}

	public static string VoiceLocale
	{
		get => Preferences.Default.Get("VoiceLocale", string.Empty);
		set => Preferences.Default.Set("VoiceLocale", value);
	}

	public static bool DiffSplitView
	{
		get => Preferences.Default.Get("DiffSplitView", false);
		set => Preferences.Default.Set("DiffSplitView", value);
	}

	public static bool SoundPermissionEnabled
	{
		get => Preferences.Default.Get("SoundPermissionEnabled", true);
		set => Preferences.Default.Set("SoundPermissionEnabled", value);
	}

	public static float SoundPermissionVolume
	{
		get => Preferences.Default.Get("SoundPermissionVolume", 0.5f);
		set => Preferences.Default.Set("SoundPermissionVolume", value);
	}

	public static bool SoundUserInputEnabled
	{
		get => Preferences.Default.Get("SoundUserInputEnabled", true);
		set => Preferences.Default.Set("SoundUserInputEnabled", value);
	}

	public static float SoundUserInputVolume
	{
		get => Preferences.Default.Get("SoundUserInputVolume", 0.5f);
		set => Preferences.Default.Set("SoundUserInputVolume", value);
	}

	public static bool SoundFinishedEnabled
	{
		get => Preferences.Default.Get("SoundFinishedEnabled", true);
		set => Preferences.Default.Set("SoundFinishedEnabled", value);
	}

	public static float SoundFinishedVolume
	{
		get => Preferences.Default.Get("SoundFinishedVolume", 0.5f);
		set => Preferences.Default.Set("SoundFinishedVolume", value);
	}

	public static string SoundPermissionCustomFileName
	{
		get => Preferences.Default.Get("SoundPermissionCustomFileName", string.Empty);
		set => Preferences.Default.Set("SoundPermissionCustomFileName", value);
	}

	public static string SoundUserInputCustomFileName
	{
		get => Preferences.Default.Get("SoundUserInputCustomFileName", string.Empty);
		set => Preferences.Default.Set("SoundUserInputCustomFileName", value);
	}

	public static string SoundFinishedCustomFileName
	{
		get => Preferences.Default.Get("SoundFinishedCustomFileName", string.Empty);
		set => Preferences.Default.Set("SoundFinishedCustomFileName", value);
	}
}
