using Cockpit.Features.Permissions;
using Cockpit.Services;
using Microsoft.AspNetCore.Components;

namespace Cockpit.Components;

public partial class Main : ComponentBase, IDisposable
{
	[Inject] UnifiedSessionManager SessionManager { get; set; } = default!;
	[Inject] PermissionFeature PermissionFeature { get; set; } = default!;

	protected override async Task OnInitializedAsync()
	{
		_ = PermissionFeature;
		SessionManager.OnStateChanged += OnStateChanged;
	}

	void OnStateChanged()
	{
		InvokeAsync(StateHasChanged);
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
			SessionManager.OnStateChanged -= OnStateChanged;
		}
	}
}
