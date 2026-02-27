using Cockpit.Features.Sessions;
using Microsoft.AspNetCore.Components;

namespace Cockpit.Components.Pages.ContextPanel;

public partial class Directory : ComponentBase, IDisposable
{
	readonly SessionListFeature _sessionListFeature;

	public Directory(SessionListFeature sessionListFeature)
	{
		_sessionListFeature = sessionListFeature;
	}

	string CurrentDirectory => _sessionListFeature.CurrentSession?.Context?.CurrentWorkingDirectory ?? string.Empty;

	protected override void OnInitialized()
	{
		_sessionListFeature.OnStateChanged += OnStateChanged;
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
			_sessionListFeature.OnStateChanged -= OnStateChanged;
		}
	}
}