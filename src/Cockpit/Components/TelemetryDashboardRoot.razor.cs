using Cockpit.Features.Splash;
using Cockpit.Features.Telemetry;
using Cockpit.Features.Telemetry.Models;
using Cockpit.Features.Theme;
using Cockpit.Utilities;
using Microsoft.AspNetCore.Components;
using Microsoft.JSInterop;

namespace Cockpit.Components;

public sealed partial class TelemetryDashboardRoot : ComponentBase, IAsyncDisposable
{
	readonly IJSRuntime _jsRuntime;
	readonly ThemeStateFeature _themeStateFeature;
	readonly TelemetryDashboardSplashFeature _splashFeature;
	readonly TelemetryFileService _telemetryFileService;

	CancellationTokenSource? _loadCts;
	bool _loadingFiles = true;
	bool _loadingData = true;
	string _spanSearch = string.Empty;
	string _loadError = string.Empty;
	string? _selectedFilePath;
	TelemetryDashboardTab _activeTab = TelemetryDashboardTab.Traces;

	List<TelemetryFileInfo> _files = [];
	List<TelemetryTrace> _traces = [];
	List<TelemetrySpan> _allSpans = [];
	List<TelemetrySpan> _filteredSpans = [];
	List<TelemetryTimelineRow> _timelineRows = [];
	TelemetryTrace? _selectedTrace;
	TelemetrySpan? _selectedSpan;

	public TelemetryDashboardRoot(
		IJSRuntime jsRuntime,
		ThemeStateFeature themeStateFeature,
		TelemetryDashboardSplashFeature splashFeature,
		IServiceProvider serviceProvider)
	{
		_jsRuntime = jsRuntime;
		_themeStateFeature = themeStateFeature;
		_splashFeature = splashFeature;
		_telemetryFileService = serviceProvider.GetRequiredService<TelemetryFileService>();
	}

	TelemetryFileInfo? CurrentFile => _selectedFilePath is null
		? null
		: _files.FirstOrDefault(file => StringComparer.OrdinalIgnoreCase.Equals(file.FullPath, _selectedFilePath));

	protected override void OnInitialized()
	{
		_themeStateFeature.OnThemeChanged += OnThemeChangedHandler;
		_ = RefreshAsync();
	}

	protected override async Task OnAfterRenderAsync(bool firstRender)
	{
		if(firstRender)
		{
			await ApplyThemeAsync();
			_splashFeature.NotifyBlazorReady();
		}
	}

	async Task RefreshAsync()
	{
		CancellationTokenSource cts = BeginLoad();
		CancellationToken ct = cts.Token;
		_loadingFiles = true;
		_loadingData = true;
		_loadError = string.Empty;
		await InvokeAsync(StateHasChanged);

		try
		{
			List<TelemetryFileInfo> files = await Task.Run(() =>
			{
				return _telemetryFileService
					.GetAvailableFiles()
					.OrderByDescending(file => ToDateTimeOffset(file.Date) ?? DateTimeOffset.MinValue)
					.ThenByDescending(file => file.FileName)
					.ToList();
			}, ct);

			if(ct.IsCancellationRequested)
			{
				return;
			}

			_files = files;
			TelemetryFileInfo? selectedFile = ResolveSelectedFile(_selectedFilePath);
			_selectedFilePath = selectedFile?.FullPath;
			await LoadSelectedFileCoreAsync(selectedFile, ct);
		}
		catch(OperationCanceledException) when(ct.IsCancellationRequested)
		{
		}
		catch(Exception ex)
		{
			if(!ct.IsCancellationRequested)
			{
				_loadError = $"Unable to refresh telemetry files: {ex.Message}";
				ClearData();
			}
		}
		finally
		{
			CompleteLoad(cts);
		}
	}

