using Cockpit.Features.Connection;
using Microsoft.AspNetCore.Components;

namespace Cockpit.Components.Pages.ContextPanel;

public partial class ConnectionStatus : ComponentBase, IDisposable
{
	[Inject] ConnectionFeature _feature { get; set; } = default!;

	bool _showPopup = false;

	string _statusClass => _feature.Status switch
	{
		ConnectionHealthStatus.Connected => "connected",
		ConnectionHealthStatus.Disconnected => "disconnected",
		ConnectionHealthStatus.Checking => "checking",
		_ => "checking"
	};

	string _statusText => _feature.Status switch
	{
		ConnectionHealthStatus.Connected => "Connected",
		ConnectionHealthStatus.Disconnected => "Disconnected",
		ConnectionHealthStatus.Checking => "Checking...",
		_ => "Unknown"
	};

	protected override void OnInitialized()
	{
		_feature.OnStatusChanged += OnStatusChanged;
	}

	protected override async Task OnInitializedAsync()
	{
		await _feature.Initialize();
	}

	void OnStatusChanged() => InvokeAsync(StateHasChanged);

	void OpenPopup() => _showPopup = true;
	void ClosePopup() => _showPopup = false;

	public void Dispose()
	{
		Dispose(true);
		GC.SuppressFinalize(this);
	}

	protected virtual void Dispose(bool disposing)
	{
		if(disposing)
		{
			_feature.OnStatusChanged -= OnStatusChanged;
		}
	}
}
