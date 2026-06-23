using Cockpit.Features.AppSettings;
using Cockpit.Features.Theme;
using Cockpit.UnitTests.Features.AppSettings;
using Shouldly;

namespace Cockpit.UnitTests.Features.Theme;

/// <summary>
/// Tests for <see cref="ThemeStateFeature"/> — pure logic with no MAUI or JS dependencies.
/// </summary>
public class ThemeStateFeatureTests
{
	static ThemeStateFeature Create(
		ThemeEnum theme = ThemeEnum.Dark,
		string accent = "#005FB8",
		string accentHover = "#0050a0")
	{
		InMemoryPreferencesStorage store = new();
		store.Set("Theme", theme.ToString());
		store.Set("AccentColor", accent);
		store.Set("AccentHoverColor", accentHover);

		IAppSettingsFeature settings = new AppSettingsFeature(new UserAppSettings(store));
		return new ThemeStateFeature(settings);
	}

	// -------------------------------------------------------------------------
	// Constructor — IsLightTheme
	// -------------------------------------------------------------------------

	[Fact]
	public void Constructor_DarkTheme_IsLightTheme_IsFalse()
	{
		ThemeStateFeature feature = Create(ThemeEnum.Dark);
		feature.IsLightTheme.ShouldBeFalse();
	}

	[Fact]
	public void Constructor_LightTheme_IsLightTheme_IsTrue()
	{
		ThemeStateFeature feature = Create(ThemeEnum.Light);
		feature.IsLightTheme.ShouldBeTrue();
	}

	[Fact]
	public void Constructor_SystemTheme_NoMauiApp_IsLightTheme_IsFalse()
	{
		// When Application.Current is null (unit test host), System theme resolves to dark.
		ThemeStateFeature feature = Create(ThemeEnum.System);
		feature.IsLightTheme.ShouldBeFalse();
	}

	// -------------------------------------------------------------------------
	// Constructor — Accent colours
	// -------------------------------------------------------------------------

	[Fact]
	public void Constructor_InitializesAccentColor_FromSettings()
	{
		ThemeStateFeature feature = Create(accent: "#FF0000");
		feature.AccentColor.ShouldBe("#FF0000");
	}

	[Fact]
	public void Constructor_InitializesAccentHoverColor_FromSettings()
	{
		ThemeStateFeature feature = Create(accentHover: "#CC0000");
		feature.AccentHoverColor.ShouldBe("#CC0000");
	}

	// -------------------------------------------------------------------------
	// Update — property propagation
	// -------------------------------------------------------------------------

	[Fact]
	public void Update_SetsIsLightTheme()
	{
		ThemeStateFeature feature = Create(ThemeEnum.Dark);
		feature.Update(true, "#FF0000", "#CC0000");
		feature.IsLightTheme.ShouldBeTrue();
	}

	[Fact]
	public void Update_SetsAccentColor()
	{
		ThemeStateFeature feature = Create();
		feature.Update(false, "#FF0000", "#CC0000");
		feature.AccentColor.ShouldBe("#FF0000");
	}

	[Fact]
	public void Update_SetsAccentHoverColor()
	{
		ThemeStateFeature feature = Create();
		feature.Update(false, "#FF0000", "#CC0000");
		feature.AccentHoverColor.ShouldBe("#CC0000");
	}

	// -------------------------------------------------------------------------
	// Update — event firing
	// -------------------------------------------------------------------------

	[Fact]
	public void Update_FiresOnThemeChangedEvent()
	{
		ThemeStateFeature feature = Create();
		bool fired = false;
		feature.OnThemeChanged += () => fired = true;

		feature.Update(true, "#FF0000", "#CC0000");

		fired.ShouldBeTrue();
	}

	[Fact]
	public void Update_FiresEvent_ExactlyOnce_PerCall()
	{
		ThemeStateFeature feature = Create();
		int count = 0;
		feature.OnThemeChanged += () => count++;

		feature.Update(true, "#FF0000", "#CC0000");

		count.ShouldBe(1);
	}

	[Fact]
	public void Update_CalledTwice_FiresEventTwice_AndReflectsLatestState()
	{
		ThemeStateFeature feature = Create();
		int count = 0;
		feature.OnThemeChanged += () => count++;

		feature.Update(true, "#FF0000", "#CC0000");
		feature.Update(false, "#0000FF", "#0000CC");

		count.ShouldBe(2);
		feature.IsLightTheme.ShouldBeFalse();
		feature.AccentColor.ShouldBe("#0000FF");
	}

	[Fact]
	public void Update_NoSubscribers_DoesNotThrow()
	{
		ThemeStateFeature feature = Create();
		Should.NotThrow(() => feature.Update(true, "#FF0000", "#CC0000"));
	}
}