	async Task OnSelectedFileChanged(ChangeEventArgs args)
	{
		string? filePath = args.Value?.ToString();
		_selectedFilePath = string.IsNullOrWhiteSpace(filePath) ? null : filePath;

		CancellationTokenSource cts = BeginLoad();
		CancellationToken ct = cts.Token;
		_loadingData = true;
		_loadError = string.Empty;
		await InvokeAsync(StateHasChanged);

		try
		{
			TelemetryFileInfo? selectedFile = ResolveSelectedFile(_selectedFilePath);
			_selectedFilePath = selectedFile?.FullPath;
			await LoadSelectedFileCoreAsync(selectedFile, ct);
		}
		catch(OperationCanceledException) when(ct.IsCancellationRequested)
		{
		}
		catch(Exception ex)
		{
			if(!ct.IsCancellationRequested)
			{
				_loadError = $"Unable to load telemetry file: {ex.Message}";
				ClearData();
			}
		}
		finally
		{
			CompleteLoad(cts);
		}
	}

	async Task LoadSelectedFileCoreAsync(TelemetryFileInfo? selectedFile, CancellationToken ct)
	{
		if(selectedFile is null)
		{
			ClearData();
			return;
		}

		Task<List<TelemetryTrace>> readTracesTask = _telemetryFileService.ReadTracesAsync(selectedFile.FullPath, ct);
		Task<List<TelemetrySpan>> readSpansTask = _telemetryFileService.ReadAllSpansAsync(selectedFile.FullPath, ct);
		await Task.WhenAll(readTracesTask, readSpansTask);

		if(ct.IsCancellationRequested)
		{
			return;
		}

		string? previousTraceId = _selectedTrace?.TraceId;
		(string traceId, string spanId)? previousSpan = _selectedSpan is null ? null : (_selectedSpan.TraceId, _selectedSpan.SpanId);

		_traces = [..
			readTracesTask.Result
				.OrderByDescending(trace => ToDateTimeOffset(trace.StartTime) ?? DateTimeOffset.MinValue)
				.ThenBy(trace => trace.TraceId)];

		_allSpans = [..
			readSpansTask.Result
				.OrderByDescending(span => ToDateTimeOffset(span.StartTime) ?? DateTimeOffset.MinValue)
				.ThenBy(span => span.TraceId)
				.ThenBy(span => span.SpanId)];

		_selectedTrace = previousTraceId is null
			? _traces.FirstOrDefault()
			: _traces.FirstOrDefault(trace => trace.TraceId == previousTraceId) ?? _traces.FirstOrDefault();

		_selectedSpan = previousSpan is null
			? null
			: _allSpans.FirstOrDefault(span => span.TraceId == previousSpan.Value.traceId && span.SpanId == previousSpan.Value.spanId);

		ApplySpanFilter();
		UpdateTimelineRows();
	}

	void ClearData()
	{
		_traces = [];
		_allSpans = [];
		_filteredSpans = [];
		_timelineRows = [];
		_selectedTrace = null;
		_selectedSpan = null;
	}

	CancellationTokenSource BeginLoad()
	{
		CancellationTokenSource next = new();
		CancellationTokenSource? previous = Interlocked.Exchange(ref _loadCts, next);
		previous?.Cancel();
		previous?.Dispose();
		return next;
	}

	void CompleteLoad(CancellationTokenSource cts)
	{
		if(!ReferenceEquals(_loadCts, cts))
		{
			cts.Dispose();
			return;
		}

		_loadingFiles = false;
		_loadingData = false;
		_ = InvokeAsync(StateHasChanged);
	}

	TelemetryFileInfo? ResolveSelectedFile(string? filePath)
	{
		if(!string.IsNullOrWhiteSpace(filePath))
		{
			TelemetryFileInfo? existing = _files.FirstOrDefault(file => StringComparer.OrdinalIgnoreCase.Equals(file.FullPath, filePath));
			if(existing is not null)
			{
				return existing;
			}
		}

		return _files.FirstOrDefault();
	}

	void SwitchTab(TelemetryDashboardTab tab)
	{
		if(tab == TelemetryDashboardTab.Timeline && _selectedTrace is null)
		{
			return;
		}

		_activeTab = tab;
	}

	void SelectTrace(TelemetryTrace trace)
	{
		_selectedTrace = trace;
		UpdateTimelineRows();
	}

	void OpenTimelineForSelectedTrace()
	{
		if(_selectedTrace is null)
		{
			return;
		}

		_activeTab = TelemetryDashboardTab.Timeline;
		UpdateTimelineRows();
	}

