using System.Text.Json;
using Cockpit.Features.Telemetry.Models;
using Cockpit.Utilities.Logging;

namespace Cockpit.Features.Telemetry;

sealed class TelemetryFileService
{
	readonly SemaphoreSlim _cacheLock = new(1, 1);
	string? _cachedFilePath;
	DateTime _cachedLastWriteTimeUtc;
	long _cachedFileSize;
	List<TelemetryTrace> _cachedTraces = [];

	public string TelemetryDirectory { get; } = ResolveTelemetryDirectory();

	static string ResolveTelemetryDirectory()
	{
		string logDir = LogDirectoryHelper.LogDirectory;
		string cockpitRoot = Path.GetDirectoryName(logDir)!;
		string dir = Path.Combine(cockpitRoot, "telemetry");
		Directory.CreateDirectory(dir);
		return dir;
	}

	public List<TelemetryFileInfo> GetAvailableFiles()
	{
		DirectoryInfo telemetryDirectory = new(TelemetryDirectory);
		if(!telemetryDirectory.Exists)
		{
			return [];
		}

		IOrderedEnumerable<TelemetryFileInfo> files = telemetryDirectory
			.EnumerateFiles("otel-*.jsonl", SearchOption.TopDirectoryOnly)
			.Select(file => new TelemetryFileInfo
			{
				FileName = file.Name,
				FullPath = file.FullName,
				Date = TryParseFileDate(file.Name) ?? file.LastWriteTime,
				SizeBytes = file.Length
			})
			.OrderByDescending(file => file.Date)
			.ThenByDescending(file => file.FileName);

		return [.. files];
	}

	public async Task<List<TelemetryTrace>> ReadTracesAsync(string filePath, CancellationToken ct = default)
	{
		List<TelemetryTrace> traces = await GetCachedOrReadTracesAsync(filePath, ct);
		return [.. traces.Select(CloneTrace)];
	}

	public async Task<List<TelemetrySpan>> ReadAllSpansAsync(string filePath, CancellationToken ct = default)
	{
		List<TelemetryTrace> traces = await GetCachedOrReadTracesAsync(filePath, ct);
		return [.. traces.SelectMany(trace => trace.Spans).Select(CloneSpan)];
	}

	public async Task<TelemetrySummary> GetSummaryAsync(string filePath, CancellationToken ct = default)
	{
		List<TelemetryTrace> traces = await GetCachedOrReadTracesAsync(filePath, ct);
		TelemetrySummary summary = CreateSummary(traces);
		return new TelemetrySummary
		{
			TotalTraces = summary.TotalTraces,
			TotalSpans = summary.TotalSpans,
			ErrorCount = summary.ErrorCount,
			StartTime = summary.StartTime,
			EndTime = summary.EndTime
		};
	}

	async Task<List<TelemetryTrace>> GetCachedOrReadTracesAsync(string filePath, CancellationToken ct)
	{
		if(string.IsNullOrWhiteSpace(filePath) || !File.Exists(filePath))
		{
			return [];
		}

		FileInfo fileInfo = new(filePath);

		await _cacheLock.WaitAsync(ct);
		try
		{
			if(string.Equals(_cachedFilePath, filePath, StringComparison.OrdinalIgnoreCase)
				&& _cachedLastWriteTimeUtc == fileInfo.LastWriteTimeUtc
				&& _cachedFileSize == fileInfo.Length)
			{
				return _cachedTraces;
			}

			List<TelemetryTrace> traces = await ParseTracesAsync(filePath, ct);

			_cachedFilePath = filePath;
			_cachedLastWriteTimeUtc = fileInfo.LastWriteTimeUtc;
			_cachedFileSize = fileInfo.Length;
			_cachedTraces = traces;

			return _cachedTraces;
		}
		finally
		{
			_cacheLock.Release();
		}
	}

