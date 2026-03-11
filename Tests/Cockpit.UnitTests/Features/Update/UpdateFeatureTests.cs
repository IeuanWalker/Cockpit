using Cockpit.Features.Updates;

namespace Cockpit.UnitTests.Features.Update;

public class UpdateFeatureTests
{
	[Theory]
	[InlineData("v1.2.3", "v1.2.2", true)]
	[InlineData("v1.2.3", "v1.2.3", false)]
	[InlineData("v1.2.3", "v1.2.4", false)]
	[InlineData("v2.0.0", "v1.9.9", true)]
	[InlineData("v1.2.3", "v1.2.3-beta", true)]
	[InlineData("v1.2.3", "v1.2", true)]
	[InlineData("v1.2", "v1.2.3", false)]
	[InlineData("v1.2.3", "v1.2.3+build", false)]
	[InlineData("v1.2.3+build", "v1.2.3", false)]
	public void IsNewerVersion_CorrectlyComparesVersions(string remote, string current, bool expected)
	{
		bool result = UpdateFeature.IsNewerVersion(remote, current);
		Assert.Equal(expected, result);
	}

	[Fact]
	public void IsNewerVersion_HandlesInvalidInputGracefully()
	{
		bool result = UpdateFeature.IsNewerVersion("not-a-version", "also-not-a-version");
		Assert.False(result);
	}
}
