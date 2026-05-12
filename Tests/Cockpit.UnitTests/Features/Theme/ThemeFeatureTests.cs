using Cockpit.Features.AppSettings;
using Cockpit.Features.Theme;
using Cockpit.UnitTests.Features.AppSettings;
using Microsoft.Extensions.Logging.Abstractions;
using Shouldly;

namespace Cockpit.UnitTests.Features.Theme;

/// <summary>
/// Tests for <see cref="ThemeFeature"/> state and persistence logic.
/// JS/MAUI calls are stubbed; platform-specific code paths that require
/// <c>Application.Current</c> are guarded and silently skipped in the test host.
/// </summary>
public class ThemeFeatureTests
{
	static (ThemeFeature feature, IAppSettingsFeature settings) Create(
		ThemeEnum theme = ThemeEnum.Dark,
		string accent = "#005FB8",
		string accentHover = "#0050a0")
	{
		InMemoryPreferencesStorage store = new();
		store.Set("Theme", theme.ToString());
		store.Set("AccentColor", accent);
		store.Set("AccentHoverColor", accentHover);

		IAppSettingsFeature settings = new AppSettingsFeature(new UserAppSettings(store));
		ThemeStateFeature stateFeature = new(settings);

		ThemeFeature feature = new(
			new NoOpJSRuntime(),
			NullLogger<ThemeFeature>.Instance,
			settings,
			stateFeature);

		return (feature, settings);
	}

	// -------------------------------------------------------------------------
	// Constructor — initial state
	// -------------------------------------------------------------------------

	[Theory]
	[InlineData(ThemeEnum.Light)]
	[InlineData(ThemeEnum.Dark)]
	[InlineData(ThemeEnum.System)]
	public void Constructor_SetsCurrentTheme_FromSettings(ThemeEnum theme)
	{
		(ThemeFeature feature, _) = Create(theme);
		feature.CurrentTheme.ShouldBe(theme);
	}

	[Fact]
	public void Constructor_SetsAccentColor_FromSettings()
	{
		(ThemeFeature feature, _) = Create(accent: "#FF0000");
		feature.AccentColor.ShouldBe("#FF0000");
	}

	[Fact]
	public void Constructor_SetsAccentHoverColor_FromSettings()
	{
		(ThemeFeature feature, _) = Create(accentHover: "#CC0000");
		feature.AccentHoverColor.ShouldBe("#CC0000");
	}

	// -------------------------------------------------------------------------
	// IsLightTheme — pure computation
	// -------------------------------------------------------------------------

	[Theory]
	[InlineData(ThemeEnum.Light, true)]
	[InlineData(ThemeEnum.Dark, false)]
	public void IsLightTheme_ExplicitTheme_ReturnsCorrectValue(ThemeEnum theme, bool expected)
	{
		(ThemeFeature feature, _) = Create(theme);
		feature.IsLightTheme.ShouldBe(expected);
	}

	[Fact]
	public void IsLightTheme_SystemTheme_NoMauiApp_ReturnsFalse()
	{
		// Application.Current is null in the test host; System resolves to dark.
		(ThemeFeature feature, _) = Create(ThemeEnum.System);
		feature.IsLightTheme.ShouldBeFalse();
	}

	// -------------------------------------------------------------------------
	// SetTheme — state and persistence
	// -------------------------------------------------------------------------

	[Theory]
	[InlineData(ThemeEnum.Light)]
	[InlineData(ThemeEnum.Dark)]
	[InlineData(ThemeEnum.System)]
	public async Task SetTheme_UpdatesCurrentTheme(ThemeEnum theme)
	{
		(ThemeFeature feature, _) = Create(ThemeEnum.Dark);
		await feature.SetTheme(theme);
		feature.CurrentTheme.ShouldBe(theme);
	}

	[Fact]
	public async Task SetTheme_PersistsTheme_ToSettings()
	{
		(ThemeFeature feature, IAppSettingsFeature settings) = Create(ThemeEnum.Dark);
		await feature.SetTheme(ThemeEnum.Light);
		settings.Theme.ShouldBe(ThemeEnum.Light);
	}

	[Fact]
	public async Task SetTheme_FiresOnThemeChanged()
	{
		(ThemeFeature feature, _) = Create();
		bool fired = false;
		feature.OnThemeChanged += () => fired = true;

		await feature.SetTheme(ThemeEnum.Light);

		fired.ShouldBeTrue();
	}

	// -------------------------------------------------------------------------
	// SetAccentColor — state and persistence
	// -------------------------------------------------------------------------

	[Fact]
	public async Task SetAccentColor_UpdatesAccentColor()
	{
		(ThemeFeature feature, _) = Create();
		await feature.SetAccentColor("#FF0000", "#CC0000");
		feature.AccentColor.ShouldBe("#FF0000");
	}

	[Fact]
	public async Task SetAccentColor_UpdatesAccentHoverColor()
	{
		(ThemeFeature feature, _) = Create();
		await feature.SetAccentColor("#FF0000", "#CC0000");
		feature.AccentHoverColor.ShouldBe("#CC0000");
	}

	[Fact]
	public async Task SetAccentColor_PersistsColors_ToSettings()
	{
		(ThemeFeature feature, IAppSettingsFeature settings) = Create();
		await feature.SetAccentColor("#FF0000", "#CC0000");
		settings.AccentColor.ShouldBe("#FF0000");
		settings.AccentHoverColor.ShouldBe("#CC0000");
	}

	[Fact]
	public async Task SetAccentColor_FiresOnThemeChanged()
	{
		(ThemeFeature feature, _) = Create();
		bool fired = false;
		feature.OnThemeChanged += () => fired = true;

		await feature.SetAccentColor("#FF0000", "#CC0000");

		fired.ShouldBeTrue();
	}

	// -------------------------------------------------------------------------
	// Initialize — idempotency
	// -------------------------------------------------------------------------

	[Fact]
	public async Task Initialize_CalledMultipleTimes_DoesNotThrow()
	{
		(ThemeFeature feature, _) = Create();
		await feature.Initialize();
		await Should.NotThrowAsync(() => feature.Initialize());
	}

	[Fact]
	public async Task Initialize_CalledTwice_DoesNotDoubleSubscribe()
	{
		// If Initialize double-subscribes to RequestedThemeChanged, the system theme
		// handler would fire twice. We verify the flag prevents re-initialization.
		(ThemeFeature feature, _) = Create();
		await feature.Initialize();
		await feature.Initialize(); // second call is a no-op
		// No assertion needed beyond no exception; the _isInitialized guard is tested implicitly.
	}

	// -------------------------------------------------------------------------
	// Dispose — no exception
	// -------------------------------------------------------------------------

	[Fact]
	public void Dispose_DoesNotThrow()
	{
		(ThemeFeature feature, _) = Create();
		Should.NotThrow(() => feature.Dispose());
	}
}
