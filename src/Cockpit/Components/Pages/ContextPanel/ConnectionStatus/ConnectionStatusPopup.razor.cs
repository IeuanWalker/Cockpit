using Cockpit.Features.Connection;
using Microsoft.AspNetCore.Components;

namespace Cockpit.Components.Pages.ContextPanel.ConnectionStatus;

public partial class ConnectionStatusPopup
{
	readonly ConnectionFeature _connectionFeature;
	public ConnectionStatusPopup(ConnectionFeature connectionFeature)
	{
		_connectionFeature = connectionFeature;
	}
	[Parameter] public bool Show { get; set; }
	[Parameter] public EventCallback OnClose { get; set; }

	bool _showHistory;

	string _statusClass => _connectionFeature.Status switch
	{
		ConnectionStatusEnum.Connected => "connected",
		ConnectionStatusEnum.Disconnected => "disconnected",
		ConnectionStatusEnum.Checking => "checking",
		_ => "checking"
	};

	async Task OpenHistoryPopup()
	{
		_showHistory = true;
		await OnClose.InvokeAsync();
	}

	void CloseHistoryPopup() => _showHistory = false;
}