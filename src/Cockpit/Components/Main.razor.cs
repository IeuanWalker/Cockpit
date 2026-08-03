using Cockpit.Features.Sessions;
using Microsoft.AspNetCore.Components;

namespace Cockpit.Components;

public sealed partial class Main : ComponentBase, IDisposable
{
	readonly SessionListFeature _sessionListFeature;

	public Main(SessionListFeature sessionListFeature)
	{
		_sessionListFeature = sessionListFeature;
		_previousSessionId = sessionListFeature.CurrentSession?.Id;
		_sessionListFeature.OnSessionStateChanged += OnSessionStateChanged;
	}

	string? _previousSessionId;

	void OnSessionStateChanged(SessionStateChange change)
	{
		string? currentSessionId = _sessionListFeature.CurrentSession?.Id;
		if(!MainStateChangeFilter.IsCurrentSessionTransition(currentSessionId, _previousSessionId, change))
		{
			return;
		}

		_previousSessionId = currentSessionId;
		_ = InvokeAsync(StateHasChanged);
	}

	public void Dispose()
	{
		_sessionListFeature.OnSessionStateChanged -= OnSessionStateChanged;
		GC.SuppressFinalize(this);
	}
}

internal static class MainStateChangeFilter
{
	public static bool IsCurrentSessionTransition(
		string? currentSessionId,
		string? previousSessionId,
		SessionStateChange change) =>
		(change.Kind & SessionChangeKind.CurrentSession) != 0 && currentSessionId != previousSessionId;
}