	void SelectSpan(TelemetrySpan span)
	{
		_selectedSpan = _selectedSpan?.TraceId == span.TraceId && _selectedSpan.SpanId == span.SpanId ? null : span;
		_selectedTrace = _traces.FirstOrDefault(trace => trace.TraceId == span.TraceId) ?? _selectedTrace;
		UpdateTimelineRows();
	}

	void CloseSelectedSpan()
	{
		_selectedSpan = null;
	}

	void OnSpanSearchChanged(ChangeEventArgs args)
	{
		_spanSearch = args.Value?.ToString() ?? string.Empty;
		ApplySpanFilter();
	}

	void ApplySpanFilter()
	{
		string search = _spanSearch.Trim();
		IEnumerable<TelemetrySpan> spans = _allSpans;

		if(search.Length > 0)
		{
			spans = spans.Where(span => SpanMatches(span, search));
		}

		_filteredSpans = [.. spans];
	}

	bool SpanMatches(TelemetrySpan span, string search)
	{
		if(Contains(span.Name, search)
			|| Contains(span.TraceId, search)
			|| Contains(span.SpanId, search)
			|| Contains(span.ParentSpanId, search)
			|| Contains(span.StatusMessage, search)
			|| Contains(span.Kind.ToString(), search)
			|| Contains(span.Status.ToString(), search))
		{
			return true;
		}

		return span.Attributes.Any(attribute => Contains(attribute.Key, search) || Contains(FormatValue(attribute.Value), search));
	}

	void UpdateTimelineRows()
	{
		_timelineRows = _selectedTrace is null ? [] : BuildTimelineRows(_selectedTrace);
	}

	List<TelemetryTimelineRow> BuildTimelineRows(TelemetryTrace trace)
	{
		List<TelemetryTimelineRow> rows = [];
		if(trace.Spans.Count == 0)
		{
			return rows;
		}

		List<TelemetrySpan> spans = [..
			trace.Spans
				.OrderBy(span => ToDateTimeOffset(span.StartTime) ?? DateTimeOffset.MinValue)
				.ThenBy(span => span.Name)
				.ThenBy(span => span.SpanId)];

		Dictionary<string, TelemetrySpan> spanLookup = spans
			.Where(span => !string.IsNullOrWhiteSpace(span.SpanId))
			.GroupBy(span => span.SpanId)
			.ToDictionary(group => group.Key, group => group.First(), StringComparer.Ordinal);

		ILookup<string?, TelemetrySpan> children = spans.ToLookup(span => string.IsNullOrWhiteSpace(span.ParentSpanId) ? null : span.ParentSpanId, StringComparer.Ordinal);
		HashSet<string> visited = [];
		DateTimeOffset traceStart = GetTraceStart(trace, spans);
		TimeSpan traceDuration = GetTraceDuration(trace, spans, traceStart);
		List<TelemetrySpan> roots = [..
			spans.Where(span => string.IsNullOrWhiteSpace(span.ParentSpanId) || !spanLookup.ContainsKey(span.ParentSpanId))];

		foreach(TelemetrySpan root in roots)
		{
			AppendTimelineRow(root, 0, rows, children, visited, traceStart, traceDuration);
		}

		foreach(TelemetrySpan span in spans)
		{
			if(string.IsNullOrWhiteSpace(span.SpanId) || !visited.Contains(span.SpanId))
			{
				AppendTimelineRow(span, 0, rows, children, visited, traceStart, traceDuration, includeCurrent: true);
			}
		}

		return rows;
	}

	void AppendTimelineRow(
		TelemetrySpan span,
		int depth,
		List<TelemetryTimelineRow> rows,
		ILookup<string?, TelemetrySpan> children,
		HashSet<string> visited,
		DateTimeOffset traceStart,
		TimeSpan traceDuration,
		bool includeCurrent = true)
	{
		if(!string.IsNullOrWhiteSpace(span.SpanId) && !visited.Add(span.SpanId) && includeCurrent)
		{
			return;
		}

		if(includeCurrent)
		{
			rows.Add(CreateTimelineRow(span, depth, traceStart, traceDuration));
		}

		foreach(TelemetrySpan child in children[span.SpanId]
			.OrderBy(item => ToDateTimeOffset(item.StartTime) ?? DateTimeOffset.MinValue)
			.ThenBy(item => item.Name)
			.ThenBy(item => item.SpanId))
		{
			AppendTimelineRow(child, depth + 1, rows, children, visited, traceStart, traceDuration);
		}
	}

