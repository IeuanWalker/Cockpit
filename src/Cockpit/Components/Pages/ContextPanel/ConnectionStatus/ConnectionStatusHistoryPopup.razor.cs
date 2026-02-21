using Cockpit.Features.Connection;
using Cockpit.Components.Controls;
using Microsoft.AspNetCore.Components;

namespace Cockpit.Components.Pages.ContextPanel.ConnectionStatus;

public partial class ConnectionStatusHistoryPopup
{
	readonly ConnectionFeature _connectionFeature;
	public ConnectionStatusHistoryPopup(ConnectionFeature connectionFeature)
	{
		_connectionFeature = connectionFeature;
	}

	PopupBase _popup = default!;

	public void Open() => _popup.Open();

	string _statusClass => _connectionFeature.Status switch
	{
		ConnectionStatusEnum.Connected => "connected",
		ConnectionStatusEnum.Disconnected => "disconnected",
		ConnectionStatusEnum.Checking => "checking",
		_ => "checking"
	};
}