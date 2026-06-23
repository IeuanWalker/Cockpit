using Cockpit.Features.AppSettings;
using Cockpit.UnitTests.Features.AppSettings;
using Shouldly;

namespace Cockpit.UnitTests.Features.Canvas;

/// <summary>
/// Tests for the <c>CanvasEnabled</c> setting in <see cref="UserAppSettings"/>.
/// </summary>
public class CanvasSettingsTests
{
	static UserAppSettings CreateSettings(InMemoryPreferencesStorage? store = null)
		=> new(store ?? new InMemoryPreferencesStorage());

	[Fact]
	public void CanvasEnabled_DefaultsTo_True()
	{
		UserAppSettings settings = CreateSettings();
		settings.CanvasEnabled.ShouldBeTrue();
	}

	[Theory]
	[InlineData(true)]
	[InlineData(false)]
	public void CanvasEnabled_RoundTrip_PreservesValue(bool value)
	{
		UserAppSettings settings = CreateSettings();
		settings.CanvasEnabled = value;
		settings.CanvasEnabled.ShouldBe(value);
	}

	[Fact]
	public void CanvasEnabled_Persists_AcrossInstances()
	{
		InMemoryPreferencesStorage store = new();
		UserAppSettings first = CreateSettings(store);
		first.CanvasEnabled = false;

		UserAppSettings second = CreateSettings(store);
		second.CanvasEnabled.ShouldBeFalse();
	}

	[Fact]
	public void CanvasEnabled_ExplicitTrue_StoresAndReturnsTrue()
	{
		UserAppSettings settings = CreateSettings();
		settings.CanvasEnabled = true;
		settings.CanvasEnabled.ShouldBeTrue();
	}
}
