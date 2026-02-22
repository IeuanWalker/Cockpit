using Cockpit.Components.Controls;
using Cockpit.Features.Connection;

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
}