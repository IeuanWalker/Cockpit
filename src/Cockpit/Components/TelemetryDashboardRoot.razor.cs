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
	CancellationTokenSource? _autoRefreshCts;
	bool _loadingFiles = true;
	bool _loadingData = true;
	bool _autoRefresh = true;
	string _spanSearch = string.Empty;
	string _loadError = string.Empty;
	string? _selectedFilePath;
	string? _selectedSessionId;
	TelemetryDashboardTab _activeTab = TelemetryDashboardTab.Traces;

	List<TelemetryFileInfo> _files = [];
	List<TelemetryTrace> _traces = [];
	List<TelemetryTrace> _filteredTraces = [];
	List<TelemetrySpan> _allSpans = [];
	List<TelemetrySpan> _sessionFilteredSpans = [];
	List<TelemetrySpan> _filteredSpans = [];
	List<TelemetrySessionInfo> _sessions = [];
	List<TelemetryTimelineRow> _timelineRows = [];
	TelemetryTrace? _selectedTrace;
	TelemetrySpan? _selectedSpan;

	readonly string _spansBodyId = $"td-spans-{Guid.NewGuid():N}";
	readonly string _timelineBodyId = $"td-timeline-{Guid.NewGuid():N}";
	bool _autoScroll = true;
	bool _pendingScroll;
	bool _isScrolledUp;
	bool _scrollSetup;
	DotNetObjectReference<TelemetryDashboardRoot>? _dotNetRef;

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
		StartAutoRefresh();
	}

	protected override async Task OnAfterRenderAsync(bool firstRender)
	{
		if(firstRender)
		{
			_dotNetRef = DotNetObjectReference.Create(this);
			await ApplyThemeAsync();
			_splashFeature.NotifyBlazorReady();
		}

		if(!_scrollSetup && !_loadingData && (_activeTab == TelemetryDashboardTab.Spans || _activeTab == TelemetryDashboardTab.Timeline))
		{
			_scrollSetup = true;
			string scrollElementId = _activeTab == TelemetryDashboardTab.Spans ? _spansBodyId : _timelineBodyId;
			try
			{
				await _jsRuntime.InvokeVoidAsync("cockpit.setupLogViewerScroll", scrollElementId, _dotNetRef, nameof(OnScrollPositionChanged));
			}
			catch { /* best-effort */ }
		}

		if(_pendingScroll && _autoScroll && !_isScrolledUp)
		{
			_pendingScroll = false;
			string? scrollElementId = _activeTab switch
			{
				TelemetryDashboardTab.Spans => _spansBodyId,
				TelemetryDashboardTab.Timeline => _timelineBodyId,
				_ => null,
			};
			if(scrollElementId is not null)
			{
				try
				{
					await _jsRuntime.InvokeVoidAsync("cockpit.scrollToBottom", scrollElementId);
				}
				catch { /* best-effort */ }
			}
		}
	}

	[JSInvokable]
	public void OnScrollPositionChanged(bool isNearBottom)
	{
		_isScrolledUp = !isNearBottom;
		InvokeAsync(StateHasChanged);
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

		_traces = [..
			readTracesTask.Result
				.OrderByDescending(trace => ToDateTimeOffset(trace.StartTime) ?? DateTimeOffset.MinValue)
				.ThenBy(trace => trace.TraceId)];

		_allSpans = [..
			readSpansTask.Result
				.OrderByDescending(span => ToDateTimeOffset(span.StartTime) ?? DateTimeOffset.MinValue)
				.ThenBy(span => span.TraceId)
				.ThenBy(span => span.SpanId)];

		_sessions = [..
			_allSpans
				.Where(span => span.Attributes.TryGetValue("gen_ai.conversation.id", out string? sid) && !string.IsNullOrWhiteSpace(sid))
				.GroupBy(span => span.Attributes["gen_ai.conversation.id"], StringComparer.Ordinal)
				.Select(group => new TelemetrySessionInfo(
					SessionId: group.Key,
					StartTime: group.Min(s => s.StartTime),
					EndTime: group.Max(s => s.EndTime)))
				.OrderBy(s => s.StartTime)];

		if(_selectedSessionId is not null && !_sessions.Any(s => StringComparer.Ordinal.Equals(s.SessionId, _selectedSessionId)))
		{
			_selectedSessionId = null;
		}

		ApplySessionFilter();
	}

	void ClearData()
	{
		_traces = [];
		_filteredTraces = [];
		_allSpans = [];
		_sessionFilteredSpans = [];
		_filteredSpans = [];
		_sessions = [];
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
		_scrollSetup = false;
		if(_autoScroll && (_activeTab == TelemetryDashboardTab.Spans || _activeTab == TelemetryDashboardTab.Timeline))
		{
			_pendingScroll = true;
		}

		_ = InvokeAsync(StateHasChanged);
	}

	void ToggleAutoRefresh()
	{
		_autoRefresh = !_autoRefresh;
		if(_autoRefresh)
		{
			StartAutoRefresh();
		}
		else
		{
			StopAutoRefresh();
		}
	}

	void StartAutoRefresh()
	{
		StopAutoRefresh();
		CancellationTokenSource cts = new();
		_autoRefreshCts = cts;
		_ = AutoRefreshLoopAsync(cts.Token);
	}

	void StopAutoRefresh()
	{
		CancellationTokenSource? previous = Interlocked.Exchange(ref _autoRefreshCts, null);
		previous?.Cancel();
		previous?.Dispose();
	}

	async Task AutoRefreshLoopAsync(CancellationToken ct)
	{
		using PeriodicTimer timer = new(TimeSpan.FromSeconds(3));
		try
		{
			while(await timer.WaitForNextTickAsync(ct))
			{
				if(_loadingFiles || _loadingData)
				{
					continue;
				}

				await SilentReloadAsync(ct);
			}
		}
		catch(OperationCanceledException)
		{
		}
	}

	/// <summary>
	/// Reloads the current file without showing loading spinners.
	/// The TelemetryFileService cache checks file size + last write time,
	/// so unchanged files return instantly with no re-parse.
	/// </summary>
	async Task SilentReloadAsync(CancellationToken ct)
	{
		TelemetryFileInfo? selectedFile = ResolveSelectedFile(_selectedFilePath);
		if(selectedFile is null)
		{
			return;
		}

		try
		{
			Task<List<TelemetryTrace>> readTracesTask = _telemetryFileService.ReadTracesAsync(selectedFile.FullPath, ct);
			Task<List<TelemetrySpan>> readSpansTask = _telemetryFileService.ReadAllSpansAsync(selectedFile.FullPath, ct);
			await Task.WhenAll(readTracesTask, readSpansTask);

			if(ct.IsCancellationRequested)
			{
				return;
			}

			List<TelemetryTrace> newTraces = readTracesTask.Result;
			List<TelemetrySpan> newSpans = readSpansTask.Result;

			// Skip UI update if data hasn't changed
			if(newTraces.Count == _traces.Count && newSpans.Count == _allSpans.Count)
			{
				return;
			}

			string? previousTraceId = _selectedTrace?.TraceId;
			(string traceId, string spanId)? previousSpan = _selectedSpan is null ? null : (_selectedSpan.TraceId, _selectedSpan.SpanId);

			_traces = [..
				newTraces
					.OrderByDescending(trace => ToDateTimeOffset(trace.StartTime) ?? DateTimeOffset.MinValue)
					.ThenBy(trace => trace.TraceId)];

			_allSpans = [..
				newSpans
					.OrderByDescending(span => ToDateTimeOffset(span.StartTime) ?? DateTimeOffset.MinValue)
					.ThenBy(span => span.TraceId)
					.ThenBy(span => span.SpanId)];

			_sessions = [..
				_allSpans
					.Where(span => span.Attributes.TryGetValue("gen_ai.conversation.id", out string? sid) && !string.IsNullOrWhiteSpace(sid))
					.GroupBy(span => span.Attributes["gen_ai.conversation.id"], StringComparer.Ordinal)
					.Select(group => new TelemetrySessionInfo(
						SessionId: group.Key,
						StartTime: group.Min(s => s.StartTime),
						EndTime: group.Max(s => s.EndTime)))
					.OrderBy(s => s.StartTime)];

			if(_selectedSessionId is not null && !_sessions.Any(s => StringComparer.Ordinal.Equals(s.SessionId, _selectedSessionId)))
			{
				_selectedSessionId = null;
			}

			_selectedTrace = previousTraceId is null
				? _traces.FirstOrDefault()
				: _traces.FirstOrDefault(trace => trace.TraceId == previousTraceId) ?? _traces.FirstOrDefault();

			_selectedSpan = previousSpan is null
				? null
				: _allSpans.FirstOrDefault(span => span.TraceId == previousSpan.Value.traceId && span.SpanId == previousSpan.Value.spanId);

			ApplySessionFilter();

			if(_autoScroll && !_isScrolledUp && (_activeTab == TelemetryDashboardTab.Spans || _activeTab == TelemetryDashboardTab.Timeline))
			{
				_pendingScroll = true;
			}

			await InvokeAsync(StateHasChanged);
		}
		catch(OperationCanceledException)
		{
		}
		catch
		{
			// Silently ignore errors during auto-refresh
		}
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

	void SetActiveTab(TelemetryDashboardTab tab)
	{
		TelemetryDashboardTab previousTab = _activeTab;
		_activeTab = tab;

		if(previousTab == TelemetryDashboardTab.Spans || previousTab == TelemetryDashboardTab.Timeline)
		{
			string elementId = previousTab == TelemetryDashboardTab.Spans ? _spansBodyId : _timelineBodyId;
			_ = CleanupScrollAsync(elementId);
		}

		_isScrolledUp = false;
		_scrollSetup = false;
		if(tab == TelemetryDashboardTab.Spans || tab == TelemetryDashboardTab.Timeline)
		{
			_pendingScroll = _autoScroll;
		}
	}

	void SwitchTab(TelemetryDashboardTab tab)
	{
		if(tab == TelemetryDashboardTab.Timeline && _selectedTrace is null)
		{
			return;
		}

		SetActiveTab(tab);
		_selectedSpan = null;
	}

	void SelectTrace(TelemetryTrace trace)
	{
		_selectedTrace = trace;
		UpdateTimelineRows();

		// If the span detail panel is open on the Traces tab (via "Inspect Root Span"),
		// update it to reflect the newly selected trace's root span.
		if(_activeTab == TelemetryDashboardTab.Traces && _selectedSpan is not null)
		{
			_selectedSpan = GetRootSpan(trace);
		}
	}

	void OpenTimelineForSelectedTrace()
	{
		if(_selectedTrace is null)
		{
			return;
		}

		SetActiveTab(TelemetryDashboardTab.Timeline);
		UpdateTimelineRows();
	}

	void SelectSpan(TelemetrySpan span)
	{
		_selectedSpan = _selectedSpan?.TraceId == span.TraceId && _selectedSpan.SpanId == span.SpanId ? null : span;
		_selectedTrace = _filteredTraces.FirstOrDefault(trace => trace.TraceId == span.TraceId) ?? _selectedTrace;
		UpdateTimelineRows();
	}

	void CloseSelectedSpan()
	{
		_selectedSpan = null;
	}

	void OnSessionFilterChanged(ChangeEventArgs args)
	{
		string? sessionId = args.Value?.ToString();
		_selectedSessionId = string.IsNullOrWhiteSpace(sessionId) ? null : sessionId;
		ApplySessionFilter();
	}

	void OnSpanSearchChanged(ChangeEventArgs args)
	{
		_spanSearch = args.Value?.ToString() ?? string.Empty;
		ApplySpanFilter();
	}

	void ApplySessionFilter()
	{
		string? previousTraceId = _selectedTrace?.TraceId;
		(string traceId, string spanId)? previousSpan = _selectedSpan is null ? null : (_selectedSpan.TraceId, _selectedSpan.SpanId);

		if(_selectedSessionId is null)
		{
			_filteredTraces = _traces;
			_sessionFilteredSpans = _allSpans;
		}
		else
		{
			HashSet<string> matchingTraceIds = new(
				_allSpans
					.Where(span => span.Attributes.TryGetValue("gen_ai.conversation.id", out string? sessionId)
						&& StringComparer.Ordinal.Equals(sessionId, _selectedSessionId))
					.Select(span => span.TraceId),
				StringComparer.Ordinal);

			_filteredTraces = [..
				_traces.Where(trace => matchingTraceIds.Contains(trace.TraceId))];

			_sessionFilteredSpans = [..
				_allSpans.Where(span => matchingTraceIds.Contains(span.TraceId))];
		}

		ApplySpanFilter();

		_selectedTrace = previousTraceId is null
			? _filteredTraces.FirstOrDefault()
			: _filteredTraces.FirstOrDefault(trace => trace.TraceId == previousTraceId) ?? _filteredTraces.FirstOrDefault();

		_selectedSpan = previousSpan is null
			? null
			: _sessionFilteredSpans.FirstOrDefault(span => span.TraceId == previousSpan.Value.traceId && span.SpanId == previousSpan.Value.spanId);

		UpdateTimelineRows();
	}

	void ApplySpanFilter()
	{
		string search = _spanSearch.Trim();
		IEnumerable<TelemetrySpan> spans = _sessionFilteredSpans;

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

	List<(string ToolName, int Count)> GetToolUsageRanking()
	{
		return [.. _sessionFilteredSpans
			.Where(span => span.Attributes.TryGetValue("gen_ai.operation.name", out string? operationName) && operationName == "execute_tool")
			.Select(span => span.Attributes.TryGetValue("gen_ai.tool.name", out string? toolName) ? toolName : "(unknown)")
			.GroupBy(toolName => toolName)
			.Select(group => (ToolName: group.Key, Count: group.Count()))
			.OrderByDescending(item => item.Count)];
	}

	List<(string Model, int Calls, long InputTokens, long OutputTokens, long ReasoningTokens, long CacheReadTokens)> GetModelBreakdown()
	{
		return [.. _sessionFilteredSpans
			.Where(span => span.Attributes.TryGetValue("gen_ai.operation.name", out string? operationName) && operationName == "chat")
			.GroupBy(span => span.Attributes.TryGetValue("gen_ai.response.model", out string? model)
				? model
				: span.Attributes.TryGetValue("gen_ai.request.model", out string? requestModel)
					? requestModel
					: "(unknown)")
			.Select(group => (
				Model: group.Key,
				Calls: group.Count(),
				InputTokens: group.Sum(span => ParseLong(span.Attributes.GetValueOrDefault("gen_ai.usage.input_tokens"))),
				OutputTokens: group.Sum(span => ParseLong(span.Attributes.GetValueOrDefault("gen_ai.usage.output_tokens"))),
				ReasoningTokens: group.Sum(span => ParseLong(span.Attributes.GetValueOrDefault("gen_ai.usage.reasoning.output_tokens"))),
				CacheReadTokens: group.Sum(span => ParseLong(span.Attributes.GetValueOrDefault("gen_ai.usage.cache_read.input_tokens")))))
			.OrderByDescending(item => item.Calls)];
	}

	(long TotalInputTokens, long TotalOutputTokens, long TotalReasoningTokens, long TotalCacheReadTokens, long TotalCost, int TotalErrors, int TotalTurns) GetAggregateStats()
	{
		List<TelemetrySpan> chatSpans = [..
			_sessionFilteredSpans
				.Where(span => span.Attributes.TryGetValue("gen_ai.operation.name", out string? operationName) && operationName == "chat")];

		long inputTokens = chatSpans.Sum(span => ParseLong(span.Attributes.GetValueOrDefault("gen_ai.usage.input_tokens")));
		long outputTokens = chatSpans.Sum(span => ParseLong(span.Attributes.GetValueOrDefault("gen_ai.usage.output_tokens")));
		long reasoningTokens = chatSpans.Sum(span => ParseLong(span.Attributes.GetValueOrDefault("gen_ai.usage.reasoning.output_tokens")));
		long cacheReadTokens = chatSpans.Sum(span => ParseLong(span.Attributes.GetValueOrDefault("gen_ai.usage.cache_read.input_tokens")));
		long totalCost = chatSpans.Sum(span => ParseLong(span.Attributes.GetValueOrDefault("github.copilot.cost")));
		int errors = _sessionFilteredSpans.Count(span => span.Status == SpanStatusEnum.Error);
		int turns = chatSpans
			.Select(span => span.Attributes.GetValueOrDefault("github.copilot.turn_id"))
			.Where(turn => turn is not null)
			.Distinct()
			.Count();

		return (inputTokens, outputTokens, reasoningTokens, cacheReadTokens, totalCost, errors, turns);
	}

	static long ParseLong(string? value) => long.TryParse(value, out long result) ? result : 0;

	static string FormatTokenCount(long tokens)
	{
		if(tokens >= 1_000_000)
		{
			return $"{tokens / 1_000_000d:0.##}M";
		}

		if(tokens >= 1_000)
		{
			return $"{tokens / 1_000d:0.#}K";
		}

		return tokens.ToString("N0");
	}

	void OpenFolder()
	{
		string directory = _telemetryFileService.TelemetryDirectory;
		Directory.CreateDirectory(directory);
		FileUtil.RevealFolder(directory);
	}

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

	static string FormatSessionOption(TelemetrySessionInfo session)
	{
		string id = ShortId(session.SessionId);
		string start = session.StartTime == DateTime.MinValue ? "–" : session.StartTime.ToString("d MMM HH:mm");
		string end = session.EndTime == DateTime.MinValue ? "–" : session.EndTime.ToString("HH:mm");
		return $"{id} · {start} – {end}";
	}

	static bool Contains(string? value, string search)
	{
		return !string.IsNullOrWhiteSpace(value) && value.Contains(search, StringComparison.OrdinalIgnoreCase);
	}

	async Task CleanupScrollAsync(string elementId)
	{
		try
		{
			await _jsRuntime.InvokeVoidAsync("cockpit.cleanupLogViewerScroll", elementId);
		}
		catch { /* best-effort */ }
	}

	async Task ScrollToBottomAndResume()
	{
		_isScrolledUp = false;
		string? elementId = _activeTab switch
		{
			TelemetryDashboardTab.Spans => _spansBodyId,
			TelemetryDashboardTab.Timeline => _timelineBodyId,
			_ => null,
		};
		if(elementId is not null)
		{
			try
			{
				await _jsRuntime.InvokeVoidAsync("cockpit.scrollToBottom", elementId);
			}
			catch { /* best-effort */ }
		}
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

	public async ValueTask DisposeAsync()
	{
		_themeStateFeature.OnThemeChanged -= OnThemeChangedHandler;
		StopAutoRefresh();
		CancellationTokenSource? cts = Interlocked.Exchange(ref _loadCts, null);
		cts?.Cancel();
		cts?.Dispose();
		await CleanupScrollAsync(_spansBodyId);
		await CleanupScrollAsync(_timelineBodyId);
		_dotNetRef?.Dispose();
		_dotNetRef = null;
	}
}

enum TelemetryDashboardTab
{
	Traces,
	Spans,
	Timeline,
	Stats,
}

record TelemetrySessionInfo(string SessionId, DateTime StartTime, DateTime EndTime);

sealed record TelemetryTimelineRow(TelemetrySpan Span, int Depth, double LeftPercent, double WidthPercent);
