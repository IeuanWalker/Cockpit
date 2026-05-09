using Cockpit.Features.Plugins;
using Shouldly;

namespace Cockpit.UnitTests.Features.Plugins;

public sealed class PluginsFeatureTests
{
	[Fact]
	public void FormatVersion_Null_ReturnsUnknown()
	{
		string result = PluginsFeature.FormatVersion(null);

		result.ShouldBe("unknown");
	}

	[Fact]
	public void FormatVersion_Empty_ReturnsUnknown()
	{
		string result = PluginsFeature.FormatVersion(string.Empty);

		result.ShouldBe("unknown");
	}

	[Fact]
	public void FormatVersion_Whitespace_ReturnsUnknown()
	{
		string result = PluginsFeature.FormatVersion("   ");

		result.ShouldBe("unknown");
	}

	[Fact]
	public void FormatVersion_ValidVersion_ReturnsVersion()
	{
		string result = PluginsFeature.FormatVersion("1.2.3");

		result.ShouldBe("1.2.3");
	}

	[Fact]
	public void FormatVersion_VersionWithSpaces_ReturnsVersionAsIs()
	{
		// FormatVersion delegates to string.IsNullOrWhiteSpace — it does not trim,
		// so a version surrounded by spaces is returned verbatim.
		string result = PluginsFeature.FormatVersion("  1.2.3  ");

		result.ShouldBe("  1.2.3  ");
	}
}
