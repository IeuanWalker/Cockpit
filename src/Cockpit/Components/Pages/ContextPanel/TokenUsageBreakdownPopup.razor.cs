using Cockpit.Components.Controls;
using Cockpit.Features.Sessions.Models;
using Microsoft.AspNetCore.Components;

namespace Cockpit.Components.Pages.ContextPanel;

public partial class TokenUsageBreakdownPopup : ComponentBase
{
	PopupBase _popup = default!;
	TokenUsageInfoModel? _usageInfo;

	double UsagePercent => _usageInfo is null || _usageInfo.TokenLimit <= 0
		? 0
		: Math.Min(100, _usageInfo.CurrentTokens / _usageInfo.TokenLimit * 100);

	string BarColor => UsagePercent >= 90 ? "#ef4444" : UsagePercent >= 70 ? "#f59e0b" : "var(--accent-color)";

	static double GetPercent(double value, double total)
		=> total <= 0 ? 0 : Math.Min(100, value / total * 100);

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

	public void Open(TokenUsageInfoModel? usageInfo)
	{
		_usageInfo = usageInfo;
		_popup.Open();
	}
}
