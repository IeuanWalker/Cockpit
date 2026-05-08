using Cockpit.Features.Mcp;
using GitHub.Copilot.SDK.Rpc;
using Shouldly;

namespace Cockpit.UnitTests.Features.Mcp;

public sealed class McpFeatureTests
{
	[Fact]
	public void GetStatusDisplayString_Connected_ReturnsConnected()
	{
		string result = McpFeature.GetStatusDisplayString(McpServerStatus.Connected);

		result.ShouldBe("Connected");
	}

	[Fact]
	public void GetStatusDisplayString_Failed_ReturnsFailed()
	{
		string result = McpFeature.GetStatusDisplayString(McpServerStatus.Failed);

		result.ShouldBe("Failed");
	}

	[Fact]
	public void GetStatusDisplayString_NeedsAuth_ReturnsNeedsAuth()
	{
		string result = McpFeature.GetStatusDisplayString(McpServerStatus.NeedsAuth);

		result.ShouldBe("Needs Auth");
	}

	[Fact]
	public void GetStatusDisplayString_Pending_ReturnsPending()
	{
		string result = McpFeature.GetStatusDisplayString(McpServerStatus.Pending);

		result.ShouldBe("Pending");
	}

	[Fact]
	public void GetStatusDisplayString_Disabled_ReturnsDisabled()
	{
		string result = McpFeature.GetStatusDisplayString(McpServerStatus.Disabled);

		result.ShouldBe("Disabled");
	}

	[Fact]
	public void GetStatusDisplayString_NotConfigured_ReturnsNotConfigured()
	{
		string result = McpFeature.GetStatusDisplayString(McpServerStatus.NotConfigured);

		result.ShouldBe("Not Configured");
	}
}