	static async Task<List<TelemetryTrace>> ParseTracesAsync(string filePath, CancellationToken ct)
	{
		Dictionary<string, List<TelemetrySpan>> tracesById = new(StringComparer.Ordinal);

		await using FileStream stream = new(filePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite, 4096, FileOptions.Asynchronous | FileOptions.SequentialScan);
		using StreamReader reader = new(stream);

		while(true)
		{
			ct.ThrowIfCancellationRequested();

			string? line = await reader.ReadLineAsync(ct);
			if(line is null)
			{
				break;
			}

			if(string.IsNullOrWhiteSpace(line))
			{
				continue;
			}

			try
			{
				using JsonDocument document = JsonDocument.Parse(line);
				JsonElement root = document.RootElement;

				// SDK flat format: each line is {"type":"span", "traceId":"…", …}
				if(root.TryGetProperty("type", out JsonElement typeElement)
					&& typeElement.ValueKind == JsonValueKind.String
					&& string.Equals(typeElement.GetString(), "span", StringComparison.OrdinalIgnoreCase))
				{
					TelemetrySpan? span = ParseFlatSpan(root);
					if(span is not null)
					{
						AddSpanToDict(span, tracesById);
					}
				}
				// Standard OTLP batch format: {"resourceSpans":[…]}
				else
				{
					ParseOtlpTraceBatch(root, tracesById);
				}
			}
			catch(JsonException)
			{
				// Ignore malformed or non-JSON lines defensively.
			}
		}

		return [..
			tracesById
				.Select(pair => new TelemetryTrace
				{
					TraceId = pair.Key,
					Spans = [.. pair.Value.OrderBy(span => span.StartTime).ThenBy(span => span.Name, StringComparer.Ordinal)]
				})
				.OrderByDescending(trace => trace.StartTime)
				.ThenBy(trace => trace.TraceId, StringComparer.Ordinal)];
	}

	static void AddSpanToDict(TelemetrySpan span, Dictionary<string, List<TelemetrySpan>> tracesById)
	{
		if(!tracesById.TryGetValue(span.TraceId, out List<TelemetrySpan>? traceSpans))
		{
			traceSpans = [];
			tracesById[span.TraceId] = traceSpans;
		}

		traceSpans.Add(span);
	}

	// --- SDK flat format: {"type":"span","traceId":"…","startTime":[sec,nanos],…} ---

	static TelemetrySpan? ParseFlatSpan(JsonElement root)
	{
		string? traceId = GetString(root, "traceId");
		string? spanId = GetString(root, "spanId");
		string? name = GetString(root, "name");
		if(string.IsNullOrWhiteSpace(traceId) || string.IsNullOrWhiteSpace(spanId) || string.IsNullOrWhiteSpace(name))
		{
			return null;
		}

		DateTime startTime = ParseTimestamp(root, "startTime");
		DateTime endTime = ParseTimestamp(root, "endTime");
		if(endTime < startTime)
		{
			endTime = startTime;
		}

		(JsonElement statusElement, bool hasStatus) = root.TryGetProperty("status", out JsonElement currentStatus)
			? (currentStatus, true)
			: (default, false);

		return new TelemetrySpan
		{
			TraceId = traceId,
			SpanId = spanId,
			ParentSpanId = NormalizeParentSpanId(GetString(root, "parentSpanId")),
			Name = name,
			Kind = ParseSpanKind(GetInt32(root, "kind")),
			StartTime = startTime,
			EndTime = endTime,
			Status = hasStatus ? ParseSpanStatus(GetInt32(statusElement, "code")) : SpanStatusEnum.Unset,
			StatusMessage = hasStatus ? GetString(statusElement, "message") : null,
			Attributes = ParseFlatAttributes(root, "attributes"),
			Events = ParseFlatEvents(root)
		};
	}

	/// <summary>
	/// Parses the SDK timestamp format: either a [seconds, nanoseconds] array or a "startTimeUnixNano" string.
	/// </summary>
	static DateTime ParseTimestamp(JsonElement parent, string propertyName)
	{
		if(!parent.TryGetProperty(propertyName, out JsonElement element))
		{
			return DateTime.MinValue;
		}

		// SDK format: [seconds, nanoseconds] array
		if(element.ValueKind == JsonValueKind.Array)
		{
			return ParseSecondsNanosArray(element);
		}

		// Fallback: raw nanosecond string/number (OTLP-style)
		string? text = element.ValueKind switch
		{
			JsonValueKind.String => element.GetString(),
			JsonValueKind.Number => element.GetRawText(),
			_ => null
		};

		return ParseUnixNanoTimestamp(text);
	}

