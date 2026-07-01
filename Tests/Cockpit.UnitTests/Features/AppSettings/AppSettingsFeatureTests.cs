using Cockpit.Features.MessageMode;
using Cockpit.Features.TextToSpeech;
using Cockpit.Features.Theme;
using Shouldly;

namespace Cockpit.UnitTests.Features.AppSettings;

/// <summary>
/// Tests for <see cref="UserAppSettings"/> using an in-memory preferences store
/// so no MAUI platform is required.
/// </summary>
public class AppSettingsFeatureTests
{
	static UserAppSettings CreateSettings(InMemoryPreferencesStorage? store = null)
		=> new(store ?? new InMemoryPreferencesStorage());

	// -------------------------------------------------------------------------
	// Theme
	// -------------------------------------------------------------------------

	[Fact]
	public void Theme_DefaultsTo_Dark()
	{
		UserAppSettings settings = CreateSettings();
		settings.Theme.ShouldBe(ThemeEnum.Dark);
	}

	[Theory]
	[InlineData(ThemeEnum.Light)]
	[InlineData(ThemeEnum.Dark)]
	[InlineData(ThemeEnum.System)]
	public void Theme_RoundTrip_PreservesValue(ThemeEnum theme)
	{
		UserAppSettings settings = CreateSettings();
		settings.Theme = theme;
		settings.Theme.ShouldBe(theme);
	}

	[Fact]
	public void Theme_InvalidStoredString_FallsBackTo_Dark()
	{
		InMemoryPreferencesStorage store = new();
		store.Set("Theme", "NotATheme");

		UserAppSettings settings = CreateSettings(store);
		settings.Theme.ShouldBe(ThemeEnum.Dark);
	}

	[Fact]
	public void Theme_NullStoredString_FallsBackTo_Dark()
	{
		InMemoryPreferencesStorage store = new();
		store.Set<string?>("Theme", null);

		UserAppSettings settings = CreateSettings(store);
		settings.Theme.ShouldBe(ThemeEnum.Dark);
	}

	[Fact]
	public void Theme_CaseInsensitiveStoredString_Parses()
	{
		InMemoryPreferencesStorage store = new();
		store.Set("Theme", "light");

		UserAppSettings settings = CreateSettings(store);
		settings.Theme.ShouldBe(ThemeEnum.Light);
	}

	// -------------------------------------------------------------------------
	// Accent colors
	// -------------------------------------------------------------------------

	[Fact]
	public void AccentColor_DefaultsTo_CorporateBlue()
	{
		UserAppSettings settings = CreateSettings();
		settings.AccentColor.ShouldBe("#005FB8");
	}

	[Fact]
	public void AccentColor_RoundTrip_PreservesValue()
	{
		UserAppSettings settings = CreateSettings();
		settings.AccentColor = "#FF0000";
		settings.AccentColor.ShouldBe("#FF0000");
	}

	[Fact]
	public void AccentHoverColor_DefaultsTo_DarkerBlue()
	{
		UserAppSettings settings = CreateSettings();
		settings.AccentHoverColor.ShouldBe("#0050a0");
	}

	[Fact]
	public void AccentHoverColor_RoundTrip_PreservesValue()
	{
		UserAppSettings settings = CreateSettings();
		settings.AccentHoverColor = "#CC0000";
		settings.AccentHoverColor.ShouldBe("#CC0000");
	}

	// -------------------------------------------------------------------------
	// MessageTurnMode
	// -------------------------------------------------------------------------

	[Fact]
	public void MessageTurnMode_DefaultsTo_Immediate()
	{
		UserAppSettings settings = CreateSettings();
		settings.MessageTurnMode.ShouldBe(MessageTurnModeEnum.Immediate);
	}

	[Theory]
	[InlineData(MessageTurnModeEnum.Immediate)]
	[InlineData(MessageTurnModeEnum.Enqueue)]
	public void MessageTurnMode_RoundTrip_PreservesValue(MessageTurnModeEnum mode)
	{
		UserAppSettings settings = CreateSettings();
		settings.MessageTurnMode = mode;
		settings.MessageTurnMode.ShouldBe(mode);
	}

	[Fact]
	public void MessageTurnMode_InvalidStoredString_FallsBackTo_Immediate()
	{
		InMemoryPreferencesStorage store = new();
		store.Set("MessageTurnMode", "WarpSpeed");

		UserAppSettings settings = CreateSettings(store);
		settings.MessageTurnMode.ShouldBe(MessageTurnModeEnum.Immediate);
	}

