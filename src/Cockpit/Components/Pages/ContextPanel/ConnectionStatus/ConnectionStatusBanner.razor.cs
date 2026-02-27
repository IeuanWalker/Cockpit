using Cockpit.Features.Connection;
using Microsoft.AspNetCore.Components;

namespace Cockpit.Components.Pages.ContextPanel.ConnectionStatus;

public partial class ConnectionStatusBanner : ComponentBase, IDisposable
{
	readonly ConnectionFeature _connectionFeature;
	public ConnectionStatusBanner(ConnectionFeature connectionFeature)
	{
		_connectionFeature = connectionFeature;
	}

	ConnectionStatusPopup _popup = default!;

	protected override void OnInitialized()
	{
		_connectionFeature.OnStatusChanged += OnStatusChanged;
	}

	protected override async Task OnInitializedAsync()
	{
		await _connectionFeature.Initialize();
	}

	void OnStatusChanged() => InvokeAsync(StateHasChanged);

	void OpenPopup() => _popup.Open();


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
