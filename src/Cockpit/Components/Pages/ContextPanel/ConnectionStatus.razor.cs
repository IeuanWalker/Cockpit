using Cockpit.Features.Connection;
using Microsoft.AspNetCore.Components;

namespace Cockpit.Components.Pages.ContextPanel;

public partial class ConnectionStatus : ComponentBase, IDisposable
{
	[Inject] ConnectionFeature _connectionFeature { get; set; } = default!;

	bool _showPopup = false;
	bool _showHistoryPopup = false;
	readonly HashSet<int> _expandedHistoryIndices = [];

	string _statusClass => _connectionFeature.Status switch
	{
		ConnectionStatusEnum.Connected => "connected",
		ConnectionStatusEnum.Disconnected => "disconnected",
		ConnectionStatusEnum.Checking => "checking",
		_ => "checking"
	};

	string _statusText => _connectionFeature.Status switch
	{
		ConnectionStatusEnum.Connected => "Connected",
		ConnectionStatusEnum.Disconnected => "Disconnected",
		ConnectionStatusEnum.Checking => "Checking...",
		_ => "Unknown"
	};

	protected override void OnInitialized()
	{
		_connectionFeature.OnStatusChanged += OnStatusChanged;
	}

	protected override async Task OnInitializedAsync()
	{
		await _connectionFeature.Initialize();
	}

	void OnStatusChanged() => InvokeAsync(StateHasChanged);

	void OpenPopup() => _showPopup = true;
	void ClosePopup() => _showPopup = false;

	void OpenHistoryPopup()
	{
		_showPopup = false;
		_expandedHistoryIndices.Clear();
		_showHistoryPopup = true;
	}

	public void Dispose()
	{
		Dispose(true);
		GC.SuppressFinalize(this);
	}

	protected virtual void Dispose(bool disposing)
	{
		if(disposing)
		{
			_connectionFeature.OnStatusChanged -= OnStatusChanged;
		}
	}
}
