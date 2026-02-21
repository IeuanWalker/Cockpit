using Cockpit.Features.Connection;
using Microsoft.AspNetCore.Components;

namespace Cockpit.Components.Pages.ContextPanel.ConnectionStatus;

public partial class ConnectionStatusHistoryPopup
{
	readonly ConnectionFeature _connectionFeature;
	public ConnectionStatusHistoryPopup(ConnectionFeature connectionFeature)
	{
		_connectionFeature = connectionFeature;
	}
	[Parameter] public bool Show { get; set; }
	[Parameter] public EventCallback OnClose { get; set; }

	string _statusClass => _connectionFeature.Status switch
	{
		ConnectionStatusEnum.Connected => "connected",
		ConnectionStatusEnum.Disconnected => "disconnected",
		ConnectionStatusEnum.Checking => "checking",
		_ => "checking"
	};
}