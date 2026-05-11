using Cockpit.Features.AppSettings;
using Cockpit.Features.MessageMode;
using Cockpit.Features.TextToSpeech;
using Cockpit.Features.Theme;

namespace Cockpit;

/// <summary>
/// Persistence layer for all user-configurable application settings.
/// Delegates storage to an <see cref="IPreferencesStorage"/> so the class is testable
/// without requiring a MAUI platform host.
/// </summary>
public class UserAppSettings
{
	/// <summary>Stable preference keys. Never use raw strings at the call site.</summary>
	static class Keys
	{
		internal const string Theme = "Theme";
		internal const string AccentColor = "AccentColor";
		internal const string AccentHoverColor = "AccentHoverColor";
		internal const string MessageTurnMode = "MessageTurnMode";
		internal const string SendOnEnter = "SendOnEnter";
		internal const string LeftSidebarWidth = "LeftSidebarWidth";
		internal const string RightSidebarWidth = "RightSidebarWidth";
		internal const string TextToSpeechEnabled = "TextToSpeechEnabled";
		internal const string VoiceVolume = "VoiceVolume";
		internal const string VoicePitch = "VoicePitch";
		internal const string VoiceRate = "VoiceRate";
		internal const string VoiceLocale = "VoiceLocale";
		internal const string DiffSplitView = "DiffSplitView";
		internal const string DiffTreeView = "DiffTreeView";
		internal const string SoundPermissionEnabled = "SoundPermissionEnabled";
		internal const string SoundPermissionVolume = "SoundPermissionVolume";
		internal const string SoundUserInputEnabled = "SoundUserInputEnabled";
		internal const string SoundUserInputVolume = "SoundUserInputVolume";
		internal const string SoundFinishedEnabled = "SoundFinishedEnabled";
		internal const string SoundFinishedVolume = "SoundFinishedVolume";
		internal const string SoundPermissionCustomFileName = "SoundPermissionCustomFileName";
		internal const string SoundUserInputCustomFileName = "SoundUserInputCustomFileName";
		internal const string SoundFinishedCustomFileName = "SoundFinishedCustomFileName";
	}

	readonly IPreferencesStorage _preferences;

	public UserAppSettings(IPreferencesStorage preferences)
	{
		_preferences = preferences;
	}

	public ThemeEnum Theme
	{
		get
		{
			string? stored = _preferences.Get<string?>(Keys.Theme, null);
			return Enum.TryParse(stored, true, out ThemeEnum parsed) && Enum.IsDefined(parsed)
				? parsed
				: ThemeEnum.Dark;
		}
		set => _preferences.Set(Keys.Theme, value.ToString());
	}

	public string AccentColor
	{
		get => _preferences.Get(Keys.AccentColor, "#005FB8");
		set => _preferences.Set(Keys.AccentColor, value);
	}

	public string AccentHoverColor
	{
		get => _preferences.Get(Keys.AccentHoverColor, "#0050a0");
		set => _preferences.Set(Keys.AccentHoverColor, value);
	}

	public MessageTurnModeEnum MessageTurnMode
	{
		get
		{
			string? stored = _preferences.Get<string?>(Keys.MessageTurnMode, null);
			return Enum.TryParse(stored, true, out MessageTurnModeEnum parsed) && Enum.IsDefined(parsed)
				? parsed
				: MessageTurnModeEnum.Immediate;
		}
		set => _preferences.Set(Keys.MessageTurnMode, value.ToString());
	}

	public bool SendOnEnter
	{
		get => _preferences.Get(Keys.SendOnEnter, true);
		set => _preferences.Set(Keys.SendOnEnter, value);
	}

	public int LeftSidebarWidth
	{
		get => _preferences.Get(Keys.LeftSidebarWidth, 224);
		set => _preferences.Set(Keys.LeftSidebarWidth, value);
	}

	public int RightSidebarWidth
	{
		get => _preferences.Get(Keys.RightSidebarWidth, 256);
		set => _preferences.Set(Keys.RightSidebarWidth, value);
	}

	public bool TextToSpeechEnabled
	{
		get => _preferences.Get(Keys.TextToSpeechEnabled, false);
		set => _preferences.Set(Keys.TextToSpeechEnabled, value);
	}

	public float VoiceVolume
	{
		get => _preferences.Get(Keys.VoiceVolume, TextToSpeechFeature.DefaultVoiceVolume);
		set => _preferences.Set(Keys.VoiceVolume, value);
	}

	public float VoicePitch
	{
		get => _preferences.Get(Keys.VoicePitch, TextToSpeechFeature.DefaultVoicePitch);
		set => _preferences.Set(Keys.VoicePitch, value);
	}

	public float VoiceRate
	{
		get => _preferences.Get(Keys.VoiceRate, TextToSpeechFeature.DefaultVoiceRate);
		set => _preferences.Set(Keys.VoiceRate, value);
	}

	public string VoiceLocale
	{
		get => _preferences.Get(Keys.VoiceLocale, string.Empty);
		set => _preferences.Set(Keys.VoiceLocale, value);
	}

	public bool DiffSplitView
	{
		get => _preferences.Get(Keys.DiffSplitView, false);
		set => _preferences.Set(Keys.DiffSplitView, value);
	}

	public bool DiffTreeView
	{
		get => _preferences.Get(Keys.DiffTreeView, true);
		set => _preferences.Set(Keys.DiffTreeView, value);
	}

	public bool SoundPermissionEnabled
	{
		get => _preferences.Get(Keys.SoundPermissionEnabled, true);
		set => _preferences.Set(Keys.SoundPermissionEnabled, value);
	}

	public float SoundPermissionVolume
	{
		get => _preferences.Get(Keys.SoundPermissionVolume, 0.5f);
		set => _preferences.Set(Keys.SoundPermissionVolume, value);
	}

	public bool SoundUserInputEnabled
	{
		get => _preferences.Get(Keys.SoundUserInputEnabled, true);
		set => _preferences.Set(Keys.SoundUserInputEnabled, value);
	}

	public float SoundUserInputVolume
	{
		get => _preferences.Get(Keys.SoundUserInputVolume, 0.5f);
		set => _preferences.Set(Keys.SoundUserInputVolume, value);
	}

	public bool SoundFinishedEnabled
	{
		get => _preferences.Get(Keys.SoundFinishedEnabled, true);
		set => _preferences.Set(Keys.SoundFinishedEnabled, value);
	}

	public float SoundFinishedVolume
	{
		get => _preferences.Get(Keys.SoundFinishedVolume, 0.5f);
		set => _preferences.Set(Keys.SoundFinishedVolume, value);
	}

	public string SoundPermissionCustomFileName
	{
		get => _preferences.Get(Keys.SoundPermissionCustomFileName, string.Empty);
		set => _preferences.Set(Keys.SoundPermissionCustomFileName, value);
	}

	public string SoundUserInputCustomFileName
	{
		get => _preferences.Get(Keys.SoundUserInputCustomFileName, string.Empty);
		set => _preferences.Set(Keys.SoundUserInputCustomFileName, value);
	}

	public string SoundFinishedCustomFileName
	{
		get => _preferences.Get(Keys.SoundFinishedCustomFileName, string.Empty);
		set => _preferences.Set(Keys.SoundFinishedCustomFileName, value);
	}
}
