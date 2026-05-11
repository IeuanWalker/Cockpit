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

	// Null acts as "not yet rendered" sentinel; empty string is a valid branch value.
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
		// Seed the sentinel so the first StateHasChanged after initial render doesn't
		// unconditionally re-render when nothing has actually changed.
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