	[Fact]
	public void MessageTurnMode_NullStoredString_FallsBackTo_Immediate()
	{
		InMemoryPreferencesStorage store = new();
		store.Set<string?>("MessageTurnMode", null);

		UserAppSettings settings = CreateSettings(store);
		settings.MessageTurnMode.ShouldBe(MessageTurnModeEnum.Immediate);
	}

	[Fact]
	public void MessageTurnMode_CaseInsensitiveStoredString_Parses()
	{
		InMemoryPreferencesStorage store = new();
		store.Set("MessageTurnMode", "enqueue");

		UserAppSettings settings = CreateSettings(store);
		settings.MessageTurnMode.ShouldBe(MessageTurnModeEnum.Enqueue);
	}

	// -------------------------------------------------------------------------
	// Input settings
	// -------------------------------------------------------------------------

	[Fact]
	public void SendOnEnter_DefaultsTo_True()
	{
		UserAppSettings settings = CreateSettings();
		settings.SendOnEnter.ShouldBeTrue();
	}

	[Theory]
	[InlineData(true)]
	[InlineData(false)]
	public void SendOnEnter_RoundTrip_PreservesValue(bool value)
	{
		UserAppSettings settings = CreateSettings();
		settings.SendOnEnter = value;
		settings.SendOnEnter.ShouldBe(value);
	}

	// -------------------------------------------------------------------------
	// Sidebar widths
	// -------------------------------------------------------------------------

	[Fact]
	public void LeftSidebarWidth_DefaultsTo_224()
	{
		UserAppSettings settings = CreateSettings();
		settings.LeftSidebarWidth.ShouldBe(224);
	}

	[Fact]
	public void LeftSidebarWidth_RoundTrip_PreservesValue()
	{
		UserAppSettings settings = CreateSettings();
		settings.LeftSidebarWidth = 300;
		settings.LeftSidebarWidth.ShouldBe(300);
	}

	[Fact]
	public void RightSidebarWidth_DefaultsTo_256()
	{
		UserAppSettings settings = CreateSettings();
		settings.RightSidebarWidth.ShouldBe(256);
	}

	[Fact]
	public void RightSidebarWidth_RoundTrip_PreservesValue()
	{
		UserAppSettings settings = CreateSettings();
		settings.RightSidebarWidth = 350;
		settings.RightSidebarWidth.ShouldBe(350);
	}

	// -------------------------------------------------------------------------
	// Diff view settings
	// -------------------------------------------------------------------------

	[Fact]
	public void DiffSplitView_DefaultsTo_False()
	{
		UserAppSettings settings = CreateSettings();
		settings.DiffSplitView.ShouldBeFalse();
	}

	[Fact]
	public void DiffTreeView_DefaultsTo_True()
	{
		UserAppSettings settings = CreateSettings();
		settings.DiffTreeView.ShouldBeTrue();
	}

	// -------------------------------------------------------------------------
	// Text-to-speech / voice settings
	// -------------------------------------------------------------------------

	[Fact]
	public void TextToSpeechEnabled_DefaultsTo_False()
	{
		UserAppSettings settings = CreateSettings();
		settings.TextToSpeechEnabled.ShouldBeFalse();
	}

	[Fact]
	public void VoiceVolume_DefaultsTo_FeatureDefault()
	{
		UserAppSettings settings = CreateSettings();
		settings.VoiceVolume.ShouldBe(TextToSpeechFeature.DefaultVoiceVolume);
	}

	[Fact]
	public void VoicePitch_DefaultsTo_FeatureDefault()
	{
		UserAppSettings settings = CreateSettings();
		settings.VoicePitch.ShouldBe(TextToSpeechFeature.DefaultVoicePitch);
	}

	[Fact]
	public void VoiceRate_DefaultsTo_FeatureDefault()
	{
		UserAppSettings settings = CreateSettings();
		settings.VoiceRate.ShouldBe(TextToSpeechFeature.DefaultVoiceRate);
	}

	[Fact]
	public void VoiceLocale_DefaultsTo_Empty()
	{
		UserAppSettings settings = CreateSettings();
		settings.VoiceLocale.ShouldBe(string.Empty);
	}

