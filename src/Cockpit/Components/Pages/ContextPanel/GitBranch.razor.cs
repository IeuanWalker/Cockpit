using Cockpit.Features.Sessions;
using Microsoft.AspNetCore.Components;

namespace Cockpit.Components.Pages.ContextPanel;

public partial class GitBranch : ComponentBase, IDisposable
{
	readonly SessionListFeature _sessionListFeature;
	public GitBranch(SessionListFeature sessionListFeature)
	{
		_sessionListFeature = sessionListFeature;
	}

	string CurrentBranch => _sessionListFeature.CurrentSession?.Context?.Branch ?? string.Empty;

	string _renderedBranch = string.Empty;
	bool _hasRendered = false;

	protected override bool ShouldRender()
	{
		string current = CurrentBranch;
		if(_hasRendered && current == _renderedBranch)
		{
			return false;
		}

		_hasRendered = true;
		_renderedBranch = current;
		return true;
	}

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