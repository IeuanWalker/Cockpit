using Cockpit.Features.Plugins;
using Shouldly;

namespace Cockpit.UnitTests.Features.Plugins;

public sealed class PluginsFeatureTests
{
	[Theory]
	[InlineData(null, "unknown")]
	[InlineData("", "unknown")]
	[InlineData("   ", "unknown")]
	[InlineData("1.2.3", "1.2.3")]
	[InlineData("  1.2.3  ", "  1.2.3  ")]
	public void FormatVersion_ReturnsExpectedResult(string? input, string expected)
	{
		string result = PluginsFeature.FormatVersion(input);

		result.ShouldBe(expected);
	}
}
