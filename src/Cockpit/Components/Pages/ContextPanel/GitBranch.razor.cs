using Cockpit.Features.Sessions;
using Microsoft.AspNetCore.Components;

namespace Cockpit.Components.Pages.ContextPanel;

public sealed partial class GitBranch : ComponentBase, IDisposable
{
	readonly SessionListFeature _sessionListFeature;

	public GitBranch(SessionListFeature sessionListFeature)
	{
		_sessionListFeature = sessionListFeature;
	}

	string CurrentBranch => _sessionListFeature.CurrentSession?.Context?.Branch ?? string.Empty;

	string? _renderedBranch;

	protected override bool ShouldRender()
	{
		string current = CurrentBranch;
		if(current == _renderedBranch)
		{
			return false;
		}

		_renderedBranch = current;
		return true;
	}

	protected override void OnInitialized()
	{
		_sessionListFeature.OnStateChanged += OnStateChanged;
		_renderedBranch = CurrentBranch;
	}

	void OnStateChanged()
	{
		InvokeAsync(StateHasChanged);
	}

	public void Dispose()
	{
		_sessionListFeature.OnStateChanged -= OnStateChanged;
	}
}