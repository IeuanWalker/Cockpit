using Cockpit.Features.Sessions;
using Cockpit.Features.Sessions.Models;
using Cockpit.Features.UIState;
using Microsoft.AspNetCore.Components;

namespace Cockpit.Components.Pages.ContextPanel;

public partial class EditedFiles : ComponentBase, IDisposable
{
	[Inject] UIStateFeature _uiState { get; set; } = null!;
	[Inject] SessionListFeature _sessionManager { get; set; } = default!;

	List<SessionContextFileModel> Files => _sessionManager.CurrentSession?.Context?.EditedFiles ?? [];

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