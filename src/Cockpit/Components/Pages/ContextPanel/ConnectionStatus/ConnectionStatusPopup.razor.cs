using Cockpit.Components.Controls;
using Cockpit.Features.Connection;

namespace Cockpit.Components.Pages.ContextPanel.ConnectionStatus;

public partial class ConnectionStatusPopup
{
	readonly ConnectionFeature _connectionFeature;
	public ConnectionStatusPopup(ConnectionFeature connectionFeature)
	{
		_connectionFeature = connectionFeature;
	}

	PopupBase _popup = default!;
	ConnectionStatusHistoryPopup _historyPopup = default!;

	public void Open() => _popup.Open();

	void OpenHistoryPopup()
	{
		_popup.Close();
		_historyPopup.Open();
	}
}