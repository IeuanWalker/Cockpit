using Cockpit.Features.Sessions;
using Microsoft.AspNetCore.Components;

namespace Cockpit.Components;

public sealed partial class Main : ComponentBase, IDisposable
{
	readonly SessionListFeature _sessionListFeature;

	public Main(SessionListFeature sessionListFeature)
	{
		_sessionListFeature = sessionListFeature;
		_sessionListFeature.OnStateChanged += OnStateChanged;
	}

	void OnStateChanged()
	{
		InvokeAsync(StateHasChanged);
	}

	public void Dispose()
	{
		_sessionListFeature.OnStateChanged -= OnStateChanged;
		GC.SuppressFinalize(this);
	}
}
