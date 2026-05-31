using Cockpit.Features.Mcp;
using Cockpit.Features.Sessions;
using GitHub.Copilot;
using Microsoft.Extensions.Logging.Abstractions;
using Shouldly;

namespace Cockpit.UnitTests.Features.Mcp;

public sealed class McpFeatureTests
{
	// ── GetStatusDisplayString ────────────────────────────────────────────────

	public static TheoryData<string, string> DisplayStringCases => new()
	{
		{ McpServerStatus.Connected.Value, "Connected" },
		{ McpServerStatus.Failed.Value, "Failed" },
		{ McpServerStatus.NeedsAuth.Value, "Needs Auth" },
		{ McpServerStatus.Pending.Value, "Pending" },
		{ McpServerStatus.Disabled.Value, "Disabled" },
		{ McpServerStatus.NotConfigured.Value, "Not Configured" },
	};

	[Theory]
	[MemberData(nameof(DisplayStringCases))]
	public void GetStatusDisplayString_KnownStatus_ReturnsExpectedLabel(string statusValue, string expected)
	{
		McpFeature.GetStatusDisplayString(new McpServerStatus(statusValue)).ShouldBe(expected);
	}

	[Fact]
	public void GetStatusDisplayString_UnknownStatus_FallsBackToToString()
	{
		McpServerStatus unknown = new("999");

		McpFeature.GetStatusDisplayString(unknown).ShouldBe("999");
	}

	// ── GetStatusColor ────────────────────────────────────────────────────────

	public static TheoryData<string, string> StatusColorCases => new()
	{
		{ McpServerStatus.Connected.Value, "text-green-400" },
		{ McpServerStatus.Failed.Value, "text-red-400" },
		{ McpServerStatus.Disabled.Value, "secondary-text" },
		{ McpServerStatus.NeedsAuth.Value, "text-yellow-400" },
		{ McpServerStatus.Pending.Value, "text-yellow-400" },
		{ McpServerStatus.NotConfigured.Value, "text-yellow-400" },
	};

	[Theory]
	[MemberData(nameof(StatusColorCases))]
	public void GetStatusColor_KnownStatus_ReturnsExpectedCssClass(string statusValue, string expectedClass)
	{
		McpFeature.GetStatusColor(new McpServerStatus(statusValue)).ShouldBe(expectedClass);
	}

	[Fact]
	public void GetStatusColor_UnknownStatus_FallsBackToYellow()
	{
		McpServerStatus unknown = new("999");

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