	TelemetryTimelineRow CreateTimelineRow(TelemetrySpan span, int depth, DateTimeOffset traceStart, TimeSpan traceDuration)
	{
		DateTimeOffset spanStart = ToDateTimeOffset(span.StartTime) ?? traceStart;
		TimeSpan spanDuration = ToTimeSpan(span.Duration) ?? TimeSpan.Zero;
		double totalMs = Math.Max(traceDuration.TotalMilliseconds, 1);
		double left = Math.Clamp((spanStart - traceStart).TotalMilliseconds / totalMs * 100d, 0d, 99.65d);
		double maxWidth = Math.Max(100d - left, 0.35d);
		double width = Math.Clamp(spanDuration.TotalMilliseconds / totalMs * 100d, 0.35d, maxWidth);
		return new(span, depth, left, width);
	}

	DateTimeOffset GetTraceStart(TelemetryTrace trace, List<TelemetrySpan> spans)
	{
		return ToDateTimeOffset(trace.StartTime)
			?? spans.Select(span => ToDateTimeOffset(span.StartTime)).Where(time => time.HasValue).Select(time => time!.Value).DefaultIfEmpty(DateTimeOffset.MinValue).Min();
	}

	TimeSpan GetTraceDuration(TelemetryTrace trace, List<TelemetrySpan> spans, DateTimeOffset traceStart)
	{
		TimeSpan? duration = ToTimeSpan(trace.Duration);
		if(duration.HasValue && duration.Value > TimeSpan.Zero)
		{
			return duration.Value;
		}

		DateTimeOffset traceEnd = ToDateTimeOffset(trace.EndTime)
			?? spans.Select(span => ToDateTimeOffset(span.EndTime) ?? ((ToDateTimeOffset(span.StartTime) ?? traceStart) + (ToTimeSpan(span.Duration) ?? TimeSpan.Zero)))
			.DefaultIfEmpty(traceStart)
			.Max();

		TimeSpan computed = traceEnd - traceStart;
		return computed > TimeSpan.Zero ? computed : TimeSpan.FromMilliseconds(1);
	}

	TelemetrySpan? GetRootSpan(TelemetryTrace trace)
	{
		return trace.Spans.FirstOrDefault(span => string.IsNullOrWhiteSpace(span.ParentSpanId))
			?? trace.Spans.OrderBy(span => ToDateTimeOffset(span.StartTime) ?? DateTimeOffset.MinValue).FirstOrDefault();
	}

	void OpenFolder()
	{
		string directory = _telemetryFileService.TelemetryDirectory;
		Directory.CreateDirectory(directory);
		FileUtil.RevealFolder(directory);
	}

	string GetTraceStatusClass(TelemetryTrace trace) => trace.HasErrors ? "error" : trace.SpanCount > 0 ? "ok" : "unset";

	string GetTraceStatusLabel(TelemetryTrace trace) => trace.HasErrors ? "Error" : trace.SpanCount > 0 ? "Ok" : "Unset";

	static string GetStatusClass(object? status) => status?.ToString()?.Trim().ToLowerInvariant() switch
	{
		"ok" => "ok",
		"error" => "error",
		_ => "unset",
	};

	static string GetStatusLabel(object? status) => status?.ToString()?.Trim().ToLowerInvariant() switch
	{
		"ok" => "Ok",
		"error" => "Error",
		_ => "Unset",
	};

	static string ShortId(string? value)
	{
		if(string.IsNullOrWhiteSpace(value) || value.Length <= 12)
		{
			return value ?? string.Empty;
		}

		return $"{value[..8]}…{value[^4..]}";
	}

	static string DisplayText(string? value, string fallback)
	{
		return string.IsNullOrWhiteSpace(value) ? fallback : value;
	}

	static string FormatFileOption(TelemetryFileInfo file)
	{
		return $"{FormatTimestamp(file.Date)} · {file.FileName} · {FormatBytes(file.SizeBytes)}";
	}

	static string FormatTimestamp(object? value)
	{
		return ToDateTimeOffset(value)?.ToLocalTime().ToString("yyyy-MM-dd HH:mm:ss.fff") ?? "—";
	}

