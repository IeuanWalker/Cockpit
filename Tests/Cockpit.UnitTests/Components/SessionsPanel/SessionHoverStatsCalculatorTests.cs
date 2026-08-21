using Cockpit.Components.Pages.SessionsPanel;
using Cockpit.Features.SessionEvents.Models;
using Cockpit.Features.Sessions.Models;
using GitHub.Copilot;
using Shouldly;

namespace Cockpit.UnitTests.Components.SessionsPanel;

public class SessionHoverStatsCalculatorTests
{
	static readonly ModelInfo testModel = new() { Id = "test", Name = "Test" };

	[Fact]
	public void Calculate_NotLoaded_ReturnsNull()
	{
		SessionModel session = CreateSession();

		SessionHoverStatsCalculator.Calculate(session, DateTime.Today).ShouldBeNull();
	}

	[Theory]
	[InlineData(SdkSessionStateEnum.Loaded)]
	[InlineData(SdkSessionStateEnum.Resumed)]
	public void Calculate_LoadedSession_AggregatesMessagesTimeToolsTurnsAndTokens(SdkSessionStateEnum sdkState)
	{
		DateTime now = new(2026, 8, 21, 12, 0, 0, DateTimeKind.Local);
		SessionModel session = CreateSession();
		session.Lifecycle.SetSdkState(sdkState);

		ToolExecutionModel childTool = CreateTool("child");
		ToolExecutionModel firstTool = CreateTool("first");
		firstTool.AddChild(childTool);
		ActivityGroupModel completedGroup = CreateGroup("completed", now.AddMinutes(-10), now.AddMinutes(-7), firstTool);
		ActivityGroupModel activeGroup = CreateGroup("active", now.AddMinutes(-2), null, CreateTool("second"));
		session.Conversation.ReplaceMessages([
			CreateMessage("user"),
			CreateMessage("activity", completedGroup),
			CreateMessage("assistant")
		]);
		session.Conversation.ActiveWorkingGroup = activeGroup;
		session.Conversation.TokenUsageInfo = new TokenUsageInfoModel { CurrentTokens = 12_500, TokenLimit = 128_000 };

		SessionHoverStats stats = SessionHoverStatsCalculator.Calculate(session, now).ShouldNotBeNull();

		stats.MessageCount.ShouldBe(3);
		stats.AgentWorkingTime.ShouldBe(TimeSpan.FromMinutes(5));
		stats.ToolCallCount.ShouldBe(3);
		stats.AgentTurnCount.ShouldBe(2);
		stats.CurrentTokens.ShouldBe(12_500);
		stats.TokenLimit.ShouldBe(128_000);
	}

	[Fact]
	public void Calculate_DeduplicatesTheActiveGroupAndIgnoresPlaceholdersAndInvalidDurations()
	{
		DateTime now = new(2026, 8, 21, 12, 0, 0, DateTimeKind.Local);
		SessionModel session = CreateSession();
		session.Lifecycle.SetSdkState(SdkSessionStateEnum.Loaded);
		ActivityGroupModel group = CreateGroup("group", now.AddMinutes(-1), now, CreateTool("tool"));
		ActivityGroupModel placeholder = CreateGroup("placeholder", now.AddMinutes(-2), null, CreateTool("ignored"));
		placeholder.IsPlaceholder = true;
		session.Conversation.ReplaceMessages([
			CreateMessage("activity", group),
			CreateMessage("placeholder", placeholder)
		]);
		session.Conversation.ActiveWorkingGroup = group;

		SessionHoverStats stats = SessionHoverStatsCalculator.Calculate(session, now).ShouldNotBeNull();

		stats.AgentTurnCount.ShouldBe(1);
		stats.ToolCallCount.ShouldBe(1);
		stats.AgentWorkingTime.ShouldBe(TimeSpan.FromMinutes(1));
	}

	static SessionModel CreateSession() => new()
	{
		Id = "session",
		Title = "Session",
		CreatedAt = DateTime.UnixEpoch,
		LastActivity = DateTime.UnixEpoch,
		Model = testModel,
		Context = new Cockpit.Features.Sessions.Models.SessionContext
		{
			CurrentWorkingDirectory = null,
			WorkspacePath = null,
			GitRoot = null,
			Repository = null,
			Branch = null
		}
	};

	static ChatMessageModel CreateMessage(string id, ActivityGroupModel? group = null) => new()
	{
		Id = id,
		Content = id,
		ActivityGroup = group,
		Type = group is null ? MessageTypeEnum.Text : MessageTypeEnum.ActivityGroup,
		EventJson = null
	};

	static ActivityGroupModel CreateGroup(string id, DateTime start, DateTime? end, params ToolExecutionModel[] tools)
	{
		ActivityGroupModel group = new() { Id = id, StartTime = start, EndTime = end };
		foreach(ToolExecutionModel tool in tools)
		{
			group.AddEvent(new ThinkingEventModel
			{
				Type = ThinkingEventTypeEnum.Tool,
				Tool = tool,
				EventJson = null
			});
		}

		return group;
	}

	static ToolExecutionModel CreateTool(string id) => new() { Id = id, ToolName = id, StartTime = DateTime.UnixEpoch };
}