	/// <summary>
	/// Converts a [seconds, nanoseconds] JSON array to a local DateTime.
	/// </summary>
	static DateTime ParseSecondsNanosArray(JsonElement array)
	{
		int index = 0;
		long seconds = 0;
		long nanos = 0;

		foreach(JsonElement item in array.EnumerateArray())
		{
			long value = item.ValueKind == JsonValueKind.Number ? item.GetInt64() : 0;
			if(index == 0)
			{
				seconds = value;
			}
			else if(index == 1)
			{
				nanos = value;
			}

			index++;
		}

		if(seconds == 0)
		{
			return DateTime.MinValue;
		}

		try
		{
			long totalMilliseconds = (seconds * 1000) + (nanos / 1_000_000);
			return DateTimeOffset.FromUnixTimeMilliseconds(totalMilliseconds).LocalDateTime;
		}
		catch(ArgumentOutOfRangeException)
		{
			return DateTime.MinValue;
		}
	}

	/// <summary>
	/// Parses SDK flat attributes: {"key": value, …} where value is a string, number, or bool.
	/// </summary>
	static Dictionary<string, string> ParseFlatAttributes(JsonElement parent, string propertyName)
	{
		if(!parent.TryGetProperty(propertyName, out JsonElement attributesElement))
		{
			return [];
		}

		// SDK format: flat {"key": "stringValue", "key2": 42, …}
		if(attributesElement.ValueKind == JsonValueKind.Object)
		{
			Dictionary<string, string> attributes = new(StringComparer.Ordinal);
			foreach(JsonProperty property in attributesElement.EnumerateObject())
			{
				attributes[property.Name] = property.Value.ValueKind switch
				{
					JsonValueKind.String => property.Value.GetString() ?? string.Empty,
					JsonValueKind.Number => property.Value.GetRawText(),
					JsonValueKind.True => bool.TrueString,
					JsonValueKind.False => bool.FalseString,
					_ => property.Value.GetRawText()
				};
			}

			return attributes;
		}

		// OTLP format: [{"key":"…","value":{"stringValue":"…"}}, …]
		if(attributesElement.ValueKind == JsonValueKind.Array)
		{
			return ParseOtlpAttributes(attributesElement);
		}

		return [];
	}

	/// <summary>
	/// Parses events from the SDK flat format where timestamps are [seconds, nanoseconds] arrays
	/// and attributes are flat objects.
	/// </summary>
	static List<TelemetrySpanEvent> ParseFlatEvents(JsonElement spanElement)
	{
		if(!spanElement.TryGetProperty("events", out JsonElement eventsElement) || eventsElement.ValueKind != JsonValueKind.Array)
		{
			return [];
		}

		List<TelemetrySpanEvent> events = [];
		foreach(JsonElement eventElement in eventsElement.EnumerateArray())
		{
			string? name = GetString(eventElement, "name");
			if(string.IsNullOrWhiteSpace(name))
			{
				continue;
			}

			// SDK uses "time" as [sec, nanos]; OTLP uses "timeUnixNano"
			DateTime timestamp = ParseTimestamp(eventElement, "time");
			if(timestamp == DateTime.MinValue)
			{
				timestamp = ParseTimestamp(eventElement, "timeUnixNano");
			}

			events.Add(new TelemetrySpanEvent
			{
				Name = name,
				Timestamp = timestamp,
				Attributes = ParseFlatAttributes(eventElement, "attributes")
			});
		}

		return events;
	}

	// --- Standard OTLP batch format: {"resourceSpans":[…]} ---

	static void ParseOtlpTraceBatch(JsonElement root, Dictionary<string, List<TelemetrySpan>> tracesById)
	{
		if(!root.TryGetProperty("resourceSpans", out JsonElement resourceSpans) || resourceSpans.ValueKind != JsonValueKind.Array)
		{
			return;
		}

		foreach(JsonElement resourceSpan in resourceSpans.EnumerateArray())
		{
			JsonElement scopeSpans = default;
			if(resourceSpan.TryGetProperty("scopeSpans", out JsonElement currentScopeSpans))
			{
				scopeSpans = currentScopeSpans;
			}
			else if(resourceSpan.TryGetProperty("instrumentationLibrarySpans", out JsonElement instrumentationLibrarySpans))
			{
				scopeSpans = instrumentationLibrarySpans;
			}

			if(scopeSpans.ValueKind != JsonValueKind.Array)
			{
				continue;
			}

			foreach(JsonElement scopeSpan in scopeSpans.EnumerateArray())
			{
				if(!scopeSpan.TryGetProperty("spans", out JsonElement spans) || spans.ValueKind != JsonValueKind.Array)
				{
					continue;
				}

				foreach(JsonElement spanElement in spans.EnumerateArray())
				{
					TelemetrySpan? span = ParseOtlpSpan(spanElement);
					if(span is not null)
					{
						AddSpanToDict(span, tracesById);
					}
				}
			}
		}
	}

