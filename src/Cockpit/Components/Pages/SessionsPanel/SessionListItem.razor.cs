using Cockpit.Features.Sessions;
using Cockpit.Features.Sessions.Models;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.JSInterop;
using System.Globalization;

namespace Cockpit.Components.Pages.SessionsPanel;

public sealed partial class SessionListItem : IDisposable
{
	[Inject] public required IJSRuntime JSRuntime { get; set; }
	[Inject] public required SessionListFeature SessionListFeature { get; set; }

	[Parameter] public required SessionModel Session { get; set; }
	[Parameter] public bool IsActive { get; set; }
	[Parameter] public EventCallback<SessionModel> OnSelect { get; set; }
	[Parameter] public EventCallback<MouseEventArgs> OnDelete { get; set; }
	[Parameter] public required string TimeAgo { get; set; }

	ElementReference _sessionItem;
	ElementReference _tooltip;
	SessionStatusEnum? ListStatus => Session.DisplayStatus == SessionStatusEnum.Idle ? null : Session.DisplayStatus;
	string ListStatusText => Session.DisplayStatus == SessionStatusEnum.Error ? "Error" : "Idle";
	SessionHoverStats? HoverStats => SessionHoverStatsCalculator.Calculate(Session, DateTime.Now);

	string WorkingDirectoryLabel => string.IsNullOrWhiteSpace(Session.Context.CurrentWorkingDirectory)
		? "No working directory"
		: Session.Context.CurrentWorkingDirectory;

	async Task HandleSelect() => await OnSelect.InvokeAsync(Session);
	async Task HandleDelete(MouseEventArgs e) => await OnDelete.InvokeAsync(e);
	async Task ShowTooltip() => await JSRuntime.InvokeVoidAsync("cockpit.showSessionTooltip", _sessionItem, _tooltip);
	async Task HideTooltip() => await JSRuntime.InvokeVoidAsync("cockpit.hideSessionTooltip", _tooltip);

	protected override void OnInitialized()
	{
		SessionListFeature.OnSessionStateChanged += OnSessionStateChanged;
	}

	void OnSessionStateChanged(SessionStateChange change)
	{
		const SessionChangeKind statsChanges = SessionChangeKind.ConversationContent |
			SessionChangeKind.ConversationStructure |
			SessionChangeKind.ConversationReset |
			SessionChangeKind.WorkingState;
		if((change.Kind & statsChanges) != 0 &&
			(change.SessionId is null || string.Equals(change.SessionId, Session.Id, StringComparison.Ordinal)))
		{
			_ = InvokeAsync(StateHasChanged);
		}
	}

	static string FormatCount(int count) => count.ToString("N0", CultureInfo.CurrentCulture);

	static string FormatDuration(TimeSpan duration)
	{
		if(duration.TotalDays >= 1)
		{
			return $"{(int)duration.TotalDays}d {duration.Hours}h";
		}

		if(duration.TotalHours >= 1)
		{
			return $"{(int)duration.TotalHours}h {duration.Minutes}m";
		}

		if(duration.TotalMinutes >= 1)
		{
			return $"{(int)duration.TotalMinutes}m {duration.Seconds}s";
		}

		return duration.TotalSeconds < 1 ? "<1s" : $"{(int)duration.TotalSeconds}s";
	}

	static string FormatTokens(double tokens)
	{
		if(tokens >= 1_000_000)
		{
			return $"{tokens / 1_000_000:F1}M";
		}

		if(tokens >= 1_000)
		{
			return $"{tokens / 1_000:F1}K";
		}

		return ((int)tokens).ToString("N0", CultureInfo.CurrentCulture);
	}

	public void Dispose()
	{
		SessionListFeature.OnSessionStateChanged -= OnSessionStateChanged;
		GC.SuppressFinalize(this);
	}
}
