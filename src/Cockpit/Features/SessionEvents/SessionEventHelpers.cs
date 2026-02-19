using System.Text.Json;
using Cockpit.Features.SessionEvents.Models;
using Cockpit.Features.SessionEvents.Models.Enums;
using GitHub.Copilot.SDK;

namespace Cockpit.Features.SessionEvents;

static class SessionEventHelpers
{
	static readonly JsonSerializerOptions jsonOptions = new()
	{
		WriteIndented = true
	};

	internal static ToolExecutionModel? FindToolExecution(ActivityGroupModel group, string? toolCallId)
	{
		if(toolCallId is null)
		{
			return null;
		}

		foreach(ThinkingEventModel evt in group.GetEventsSnapshot())
		{
			if(evt.Type != ThinkingEventTypeEnum.Tool || evt.Tool is null)
			{
				continue;
			}

			if(evt.Tool.ToolCallId == toolCallId)
			{
				return evt.Tool;
			}

			ToolExecutionModel? child = evt.Tool
				.GetChildrenSnapshot()
				.FirstOrDefault(c => c.ToolCallId == toolCallId);

			if(child is not null)
			{
				return child;
			}
		}

		return null;
	}

	internal static string SerializeEvent(SessionEvent evt)
	{
		try
		{
			return JsonSerializer.Serialize(evt, evt.GetType(), jsonOptions);
		}
		catch
		{
			return evt.ToString() ?? string.Empty;
		}
	}

	internal static async Task StreamSummaryTextAsync(ChatMessageModel message, string fullText, Action notifyStateChanged)
	{
		const int chunkSize = 3;
		const int delayMs = 8;

		for(int i = 0; i < fullText.Length; i += chunkSize)
		{
			int end = Math.Min(i + chunkSize, fullText.Length);
			message.Content = fullText[..end];
			notifyStateChanged();
			await Task.Delay(delayMs);
		}

		message.Content = fullText;
		message.IsStreaming = false;
		message.IsComplete = true;
		notifyStateChanged();
	}

	internal static Dictionary<string, object>? DeserializeArguments(object? arguments)
	{
		if(arguments is null)
		{
			return null;
		}

		try
		{
			if(arguments is Dictionary<string, object> dict)
			{
				return dict;
			}

			if(arguments is JsonElement je)
			{
				if(je.ValueKind == JsonValueKind.Object)
				{
					return JsonSerializer.Deserialize<Dictionary<string, object>>(je.GetRawText());
				}
			}
		}
		catch
		{
			// Fall through to return null
		}

		return null;
	}
}
