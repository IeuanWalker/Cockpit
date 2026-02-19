using System.Text.Json;
using Cockpit.Features.SessionEvents;
using Cockpit.Features.SessionEvents.Models;
using Cockpit.Features.SessionEvents.Models.Enums;
using Shouldly;

namespace Cockpit.UnitTests.Features.SessionEvents.Handlers;

public class SessionEventHelpersTests
{
	static ActivityGroupModel CreateGroupWithTool(string toolCallId, string toolName = "test_tool")
	{
		ActivityGroupModel group = new();
		ToolExecutionModel tool = new() { ToolCallId = toolCallId, ToolName = toolName };
		group.AddEvent(new ThinkingEventModel
		{
			Type = ThinkingEventTypeEnum.Tool,
			Tool = tool
		});
		return group;
	}

	#region FindToolExecution

	[Fact]
	public void FindToolExecution_ExistingTopLevel_ReturnsCorrectTool()
	{
		// Arrange
		ActivityGroupModel group = CreateGroupWithTool("tc1");

		// Act
		ToolExecutionModel? result = SessionEventHelpers.FindToolExecution(group, "tc1");

		// Assert
		result.ShouldNotBeNull();
		result.ToolCallId.ShouldBe("tc1");
	}

	[Fact]
	public void FindToolExecution_NestedChild_ReturnsChild()
	{
		// Arrange
		ActivityGroupModel group = CreateGroupWithTool("parent1");
		ToolExecutionModel parent = group.Tools.First();

		ToolExecutionModel child = new() { ToolCallId = "child1", ToolName = "child_tool" };
		parent.AddChild(child);

		// Act
		ToolExecutionModel? result = SessionEventHelpers.FindToolExecution(group, "child1");

		// Assert
		result.ShouldNotBeNull();
		result.ToolCallId.ShouldBe("child1");
	}

	[Fact]
	public void FindToolExecution_NullToolCallId_ReturnsNull()
	{
		// Arrange
		ActivityGroupModel group = CreateGroupWithTool("tc1");

		// Act
		ToolExecutionModel? result = SessionEventHelpers.FindToolExecution(group, null);

		// Assert
		result.ShouldBeNull();
	}

	[Fact]
	public void FindToolExecution_NonExistentId_ReturnsNull()
	{
		// Arrange
		ActivityGroupModel group = CreateGroupWithTool("tc1");

		// Act
		ToolExecutionModel? result = SessionEventHelpers.FindToolExecution(group, "tc999");

		// Assert
		result.ShouldBeNull();
	}

	[Fact]
	public void FindToolExecution_EmptyGroup_ReturnsNull()
	{
		// Arrange
		ActivityGroupModel group = new();

		// Act
		ToolExecutionModel? result = SessionEventHelpers.FindToolExecution(group, "tc1");

		// Assert
		result.ShouldBeNull();
	}

	[Fact]
	public void FindToolExecution_NonToolEvent_IsSkipped()
	{
		// Arrange
		ActivityGroupModel group = new();
		group.AddEvent(new ThinkingEventModel
		{
			Type = ThinkingEventTypeEnum.Message,
			Message = "some message",
			Tool = null
		});
		ToolExecutionModel tool = new() { ToolCallId = "tc1" };
		group.AddEvent(new ThinkingEventModel { Type = ThinkingEventTypeEnum.Tool, Tool = tool });

		// Act
		ToolExecutionModel? result = SessionEventHelpers.FindToolExecution(group, "tc1");

		// Assert
		result.ShouldNotBeNull();
		result.ShouldBeSameAs(tool);
	}

	#endregion

	#region DeserializeArguments

	[Fact]
	public void DeserializeArguments_Null_ReturnsNull()
	{
		// Act & Assert
		SessionEventHelpers.DeserializeArguments(null).ShouldBeNull();
	}

	[Fact]
	public void DeserializeArguments_Dictionary_ReturnsSameDictionary()
	{
		// Arrange
		Dictionary<string, object> dict = new() { ["key"] = "value" };

		// Act
		Dictionary<string, object>? result = SessionEventHelpers.DeserializeArguments(dict);

		// Assert
		result.ShouldBeSameAs(dict);
	}

	[Fact]
	public void DeserializeArguments_JsonElementObject_ReturnsDeserializedDictionary()
	{
		// Arrange
		JsonDocument doc = JsonDocument.Parse("""{"path": "/tmp/file.txt", "encoding": "utf8"}""");
		JsonElement element = doc.RootElement;

		// Act
		Dictionary<string, object>? result = SessionEventHelpers.DeserializeArguments(element);

		// Assert
		result.ShouldNotBeNull();
		result.ContainsKey("path").ShouldBeTrue();
		result.ContainsKey("encoding").ShouldBeTrue();
	}

	[Fact]
	public void DeserializeArguments_JsonElementNonObject_ReturnsNull()
	{
		// Arrange — JSON array, not an object
		JsonDocument doc = JsonDocument.Parse("""["a","b"]""");
		JsonElement element = doc.RootElement;

		// Act
		Dictionary<string, object>? result = SessionEventHelpers.DeserializeArguments(element);

		// Assert
		result.ShouldBeNull();
	}

	[Fact]
	public void DeserializeArguments_UnrecognizedType_ReturnsNull()
	{
		// Act & Assert
		SessionEventHelpers.DeserializeArguments(42).ShouldBeNull();
	}

	#endregion

	#region StreamSummaryTextAsync

	[Fact]
	public async Task StreamSummaryTextAsync_StreamsFullContent()
	{
		// Arrange
		ChatMessageModel message = new() { Content = string.Empty, IsStreaming = true };
		const string fullText = "abc";
		int notifyCount = 0;

		// Act
		await SessionEventHelpers.StreamSummaryTextAsync(message, fullText, () => notifyCount++);

		// Assert
		message.Content.ShouldBe(fullText);
		message.IsStreaming.ShouldBeFalse();
		message.IsComplete.ShouldBeTrue();
		notifyCount.ShouldBeGreaterThan(0);
	}

	[Fact]
	public async Task StreamSummaryTextAsync_EmptyText_CompletesImmediately()
	{
		// Arrange
		ChatMessageModel message = new() { Content = "existing", IsStreaming = true };

		// Act
		await SessionEventHelpers.StreamSummaryTextAsync(message, string.Empty, () => { });

		// Assert
		message.Content.ShouldBe(string.Empty);
		message.IsStreaming.ShouldBeFalse();
		message.IsComplete.ShouldBeTrue();
	}

	#endregion
}
