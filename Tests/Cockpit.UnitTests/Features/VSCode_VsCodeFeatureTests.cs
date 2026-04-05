using Cockpit.Features.VSCode;
using Microsoft.Extensions.Logging.Abstractions;
using Shouldly;

namespace Cockpit.UnitTests.Features;

public class VsCodeFeatureTests
{
	static VsCodeFeature CreateFeature() => new(NullLogger<VsCodeFeature>.Instance);

	static VsCodeFeature CreateFeatureWithAvailability(bool isAvailable)
	{
		string? executablePath = isAvailable ? "code" : null;
		return new VsCodeFeature(NullLogger<VsCodeFeature>.Instance, executablePath);
	}

	[Fact]
	public void Constructor_DoesNotThrow()
	{
		Should.NotThrow(CreateFeature);
	}

	[Fact]
	public void IsAvailable_IsAccessible_AfterConstruction()
	{
		VsCodeFeature feature = CreateFeature();
		bool _ = feature.IsAvailable; // should not throw
	}

	[Fact]
	public void OpenPathInVsCode_ReturnsFalse_WhenNotAvailable()
	{
		VsCodeFeature feature = CreateFeatureWithAvailability(false);

		bool result = feature.OpenPathInVsCode(@"C:\some\path");

		result.ShouldBeFalse();
	}

	[Theory]
	[InlineData(@"C:\some\path")]
	[InlineData(@"/home/user/project")]
	[InlineData("")]
	[InlineData("   ")]
	public void OpenPathInVsCode_ReturnsFalse_WhenNotAvailable_ForAnyPath(string path)
	{
		VsCodeFeature feature = CreateFeatureWithAvailability(false);

		bool result = feature.OpenPathInVsCode(path);

		result.ShouldBeFalse();
	}

	[Fact]
	public void OpenPathInVsCode_DoesNotThrow_WhenNotAvailable()
	{
		VsCodeFeature feature = CreateFeatureWithAvailability(false);

		Should.NotThrow(() => feature.OpenPathInVsCode(@"C:\some\path"));
	}
}
