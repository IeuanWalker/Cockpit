using Cockpit.Features.Mcp;
using Cockpit.Features.Sessions;
using GitHub.Copilot;
using Microsoft.Extensions.Logging.Abstractions;
using Shouldly;

namespace Cockpit.UnitTests.Features.Mcp;

public sealed class McpFeatureTests
{
	// ── GetStatusDisplayString ────────────────────────────────────────────────

	public static TheoryData<McpServerStatus, string> DisplayStringCases => new()
	{
		{ McpServerStatus.Connected, "Connected" },
		{ McpServerStatus.Failed, "Failed" },
		{ McpServerStatus.NeedsAuth, "Needs Auth" },
		{ McpServerStatus.Pending, "Pending" },
		{ McpServerStatus.Disabled, "Disabled" },
		{ McpServerStatus.NotConfigured, "Not Configured" },
	};

	[Theory]
	[MemberData(nameof(DisplayStringCases))]
	public void GetStatusDisplayString_KnownStatus_ReturnsExpectedLabel(McpServerStatus status, string expected)
	{
		McpFeature.GetStatusDisplayString(status).ShouldBe(expected);
	}

	[Fact]
	public void GetStatusDisplayString_UnknownStatus_FallsBackToToString()
	{
		McpServerStatus unknown = new McpServerStatus("999");

		McpFeature.GetStatusDisplayString(unknown).ShouldBe("999");
	}

	// ── GetStatusColor ────────────────────────────────────────────────────────

	public static TheoryData<McpServerStatus, string> StatusColorCases => new()
	{
		{ McpServerStatus.Connected, "text-green-400" },
		{ McpServerStatus.Failed, "text-red-400" },
		{ McpServerStatus.Disabled, "secondary-text" },
		{ McpServerStatus.NeedsAuth, "text-yellow-400" },
		{ McpServerStatus.Pending, "text-yellow-400" },
		{ McpServerStatus.NotConfigured, "text-yellow-400" },
	};

	[Theory]
	[MemberData(nameof(StatusColorCases))]
	public void GetStatusColor_KnownStatus_ReturnsExpectedCssClass(McpServerStatus status, string expectedClass)
	{
		McpFeature.GetStatusColor(status).ShouldBe(expectedClass);
	}

	[Fact]
	public void GetStatusColor_UnknownStatus_FallsBackToYellow()
	{
		McpServerStatus unknown = new McpServerStatus("999");

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
