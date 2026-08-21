using Cockpit.Features.SessionEvents.Models;
using Cockpit.Features.Sessions.Models;

namespace Cockpit.Components.Pages.SessionsPanel;

internal sealed record SessionHoverStats(
	int MessageCount,
	TimeSpan AgentWorkingTime,
	int ToolCallCount,
	int AgentTurnCount,
	double? CurrentTokens,
	double? TokenLimit);

internal static class SessionHoverStatsCalculator
{
	internal static SessionHoverStats? Calculate(SessionModel session, DateTime now)
	{
		if(session.Lifecycle.SdkState is not (SdkSessionStateEnum.Loaded or SdkSessionStateEnum.Resumed))
		{
			return null;
		}

		IReadOnlyList<ChatMessageModel> messages = session.Conversation.MessagesSnapshot;
		List<ActivityGroupModel> groups = GetActivityGroups(session, messages);
		TimeSpan workingTime = groups.Aggregate(TimeSpan.Zero, (total, group) => total + GetDuration(group, now));
		int toolCallCount = CountToolCalls(groups);
		TokenUsageInfoModel? usage = session.Conversation.TokenUsageInfo;

		return new SessionHoverStats(
			messages.Count,
			workingTime,
			toolCallCount,
			groups.Count,
			usage?.CurrentTokens,
			usage?.TokenLimit);
	}

	static List<ActivityGroupModel> GetActivityGroups(SessionModel session, IReadOnlyList<ChatMessageModel> messages)
	{
		Dictionary<string, ActivityGroupModel> groups = new(StringComparer.Ordinal);
		foreach(ActivityGroupModel group in messages
			.Select(message => message.ActivityGroup)
			.Where(group => group is not null && !group.IsPlaceholder)
			.Cast<ActivityGroupModel>())
		{
			groups.TryAdd(group.Id, group);
		}

		ActivityGroupModel? activeGroup = session.Conversation.ActiveWorkingGroup;
		if(activeGroup is { IsPlaceholder: false })
		{
			groups.TryAdd(activeGroup.Id, activeGroup);
		}

		return [.. groups.Values];
	}

	static TimeSpan GetDuration(ActivityGroupModel group, DateTime now)
	{
		if(group.StartTime == DateTime.MinValue)
		{
			return TimeSpan.Zero;
		}

		DateTime end = group.EndTime ?? now;
		return end > group.StartTime ? end - group.StartTime : TimeSpan.Zero;
	}

	static int CountToolCalls(IEnumerable<ActivityGroupModel> groups)
	{
		HashSet<ToolExecutionModel> visited = new(ReferenceEqualityComparer.Instance);
		foreach(ToolExecutionModel tool in groups.SelectMany(group => group.Tools))
		{
			AddToolAndChildren(tool, visited);
		}

		return visited.Count;
	}

	static void AddToolAndChildren(ToolExecutionModel tool, HashSet<ToolExecutionModel> visited)
	{
		if(!visited.Add(tool))
		{
			return;
		}

		foreach(ToolExecutionModel child in tool.GetChildrenSnapshot())
		{
			AddToolAndChildren(child, visited);
		}
	}
}
