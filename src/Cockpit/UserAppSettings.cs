using System.Text.Json;
using Cockpit.Features.AppSettings;
using Cockpit.Features.MessageMode;
using Cockpit.Features.SystemMessage;
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
	static class Keys
	{
		internal const string theme = "Theme";
		internal const string accentColor = "AccentColor";
		internal const string accentHoverColor = "AccentHoverColor";
		internal const string messageTurnMode = "MessageTurnMode";
		internal const string sendOnEnter = "SendOnEnter";
		internal const string leftSidebarWidth = "LeftSidebarWidth";
		internal const string rightSidebarWidth = "RightSidebarWidth";
		internal const string textToSpeechEnabled = "TextToSpeechEnabled";
		internal const string voiceVolume = "VoiceVolume";
		internal const string voicePitch = "VoicePitch";
		internal const string voiceRate = "VoiceRate";
		internal const string voiceLocale = "VoiceLocale";
		internal const string diffSplitView = "DiffSplitView";
		internal const string diffTreeView = "DiffTreeView";
		internal const string soundPermissionEnabled = "SoundPermissionEnabled";
		internal const string soundPermissionVolume = "SoundPermissionVolume";
		internal const string soundUserInputEnabled = "SoundUserInputEnabled";
		internal const string soundUserInputVolume = "SoundUserInputVolume";
		internal const string soundFinishedEnabled = "SoundFinishedEnabled";
		internal const string soundFinishedVolume = "SoundFinishedVolume";
		internal const string soundPermissionCustomFileName = "SoundPermissionCustomFileName";
		internal const string soundUserInputCustomFileName = "SoundUserInputCustomFileName";
		internal const string soundFinishedCustomFileName = "SoundFinishedCustomFileName";
		internal const string sdkLogLevel = "SdkLogLevel";
		internal const string telemetryEnabled = "TelemetryEnabled";
		internal const string keepAlive = "KeepAlive";
		internal const string canvasEnabled = "CanvasEnabled";
		internal const string sessionListGroupBy = "SessionListGroupBy";
		internal const string systemMessageSectionOverrides = "SystemMessageSectionOverrides";
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
			string? stored = _preferences.Get<string?>(Keys.theme, null);
			return Enum.TryParse(stored, true, out ThemeEnum parsed) && Enum.IsDefined(parsed)
				? parsed
				: ThemeEnum.Dark;
		}
		set => _preferences.Set(Keys.theme, value.ToString());
	}

	public string AccentColor
	{
		get => _preferences.Get(Keys.accentColor, "#005FB8");
		set => _preferences.Set(Keys.accentColor, value);
	}

	public string AccentHoverColor
	{
		get => _preferences.Get(Keys.accentHoverColor, "#0050a0");
		set => _preferences.Set(Keys.accentHoverColor, value);
	}

	public MessageTurnModeEnum MessageTurnMode
	{
		get
		{
			string? stored = _preferences.Get<string?>(Keys.messageTurnMode, null);
			return Enum.TryParse(stored, true, out MessageTurnModeEnum parsed) && Enum.IsDefined(parsed)
				? parsed
				: MessageTurnModeEnum.Immediate;
		}
		set => _preferences.Set(Keys.messageTurnMode, value.ToString());
	}

	public bool SendOnEnter
	{
		get => _preferences.Get(Keys.sendOnEnter, true);
		set => _preferences.Set(Keys.sendOnEnter, value);
	}

	public int LeftSidebarWidth
	{
		get => _preferences.Get(Keys.leftSidebarWidth, 224);
		set => _preferences.Set(Keys.leftSidebarWidth, value);
	}

	public int RightSidebarWidth
	{
		get => _preferences.Get(Keys.rightSidebarWidth, 256);
		set => _preferences.Set(Keys.rightSidebarWidth, value);
	}

	public bool TextToSpeechEnabled
	{
		get => _preferences.Get(Keys.textToSpeechEnabled, false);
		set => _preferences.Set(Keys.textToSpeechEnabled, value);
	}

	public float VoiceVolume
	{
		get => _preferences.Get(Keys.voiceVolume, TextToSpeechFeature.DefaultVoiceVolume);
		set => _preferences.Set(Keys.voiceVolume, value);
	}

	public float VoicePitch
	{
		get => _preferences.Get(Keys.voicePitch, TextToSpeechFeature.DefaultVoicePitch);
		set => _preferences.Set(Keys.voicePitch, value);
	}

	public float VoiceRate
	{
		get => _preferences.Get(Keys.voiceRate, TextToSpeechFeature.DefaultVoiceRate);
		set => _preferences.Set(Keys.voiceRate, value);
	}

	public string VoiceLocale
	{
		get => _preferences.Get(Keys.voiceLocale, string.Empty);
		set => _preferences.Set(Keys.voiceLocale, value);
	}

	public bool DiffSplitView
	{
		get => _preferences.Get(Keys.diffSplitView, false);
		set => _preferences.Set(Keys.diffSplitView, value);
	}

	public bool DiffTreeView
	{
		get => _preferences.Get(Keys.diffTreeView, true);
		set => _preferences.Set(Keys.diffTreeView, value);
	}

	public bool SoundPermissionEnabled
	{
		get => _preferences.Get(Keys.soundPermissionEnabled, true);
		set => _preferences.Set(Keys.soundPermissionEnabled, value);
	}

	public float SoundPermissionVolume
	{
		get => _preferences.Get(Keys.soundPermissionVolume, 0.5f);
		set => _preferences.Set(Keys.soundPermissionVolume, value);
	}

	public bool SoundUserInputEnabled
	{
		get => _preferences.Get(Keys.soundUserInputEnabled, true);
		set => _preferences.Set(Keys.soundUserInputEnabled, value);
	}

	public float SoundUserInputVolume
	{
		get => _preferences.Get(Keys.soundUserInputVolume, 0.5f);
		set => _preferences.Set(Keys.soundUserInputVolume, value);
	}

	public bool SoundFinishedEnabled
	{
		get => _preferences.Get(Keys.soundFinishedEnabled, true);
		set => _preferences.Set(Keys.soundFinishedEnabled, value);
	}

	public float SoundFinishedVolume
	{
		get => _preferences.Get(Keys.soundFinishedVolume, 0.5f);
		set => _preferences.Set(Keys.soundFinishedVolume, value);
	}

	public string SoundPermissionCustomFileName
	{
		get => _preferences.Get(Keys.soundPermissionCustomFileName, string.Empty);
		set => _preferences.Set(Keys.soundPermissionCustomFileName, value);
	}

	public string SoundUserInputCustomFileName
	{
		get => _preferences.Get(Keys.soundUserInputCustomFileName, string.Empty);
		set => _preferences.Set(Keys.soundUserInputCustomFileName, value);
	}

	public string SoundFinishedCustomFileName
	{
		get => _preferences.Get(Keys.soundFinishedCustomFileName, string.Empty);
		set => _preferences.Set(Keys.soundFinishedCustomFileName, value);
	}

	/// <summary>
	/// SDK CLI log level passed to <see cref="GitHub.Copilot.CopilotClientOptions.LogLevel"/>.
	/// Valid values: "error", "warn", "info", "debug". Default is "info".
	/// Requires client restart to take effect.
	/// </summary>
	public string SdkLogLevel
	{
		get => _preferences.Get(Keys.sdkLogLevel, "info");
		set => _preferences.Set(Keys.sdkLogLevel, value);
	}

	/// <summary>
	/// When enabled, configures the SDK to export OpenTelemetry traces/metrics to a local
	/// JSON-lines file for diagnostics. Requires client restart to take effect.
	/// </summary>
	public bool TelemetryEnabled
	{
		get => _preferences.Get(Keys.telemetryEnabled, false);
		set => _preferences.Set(Keys.telemetryEnabled, value);
	}

	/// <summary>
	/// When enabled, prevents the system from sleeping while an agent session is active.
	/// </summary>
	public bool KeepAlive
	{
		get => _preferences.Get(Keys.keepAlive, true);
		set => _preferences.Set(Keys.keepAlive, value);
	}

	/// <summary>
	/// When enabled, sessions declare canvas support to the Copilot SDK so the agent
	/// can open interactive canvas windows. Requires app restart to take effect.
	/// </summary>
	public bool CanvasEnabled
	{
		get => _preferences.Get(Keys.canvasEnabled, true);
		set => _preferences.Set(Keys.canvasEnabled, value);
	}

	public string SessionListGroupBy
	{
		get => _preferences.Get(Keys.sessionListGroupBy, "Project");
		set => _preferences.Set(Keys.sessionListGroupBy, value);
	}

	/// <summary>
	/// Per-section system message overrides, serialised as JSON.
	/// </summary>
	public Dictionary<string, SystemMessageSectionSetting> SystemMessageSectionOverrides
	{
		get
		{
			string? json = _preferences.Get<string?>(Keys.systemMessageSectionOverrides, null);
			if (string.IsNullOrEmpty(json))
			{
				return [];
			}
			try
			{
				return JsonSerializer.Deserialize<Dictionary<string, SystemMessageSectionSetting>>(json) ?? [];
			}
			catch
			{
				return [];
			}
		}
		set => _preferences.Set(Keys.systemMessageSectionOverrides, JsonSerializer.Serialize(value));
	}
}
