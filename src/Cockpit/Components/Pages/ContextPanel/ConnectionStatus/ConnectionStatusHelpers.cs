using Cockpit.Features.Connection;

namespace Cockpit.Components.Pages.ContextPanel.ConnectionStatus;

public static class ConnectionStatusHelpers
{
	public static string GetStatusClass(ConnectionStatusEnum status) => status switch
	{
		ConnectionStatusEnum.Connected => "connected",
		ConnectionStatusEnum.Disconnected => "disconnected",
		ConnectionStatusEnum.Error => "disconnected",
		ConnectionStatusEnum.Checking => "checking",
		_ => "checking"
	};

	public static string GetStatusText(ConnectionStatusEnum status) => status switch
	{
		ConnectionStatusEnum.Connected => "Connected",
		ConnectionStatusEnum.Disconnected => "Disconnected",
		ConnectionStatusEnum.Checking => "Checking...",
		ConnectionStatusEnum.Error => "Error",
		_ => "Unknown"
	};
}
