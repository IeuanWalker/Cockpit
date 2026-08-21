using System.Globalization;
using Cockpit.Features.Sessions.Models;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.JSInterop;

namespace Cockpit.Components.Pages.SessionsPanel;

public sealed partial class SessionListItem
{
	[Inject] public required IJSRuntime JSRuntime { get; set; }

	[Parameter] public required SessionModel Session { get; set; }
	[Parameter] public bool Compact { get; set; }
	[Parameter] public bool IsActive { get; set; }
	[Parameter] public EventCallback<SessionModel> OnSelect { get; set; }
	[Parameter] public EventCallback<MouseEventArgs> OnDelete { get; set; }
	[Parameter] public required string TimeAgo { get; set; }

	ElementReference _sessionItem;
	ElementReference _tooltip;
	readonly string _tooltipId = $"session-tooltip-{Guid.NewGuid():N}";
	SessionHoverStats? _hoverStats;
	bool _hasCalculatedHoverStats;
	SessionStatusEnum? ListStatus => Session.DisplayStatus == SessionStatusEnum.Idle ? null : Session.DisplayStatus;
	string ListStatusText => Session.DisplayStatus == SessionStatusEnum.Error ? "Error" : "Idle";
	SessionHoverStats? HoverStats => _hoverStats;
	string TooltipId => _tooltipId;

	string WorkingDirectoryLabel => string.IsNullOrWhiteSpace(Session.Context.CurrentWorkingDirectory)
		? "No working directory"
		: Session.Context.CurrentWorkingDirectory;

	async Task HandleSelect() => await OnSelect.InvokeAsync(Session);
	async Task HandleDelete(MouseEventArgs e) => await OnDelete.InvokeAsync(e);
	async Task ShowTooltip()
	{
		if(!_hasCalculatedHoverStats)
		{
			_hoverStats = SessionHoverStatsCalculator.Calculate(Session, DateTime.Now);
			_hasCalculatedHoverStats = true;
			await InvokeAsync(StateHasChanged);
		}

		await JSRuntime.InvokeVoidAsync("cockpit.showSessionTooltip", _sessionItem, _tooltip);
	}

	async Task HideTooltip()
	{
		await JSRuntime.InvokeVoidAsync("cockpit.hideSessionTooltip", _tooltip);
		_hoverStats = null;
		_hasCalculatedHoverStats = false;
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
}
