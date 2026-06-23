using Cockpit.Features.Sessions;
using Cockpit.Features.Sessions.Models;
using Microsoft.AspNetCore.Components;

namespace Cockpit.Components.Pages.ContextPanel;

public sealed partial class TokenUsagePanel : ComponentBase, IDisposable
{
	readonly SessionListFeature _sessionListFeature;
	readonly SessionFeature _sessionFeature;

	public TokenUsagePanel(SessionListFeature sessionListFeature, SessionFeature sessionFeature)
	{
		_sessionListFeature = sessionListFeature;
		_sessionFeature = sessionFeature;
	}

	TokenUsageBreakdownPopup _breakdownPopup = default!; // assigned by @ref

	SessionModel? CurrentSession => _sessionListFeature.CurrentSession;

	double UsagePercent
	{
		get
		{
			TokenUsageInfoModel? info = CurrentSession?.TokenUsageInfo;
			if(info is null || info.TokenLimit <= 0)
			{
				return 0;
			}

			return Math.Min(100, info.CurrentTokens / info.TokenLimit * 100);
		}
	}

	string BarColor => UsagePercent >= 90 ? "#ef4444" : UsagePercent >= 70 ? "#f59e0b" : "var(--accent-color)";

	bool IsCompactDisabled =>
		CurrentSession is null ||
		CurrentSession.IsCompacting ||
		CurrentSession.Status == SessionStatusEnum.Running;

	string CompactTooltip => CurrentSession?.IsCompacting == true
		? "Compaction in progress…"
		: CurrentSession?.Status == SessionStatusEnum.Running
			? "Cannot compact while the session is busy"
			: "Compact context window to free up space";

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

		return ((int)tokens).ToString();
	}

	protected override void OnInitialized()
	{
		_sessionListFeature.OnStateChanged += OnStateChanged;
	}

	void OnStateChanged() => InvokeAsync(StateHasChanged);

	void OpenBreakdown() => _breakdownPopup.Open(CurrentSession?.TokenUsageInfo);

	async Task RunCompact()
	{
		if(IsCompactDisabled)
		{
			return;
		}

		await _sessionFeature.CompactContextAsync();
	}

	public void Dispose()
	{
		_sessionListFeature.OnStateChanged -= OnStateChanged;
		GC.SuppressFinalize(this);
	}
}
