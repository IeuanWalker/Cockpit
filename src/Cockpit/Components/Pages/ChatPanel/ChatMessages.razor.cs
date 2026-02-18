using Cockpit.Services;
using Microsoft.AspNetCore.Components;

namespace Cockpit.Components.Pages.ChatPanel;

public partial class ChatMessages : ComponentBase, IDisposable
{
	[Inject] UnifiedSessionManager _sessionManager { get; set; } = default!;

	protected override void OnInitialized()
	{
		_sessionManager.OnStateChanged += OnStateChanged;
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
			_sessionManager.OnStateChanged -= OnStateChanged;
		}
	}
}