	static string FormatDuration(object? value)
	{
		TimeSpan? duration = ToTimeSpan(value);
		if(duration is null)
		{
			return "—";
		}

		double totalMs = duration.Value.TotalMilliseconds;
		if(totalMs < 1)
		{
			return $"{totalMs:0.###} ms";
		}

		if(totalMs < 1000)
		{
			return $"{totalMs:0.##} ms";
		}

		if(totalMs < 60000)
		{
			return $"{duration.Value.TotalSeconds:0.###} s";
		}

		return duration.Value.ToString(@"hh\:mm\:ss\.fff");
	}

	static string FormatBytes(long sizeBytes)
	{
		double value = sizeBytes;
		string[] units = ["B", "KB", "MB", "GB"];
		int unit = 0;
		while(value >= 1024 && unit < units.Length - 1)
		{
			value /= 1024;
			unit++;
		}

		return $"{value:0.##} {units[unit]}";
	}

	static string FormatValue(object? value)
	{
		if(value is null)
		{
			return string.Empty;
		}

		return value switch
		{
			DateTime or DateTimeOffset => FormatTimestamp(value),
			TimeSpan => FormatDuration(value),
			_ => value.ToString() ?? string.Empty,
		};
	}

	string GetTimelineBarStyle(TelemetryTimelineRow row)
	{
		return $"left:{row.LeftPercent:0.###}%;width:{row.WidthPercent:0.###}%;";
	}

	string GetTimelineBarTitle(TelemetrySpan span)
	{
		return $"{DisplayText(span.Name, "(unnamed span)")} • {FormatDuration(span.Duration)} • {GetStatusLabel(span.Status)}";
	}

	static bool Contains(string? value, string search)
	{
		return !string.IsNullOrWhiteSpace(value) && value.Contains(search, StringComparison.OrdinalIgnoreCase);
	}

	void OnThemeChangedHandler()
	{
		_ = ApplyThemeAsync();
	}

	async Task ApplyThemeAsync()
	{
		try
		{
			if(_themeStateFeature.IsLightTheme)
			{
				await _jsRuntime.InvokeVoidAsync("cockpit.addBodyClass", "light-theme");
			}
			else
			{
				await _jsRuntime.InvokeVoidAsync("cockpit.removeBodyClass", "light-theme");
			}

			await _jsRuntime.InvokeVoidAsync("cockpit.setAccentColor", _themeStateFeature.AccentColor, _themeStateFeature.AccentHoverColor);
		}
		catch
		{
		}
	}

	static DateTimeOffset? ToDateTimeOffset(object? value)
	{
		return value switch
		{
			DateTimeOffset dto => dto,
			DateTime dt => dt.Kind == DateTimeKind.Unspecified ? new DateTimeOffset(DateTime.SpecifyKind(dt, DateTimeKind.Local)) : new DateTimeOffset(dt),
			DateOnly date => new DateTimeOffset(date.ToDateTime(TimeOnly.MinValue), TimeZoneInfo.Local.GetUtcOffset(date.ToDateTime(TimeOnly.MinValue))),
			string text when DateTimeOffset.TryParse(text, out DateTimeOffset parsed) => parsed,
			_ => null,
		};
	}

	static TimeSpan? ToTimeSpan(object? value)
	{
		return value switch
		{
			TimeSpan timeSpan => timeSpan,
			double milliseconds => TimeSpan.FromMilliseconds(milliseconds),
			long milliseconds => TimeSpan.FromMilliseconds(milliseconds),
			int milliseconds => TimeSpan.FromMilliseconds(milliseconds),
			string text when TimeSpan.TryParse(text, out TimeSpan parsed) => parsed,
			_ => null,
		};
	}

	public ValueTask DisposeAsync()
	{
		_themeStateFeature.OnThemeChanged -= OnThemeChangedHandler;
		CancellationTokenSource? cts = Interlocked.Exchange(ref _loadCts, null);
		cts?.Cancel();
		cts?.Dispose();
		return ValueTask.CompletedTask;
	}
}

enum TelemetryDashboardTab
{
	Traces,
	Spans,
	Timeline,
}

sealed record TelemetryTimelineRow(TelemetrySpan Span, int Depth, double LeftPercent, double WidthPercent);
