using Cockpit.Features.Sessions;
using Cockpit.Features.UIState;
using Microsoft.AspNetCore.Components;

namespace Cockpit.Components.Pages.ChatPanel;

public sealed partial class ChatPanel : ComponentBase, IDisposable
{
	readonly IUIStateFeature _uiStateFeature;
	readonly SessionFeature _sessionFeature;
	public ChatPanel(
		IUIStateFeature uiStateFeature,
		SessionFeature sessionFeature)
	{
		_uiStateFeature = uiStateFeature;
		_sessionFeature = sessionFeature;
	}

	protected override async Task OnInitializedAsync()
	{
		_sessionFeature.OnSessionStateChanged += OnSessionStateChanged;
		_uiStateFeature.OnStateChanged += OnUiStateChanged;

		// Load existing sessions from SDK
		await _sessionFeature.LoadExistingSessions();
	}

	void OnSessionStateChanged(SessionStateChange change)
	{
		string? currentSessionId = _sessionFeature.CurrentSession?.Id;
		if(ChatPanelStateChangeFilter.IsRelevant(currentSessionId, change))
		{
			InvokeAsync(StateHasChanged);
		}
	}

	void OnUiStateChanged() => InvokeAsync(StateHasChanged);

	void ToggleTerminalPanel()
	{
		if(_sessionFeature.CurrentSession is not null)
		{
			_sessionFeature.CurrentSession.Ui.IsTerminalOpen = !_sessionFeature.CurrentSession.Ui.IsTerminalOpen;
			StateHasChanged();
		}
	}

	public void Dispose()
	{
		_sessionFeature.OnSessionStateChanged -= OnSessionStateChanged;
		_uiStateFeature.OnStateChanged -= OnUiStateChanged;
		GC.SuppressFinalize(this);
	}

}

internal static class ChatPanelStateChangeFilter
{
	public static bool IsRelevant(string? currentSessionId, SessionStateChange change)
	{
		if(change.SessionId is not null && change.SessionId != currentSessionId)
		{
			return false;
		}

		const SessionChangeKind relevantChanges =
			SessionChangeKind.CurrentSession |
			SessionChangeKind.SessionSummary |
			SessionChangeKind.ConversationStructure |
			SessionChangeKind.WorkingState;
		return (change.Kind & relevantChanges) != 0;
	}
}
