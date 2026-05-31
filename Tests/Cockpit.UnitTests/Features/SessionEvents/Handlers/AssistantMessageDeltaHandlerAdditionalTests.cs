using Cockpit.Features.SessionEvents;
using Cockpit.Features.SessionEvents.Models;
using Cockpit.Features.Sessions.Models;
using GitHub.Copilot;
using Microsoft.Extensions.Logging.Abstractions;
using Shouldly;

namespace Cockpit.UnitTests.Features.SessionEvents.Handlers;

/// <summary>
/// Additional tests for <c>AssistantMessageDeltaHandler</c> covering the live routing
/// of streaming deltas into the thinking panel and the null-data guard.
/// </summary>
public class AssistantMessageDeltaHandlerAdditionalTests
{
	static readonly ModelInfo testModel = new() { Id = "test", Name = "Test Model" };
	static SessionModel CreateSession() => new()
	{
		Id = "sessionId",
		Title = "Test Session",
		CreatedAt = DateTime.UtcNow,
		LastActivity = DateTime.UtcNow,
		Model = testModel,
		Context = new()
		{
			CurrentWorkingDirectory = "",
			WorkspacePath = null,
			GitRoot = null,
			Branch = null,
			Repository = null
		}
	};
	static SessionEventProcessor CreateProcessor() => new(NullLogger<SessionEventProcessor>.Instance);

	[Fact]
	public void Handle_DuringActiveGroup_RoutesLiveToThinkingPanel()
	{
		// Arrange — open a working group first
		SessionModel session = CreateSession();
		SessionEventProcessor processor = CreateProcessor();

		processor.Process(session, new ToolExecutionStartEvent
		{
			Data = new ToolExecutionStartData { ToolCallId = "tc1", ToolName = "read_file" },
			Timestamp = DateTimeOffset.UtcNow
		});

		// Act — delta arrives while the group is open
		processor.Process(session, new AssistantMessageDeltaEvent
		{
			Data = new AssistantMessageDeltaData { MessageId = "msg1", DeltaContent = "Thinking..." },
			Timestamp = DateTimeOffset.UtcNow
		});

		// Assert — a ThinkingEventModel of type Message is added to the group's event list
		List<ThinkingEventModel> events = session.ActiveWorkingGroup!.GetEventsSnapshot();
		ThinkingEventModel? thinkingMsg = events.FirstOrDefault(e => e.Type == ThinkingEventTypeEnum.Message);
		thinkingMsg.ShouldNotBeNull();
		thinkingMsg!.Message.ShouldBe("Thinking...");

		// Also tracked in StreamingThinkingEvents so subsequent deltas update the same entry
		session.StreamingThinkingEvents.ShouldContainKey("msg1");
	}

	[Fact]
	public void Handle_DuringActiveGroup_AccumulatesInThinkingPanelAcrossDeltas()
	{
		// Multiple deltas for the same messageId accumulate in a single ThinkingEventModel
		SessionModel session = CreateSession();
		SessionEventProcessor processor = CreateProcessor();

		processor.Process(session, new ToolExecutionStartEvent
		{
			Data = new ToolExecutionStartData { ToolCallId = "tc1", ToolName = "read_file" },
			Timestamp = DateTimeOffset.UtcNow
		});

		processor.Process(session, new AssistantMessageDeltaEvent
		{
			Data = new AssistantMessageDeltaData { MessageId = "msg1", DeltaContent = "Hello " },
			Timestamp = DateTimeOffset.UtcNow
		});
		processor.Process(session, new AssistantMessageDeltaEvent
		{
			Data = new AssistantMessageDeltaData { MessageId = "msg1", DeltaContent = "world" },
			Timestamp = DateTimeOffset.UtcNow
		});

		// Assert — single entry with concatenated content; no duplicate StreamingThinkingEvents
		List<ThinkingEventModel> events = session.ActiveWorkingGroup!.GetEventsSnapshot();
		ThinkingEventModel? thinkingMsg = events.FirstOrDefault(e => e.Type == ThinkingEventTypeEnum.Message);
		thinkingMsg.ShouldNotBeNull();
		thinkingMsg!.Message.ShouldBe("Hello world");
		session.StreamingThinkingEvents.Count(kv => kv.Key == "msg1").ShouldBe(1);
	}

	[Fact]
	public void Handle_NullData_IsIgnored()
	{
		// A delta event with null data must not throw and must leave the session untouched
		SessionModel session = CreateSession();
		SessionEventProcessor processor = CreateProcessor();

		Should.NotThrow(() => processor.Process(session, new AssistantMessageDeltaEvent
		{
			Data = null!,
			Timestamp = DateTimeOffset.UtcNow
		}));

		session.Messages.ShouldBeEmpty();
	}
}
