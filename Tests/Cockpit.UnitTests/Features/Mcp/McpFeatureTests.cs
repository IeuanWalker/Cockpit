using Cockpit.Features.Mcp;
using Cockpit.Features.Sessions;
using GitHub.Copilot.SDK.Rpc;
using Microsoft.Extensions.Logging.Abstractions;
using Shouldly;

namespace Cockpit.UnitTests.Features.Mcp;

public sealed class McpFeatureTests
{
	// ── GetStatusDisplayString ────────────────────────────────────────────────

	[Theory]
	[InlineData(McpServerStatus.Connected, "Connected")]
	[InlineData(McpServerStatus.Failed, "Failed")]
	[InlineData(McpServerStatus.NeedsAuth, "Needs Auth")]
	[InlineData(McpServerStatus.Pending, "Pending")]
	[InlineData(McpServerStatus.Disabled, "Disabled")]
	[InlineData(McpServerStatus.NotConfigured, "Not Configured")]
	public void GetStatusDisplayString_KnownStatus_ReturnsExpectedLabel(McpServerStatus status, string expected)
	{
		McpFeature.GetStatusDisplayString(status).ShouldBe(expected);
	}

	[Fact]
	public void GetStatusDisplayString_UnknownStatus_FallsBackToToString()
	{
		McpServerStatus unknown = (McpServerStatus)999;

		McpFeature.GetStatusDisplayString(unknown).ShouldBe("999");
	}

	// ── GetStatusColor ────────────────────────────────────────────────────────

	[Theory]
	[InlineData(McpServerStatus.Connected, "text-green-400")]
	[InlineData(McpServerStatus.Failed, "text-red-400")]
	[InlineData(McpServerStatus.Disabled, "secondary-text")]
	[InlineData(McpServerStatus.NeedsAuth, "text-yellow-400")]
	[InlineData(McpServerStatus.Pending, "text-yellow-400")]
	[InlineData(McpServerStatus.NotConfigured, "text-yellow-400")]
	public void GetStatusColor_KnownStatus_ReturnsExpectedCssClass(McpServerStatus status, string expectedClass)
	{
		McpFeature.GetStatusColor(status).ShouldBe(expectedClass);
	}

	[Fact]
	public void GetStatusColor_UnknownStatus_FallsBackToYellow()
	{
		McpServerStatus unknown = (McpServerStatus)999;

		McpFeature.GetStatusColor(unknown).ShouldBe("text-yellow-400");
	}

	// ── Session-not-found guard ───────────────────────────────────────────────

	static McpFeature CreateFeature() => new(
		NullLogger<McpFeature>.Instance,
		new SdkSessionRegistry(),
		new SessionListFeature(NullLogger<SessionListFeature>.Instance));

	[Fact]
	public async Task EnableServerAsync_SessionNotInRegistry_CompletesWithoutThrowing()
	{
		McpFeature feature = CreateFeature();

		await Should.NotThrowAsync(() => feature.EnableServerAsync("missing-session", "my-server"));
	}

	[Fact]
	public async Task DisableServerAsync_SessionNotInRegistry_CompletesWithoutThrowing()
	{
		McpFeature feature = CreateFeature();

		await Should.NotThrowAsync(() => feature.DisableServerAsync("missing-session", "my-server"));
	}

	[Fact]
	public async Task ReloadAsync_SessionNotInRegistry_CompletesWithoutThrowing()
	{
		McpFeature feature = CreateFeature();

		await Should.NotThrowAsync(() => feature.ReloadAsync("missing-session"));
	}
}