	[Fact]
	public void VoiceVolume_RoundTrip_PreservesValue()
	{
		UserAppSettings settings = CreateSettings();
		settings.VoiceVolume = 0.75f;
		settings.VoiceVolume.ShouldBe(0.75f);
	}

	// -------------------------------------------------------------------------
	// Sound settings — defaults
	// -------------------------------------------------------------------------

	[Fact]
	public void SoundPermissionEnabled_DefaultsTo_True()
	{
		UserAppSettings settings = CreateSettings();
		settings.SoundPermissionEnabled.ShouldBeTrue();
	}

	[Fact]
	public void SoundPermissionVolume_DefaultsTo_HalfVolume()
	{
		UserAppSettings settings = CreateSettings();
		settings.SoundPermissionVolume.ShouldBe(0.5f);
	}

	[Fact]
	public void SoundUserInputEnabled_DefaultsTo_True()
	{
		UserAppSettings settings = CreateSettings();
		settings.SoundUserInputEnabled.ShouldBeTrue();
	}

	[Fact]
	public void SoundUserInputVolume_DefaultsTo_HalfVolume()
	{
		UserAppSettings settings = CreateSettings();
		settings.SoundUserInputVolume.ShouldBe(0.5f);
	}

	[Fact]
	public void SoundFinishedEnabled_DefaultsTo_True()
	{
		UserAppSettings settings = CreateSettings();
		settings.SoundFinishedEnabled.ShouldBeTrue();
	}

	[Fact]
	public void SoundFinishedVolume_DefaultsTo_HalfVolume()
	{
		UserAppSettings settings = CreateSettings();
		settings.SoundFinishedVolume.ShouldBe(0.5f);
	}

	[Fact]
	public void SoundPermissionCustomFileName_DefaultsTo_Empty()
	{
		UserAppSettings settings = CreateSettings();
		settings.SoundPermissionCustomFileName.ShouldBe(string.Empty);
	}

	[Fact]
	public void SoundUserInputCustomFileName_DefaultsTo_Empty()
	{
		UserAppSettings settings = CreateSettings();
		settings.SoundUserInputCustomFileName.ShouldBe(string.Empty);
	}

	[Fact]
	public void SoundFinishedCustomFileName_DefaultsTo_Empty()
	{
		UserAppSettings settings = CreateSettings();
		settings.SoundFinishedCustomFileName.ShouldBe(string.Empty);
	}

	// -------------------------------------------------------------------------
	// Sound settings — round-trip
	// -------------------------------------------------------------------------

	[Fact]
	public void SoundPermissionEnabled_RoundTrip_PreservesValue()
	{
		UserAppSettings settings = CreateSettings();
		settings.SoundPermissionEnabled = false;
		settings.SoundPermissionEnabled.ShouldBeFalse();
	}

	[Fact]
	public void SoundPermissionVolume_RoundTrip_PreservesValue()
	{
		UserAppSettings settings = CreateSettings();
		settings.SoundPermissionVolume = 0.8f;
		settings.SoundPermissionVolume.ShouldBe(0.8f);
	}

	[Fact]
	public void SoundFinishedCustomFileName_RoundTrip_PreservesValue()
	{
		UserAppSettings settings = CreateSettings();
		settings.SoundFinishedCustomFileName = "custom-done.mp3";
		settings.SoundFinishedCustomFileName.ShouldBe("custom-done.mp3");
	}

	// -------------------------------------------------------------------------
	// Isolation: separate instances share no state
	// -------------------------------------------------------------------------

	[Fact]
	public void TwoInstances_WithSeparateStores_DoNotShareState()
	{
		UserAppSettings a = CreateSettings();
		UserAppSettings b = CreateSettings();

		a.AccentColor = "#111111";
		b.AccentColor.ShouldBe("#005FB8"); // default, unaffected by 'a'
	}

	[Fact]
	public void AutoInstallDownloadedUpdateWhenNoActiveSession_DefaultsTo_False()
	{
		UserAppSettings settings = CreateSettings();
		settings.AutoInstallDownloadedUpdateWhenNoActiveSession.ShouldBeFalse();
	}

	[Fact]
	public void AutoInstallDownloadedUpdateWhenNoActiveSession_RoundTrip_PreservesValue()
	{
		UserAppSettings settings = CreateSettings();
		settings.AutoInstallDownloadedUpdateWhenNoActiveSession = true;
		settings.AutoInstallDownloadedUpdateWhenNoActiveSession.ShouldBeTrue();
	}
}