	static TelemetrySpan? ParseOtlpSpan(JsonElement spanElement)
	{
		string? traceId = GetString(spanElement, "traceId");
		string? spanId = GetString(spanElement, "spanId");
		string? name = GetString(spanElement, "name");
		if(string.IsNullOrWhiteSpace(traceId) || string.IsNullOrWhiteSpace(spanId) || string.IsNullOrWhiteSpace(name))
		{
			return null;
		}

		DateTime startTime = ParseUnixNanoTimestamp(GetString(spanElement, "startTimeUnixNano"));
		DateTime endTime = ParseUnixNanoTimestamp(GetString(spanElement, "endTimeUnixNano"));
		if(endTime < startTime)
		{
			endTime = startTime;
		}

		(JsonElement statusElement, bool hasStatus) = spanElement.TryGetProperty("status", out JsonElement currentStatus)
			? (currentStatus, true)
			: (default, false);

		return new TelemetrySpan
		{
			TraceId = traceId,
			SpanId = spanId,
			ParentSpanId = NormalizeParentSpanId(GetString(spanElement, "parentSpanId")),
			Name = name,
			Kind = ParseSpanKind(GetInt32(spanElement, "kind")),
			StartTime = startTime,
			EndTime = endTime,
			Status = hasStatus ? ParseSpanStatus(GetInt32(statusElement, "code")) : SpanStatusEnum.Unset,
			StatusMessage = hasStatus ? GetString(statusElement, "message") : null,
			Attributes = ParseFlatAttributes(spanElement, "attributes"),
			Events = ParseOtlpEvents(spanElement)
		};
	}

	static TelemetrySummary CreateSummary(IEnumerable<TelemetryTrace> traces)
	{
		List<TelemetryTrace> traceList = [.. traces];
		if(traceList.Count == 0)
		{
			return new TelemetrySummary();
		}

		return new TelemetrySummary
		{
			TotalTraces = traceList.Count,
			TotalSpans = traceList.Sum(trace => trace.SpanCount),
			ErrorCount = traceList.Sum(trace => trace.Spans.Count(span => span.Status == SpanStatusEnum.Error)),
			StartTime = traceList.Min(trace => trace.StartTime),
			EndTime = traceList.Max(trace => trace.EndTime)
		};
	}

	static List<TelemetrySpanEvent> ParseOtlpEvents(JsonElement spanElement)
	{
		if(!spanElement.TryGetProperty("events", out JsonElement eventsElement) || eventsElement.ValueKind != JsonValueKind.Array)
		{
			return [];
		}

		List<TelemetrySpanEvent> events = [];
		foreach(JsonElement eventElement in eventsElement.EnumerateArray())
		{
			string? name = GetString(eventElement, "name");
			if(string.IsNullOrWhiteSpace(name))
			{
				continue;
			}

			events.Add(new TelemetrySpanEvent
			{
				Name = name,
				Timestamp = ParseUnixNanoTimestamp(GetString(eventElement, "timeUnixNano")),
				Attributes = ParseFlatAttributes(eventElement, "attributes")
			});
		}

		return events;
	}

	static Dictionary<string, string> ParseOtlpAttributes(JsonElement attributesElement)
	{
		if(attributesElement.ValueKind != JsonValueKind.Array)
		{
			return [];
		}

		Dictionary<string, string> attributes = new(StringComparer.Ordinal);
		foreach(JsonElement attribute in attributesElement.EnumerateArray())
		{
			string? key = GetString(attribute, "key");
			if(string.IsNullOrWhiteSpace(key))
			{
				continue;
			}

			if(!attribute.TryGetProperty("value", out JsonElement valueElement) || valueElement.ValueKind != JsonValueKind.Object)
			{
				attributes[key] = string.Empty;
				continue;
			}

			attributes[key] = GetAttributeValue(valueElement);
		}

		return attributes;
	}

