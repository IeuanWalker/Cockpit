using Cockpit.Components.Pages.ContextPanel.ConnectionStatus;
using Cockpit.Features.Connection;
using Shouldly;

namespace Cockpit.UnitTests.Features.Connection;

public class ConnectionStatusHelpersTests
{
	// ── GetStatusClass ────────────────────────────────────────────────────────

	[Theory]
	[InlineData(ConnectionStatusEnum.Connected, "connected")]
	[InlineData(ConnectionStatusEnum.Disconnected, "disconnected")]
	[InlineData(ConnectionStatusEnum.Error, "disconnected")]
	[InlineData(ConnectionStatusEnum.Checking, "checking")]
	[InlineData(ConnectionStatusEnum.Unknown, "checking")]
	public void GetStatusClass_ReturnsExpectedCssClass(ConnectionStatusEnum status, string expectedClass)
	{
		ConnectionStatusHelpers.GetStatusClass(status).ShouldBe(expectedClass);
	}

	// ── GetStatusText ─────────────────────────────────────────────────────────

	[Theory]
	[InlineData(ConnectionStatusEnum.Connected, "Connected")]
	[InlineData(ConnectionStatusEnum.Disconnected, "Disconnected")]
	[InlineData(ConnectionStatusEnum.Checking, "Checking...")]
	[InlineData(ConnectionStatusEnum.Error, "Error")]
	[InlineData(ConnectionStatusEnum.Unknown, "Unknown")]
	public void GetStatusText_ReturnsExpectedLabel(ConnectionStatusEnum status, string expectedText)
	{
		ConnectionStatusHelpers.GetStatusText(status).ShouldBe(expectedText);
	}
}