	static string GetAttributeValue(JsonElement valueElement)
	{
		string? stringValue = GetString(valueElement, "stringValue");
		if(stringValue is not null)
		{
			return stringValue;
		}

		string? intValue = GetString(valueElement, "intValue");
		if(intValue is not null)
		{
			return intValue;
		}

		string? boolValue = GetString(valueElement, "boolValue");
		if(boolValue is not null)
		{
			return boolValue;
		}

		string? doubleValue = GetString(valueElement, "doubleValue");
		if(doubleValue is not null)
		{
			return doubleValue;
		}

		return string.Empty;
	}

	static string? GetString(JsonElement element, string propertyName)
	{
		if(!element.TryGetProperty(propertyName, out JsonElement property))
		{
			return null;
		}

		return property.ValueKind switch
		{
			JsonValueKind.String => property.GetString(),
			JsonValueKind.Number => property.GetRawText(),
			JsonValueKind.True => bool.TrueString,
			JsonValueKind.False => bool.FalseString,
			_ => null
		};
	}

	static int GetInt32(JsonElement element, string propertyName)
	{
		if(!element.TryGetProperty(propertyName, out JsonElement property))
		{
			return 0;
		}

		if(property.ValueKind == JsonValueKind.Number && property.TryGetInt32(out int intValue))
		{
			return intValue;
		}

		if(property.ValueKind == JsonValueKind.String && int.TryParse(property.GetString(), out intValue))
		{
			return intValue;
		}

		return 0;
	}

	static DateTime ParseUnixNanoTimestamp(string? nanosText)
	{
		if(!long.TryParse(nanosText, out long nanos))
		{
			return DateTime.MinValue;
		}

		try
		{
			return DateTimeOffset.FromUnixTimeMilliseconds(nanos / 1_000_000).LocalDateTime;
		}
		catch(ArgumentOutOfRangeException)
		{
			return DateTime.MinValue;
		}
	}

	static SpanKindEnum ParseSpanKind(int kind) => kind switch
	{
		2 => SpanKindEnum.Server,
		3 => SpanKindEnum.Client,
		4 => SpanKindEnum.Producer,
		5 => SpanKindEnum.Consumer,
		_ => SpanKindEnum.Internal
	};

	static SpanStatusEnum ParseSpanStatus(int statusCode) => statusCode switch
	{
		1 => SpanStatusEnum.Ok,
		2 => SpanStatusEnum.Error,
		_ => SpanStatusEnum.Unset
	};

	static string? NormalizeParentSpanId(string? parentSpanId) => string.IsNullOrWhiteSpace(parentSpanId) ? null : parentSpanId;

	static DateTime? TryParseFileDate(string fileName)
	{
		string nameWithoutExtension = Path.GetFileNameWithoutExtension(fileName);
		const string prefix = "otel-";
		if(!nameWithoutExtension.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
		{
			return null;
		}

		string dateText = nameWithoutExtension[prefix.Length..];
		return DateTime.TryParseExact(dateText, "yyyyMMdd", null, System.Globalization.DateTimeStyles.AssumeLocal, out DateTime date)
			? date
			: null;
	}

	static TelemetryTrace CloneTrace(TelemetryTrace trace) => new()
	{
		TraceId = trace.TraceId,
		Spans = [.. trace.Spans.Select(CloneSpan)]
	};

	static TelemetrySpan CloneSpan(TelemetrySpan span) => new()
	{
		TraceId = span.TraceId,
		SpanId = span.SpanId,
		ParentSpanId = span.ParentSpanId,
		Name = span.Name,
		Kind = span.Kind,
		StartTime = span.StartTime,
		EndTime = span.EndTime,
		Status = span.Status,
		StatusMessage = span.StatusMessage,
		Attributes = new Dictionary<string, string>(span.Attributes, StringComparer.Ordinal),
		Events = [.. span.Events.Select(CloneSpanEvent)]
	};

	static TelemetrySpanEvent CloneSpanEvent(TelemetrySpanEvent spanEvent) => new()
	{
		Name = spanEvent.Name,
		Timestamp = spanEvent.Timestamp,
		Attributes = new Dictionary<string, string>(spanEvent.Attributes, StringComparer.Ordinal)
	};
}
