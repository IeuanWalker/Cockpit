using Cockpit.Extensions;
using Cockpit.Features.Sdk;
using GitHub.Copilot;
using Microsoft.Extensions.Logging;

namespace Cockpit.Features.Connection;

/// <summary>
/// Periodically pings the Copilot backend and exposes connection health state.
/// </summary>
public sealed partial class ConnectionFeature : IDisposable
{
	readonly ICopilotPingService _pingService;
	readonly ILogger<ConnectionFeature> _logger;
	readonly List<ConnectionCheckRecordModel> _history = [];
	readonly Lock _historyLock = new();
	readonly SemaphoreSlim _initializationLock = new(1, 1);

	bool _initialized;

	// 0 = idle, 1 = pinging — prevents overlapping concurrent ping executions.
	int _isPinging;

	Timer? _timer;

	public event Action? OnStatusChanged;

	public ConnectionStatusEnum Status { get; private set; } = ConnectionStatusEnum.Unknown;
	public PingResponse? LastResponse { get; private set; }
	public DateTime? LastChecked { get; private set; }
	public IReadOnlyList<ConnectionCheckRecordModel> History
	{
		get
		{
			lock(_historyLock)
			{
				return [.. _history];
			}
		}
	}

	public const int PollIntervalSeconds = 20;
	public const int MaxHistorySize = 100;

	public ConnectionFeature(ICopilotPingService pingService, ILogger<ConnectionFeature> logger)
	{
		_pingService = pingService;
		_logger = logger;

		_pingService.OnConnectionStateChanged += HandleClientConnectionStateChanged;
	}

	/// <summary>
	/// Runs an initial ping and starts the polling timer. Idempotent — safe to call multiple times.
	/// Concurrent callers queue behind the active initialization attempt; they return immediately
	/// once initialization succeeds. Failed or cancelled attempts allow a subsequent retry.
	/// </summary>
	public async Task Initialize(CancellationToken cancellationToken = default)
	{
		await _initializationLock.WaitAsync(CancellationToken.None);
		try
		{
			if(_initialized)
			{
				return;
			}

			await Ping(cancellationToken);

			if(cancellationToken.IsCancellationRequested)
			{
				return;
			}

			_timer = new Timer(OnTimerTick, null,
				TimeSpan.FromSeconds(PollIntervalSeconds),
				TimeSpan.FromSeconds(PollIntervalSeconds));

			_initialized = true;
		}
		finally
		{
			_initializationLock.Release();
		}
	}

	/// <summary>
	/// Executes a single ping, updates <see cref="Status"/> and fires <see cref="OnStatusChanged"/>.
	/// Non-reentrant: overlapping calls are skipped to prevent race conditions on shared state.
	/// </summary>
	public async Task Ping(CancellationToken cancellationToken = default)
	{
		if(Interlocked.CompareExchange(ref _isPinging, 1, 0) != 0)
		{
			return;
		}

		try
		{
			await PingCore(cancellationToken);
		}
		finally
		{
			Interlocked.Exchange(ref _isPinging, 0);
		}
	}

	async Task PingCore(CancellationToken cancellationToken)
	{
		ConnectionStatusEnum statusBeforePing = Status;
		try
		{
			Task<PingResponse?> pingTask = _pingService.PingAsync(cancellationToken);
			Task delayTask = Task.Delay(100, CancellationToken.None); //! Important: Avoids flickering status
			Task completedFirst = await Task.WhenAny(pingTask, delayTask);
			if(completedFirst == delayTask)
			{
				Status = ConnectionStatusEnum.Checking;
				OnStatusChanged?.Invoke();
			}

			LastResponse = await pingTask;
			LastChecked = DateTime.UtcNow;
			Status = LastResponse is not null ? ConnectionStatusEnum.Connected : ConnectionStatusEnum.Disconnected;

			AddToHistory(Status, LastChecked.Value, GetResponseJson());
		}
		catch(OperationCanceledException)
		{
			// If the Checking state was shown before cancellation, restore the previous status
			// so the UI is not permanently stuck displaying "Checking".
			if(Status == ConnectionStatusEnum.Checking)
			{
				Status = statusBeforePing;
				OnStatusChanged?.Invoke();
			}
			return;
		}
		catch(Exception ex)
		{
			LastChecked = DateTime.UtcNow;
			Status = ConnectionStatusEnum.Error;
			_logger.LogWarning(ex, "Ping failed");
			AddToHistory(Status, LastChecked.Value, SerializeExceptionToJson(ex));
		}

		_logger.LogDebug("Ping result: {Status}", Status);
		OnStatusChanged?.Invoke();
	}

	/// <summary>Returns the last ping response serialized as indented JSON.</summary>
	public string GetResponseJson()
	{
		if(LastResponse is null)
		{
			return new { status = "unreachable", timestamp = DateTime.UtcNow }.SerializeJson() ?? string.Empty;
		}

		try
		{
			return LastResponse.SerializeJson() ?? string.Empty;
		}
		catch(Exception ex)
		{
			_logger.LogDebug(ex, "Failed to serialize ping response");
			return "{}";
		}
	}

	public void Dispose()
	{
		_pingService.OnConnectionStateChanged -= HandleClientConnectionStateChanged;
		_timer?.Dispose();
		_initializationLock?.Dispose();
	}

	/// <summary>
	/// Reacts immediately to SDK client state changes so the banner updates without
	/// waiting for the next poll interval. On disconnect, switches to
	/// <see cref="ConnectionStatusEnum.Reconnecting"/>. On reconnect, triggers a fresh ping.
	/// </summary>
	void HandleClientConnectionStateChanged(ConnectionState state)
	{
		if(state == ConnectionState.Disconnected)
		{
			Status = ConnectionStatusEnum.Reconnecting;
			OnStatusChanged?.Invoke();
		}
		else
		{
			// Client is back — run a ping immediately so the banner confirms the connection
			// is healthy rather than waiting up to PollIntervalSeconds seconds.
			Ping().ContinueWith(
				t => _logger.LogError(t.Exception, "Ping after reconnect failed"),
				CancellationToken.None,
				TaskContinuationOptions.OnlyOnFaulted,
				TaskScheduler.Default);
		}
	}

	void OnTimerTick(object? _)
	{
		// Avoid async void — fire-and-forget with explicit error logging as a safety net.
		// Ping() already catches all exceptions internally so this path is rarely reached.
		Ping().ContinueWith(
			t => _logger.LogError(t.Exception, "Unhandled exception in connection polling timer"),
			CancellationToken.None,
			TaskContinuationOptions.OnlyOnFaulted,
			TaskScheduler.Default);
	}

	void AddToHistory(ConnectionStatusEnum status, DateTime checkedAt, string jsonResult)
	{
		lock(_historyLock)
		{
			if(_history.Count >= MaxHistorySize)
			{
				_history.RemoveAt(0);
			}

			_history.Add(new ConnectionCheckRecordModel
			{
				Status = status,
				CheckedAt = checkedAt,
				ResponseJson = jsonResult
			});
		}
	}

	static string SerializeExceptionToJson(Exception ex)
	{
		return (new
		{
			type = ex.GetType().FullName,
			message = ex.Message,
			stackTrace = ex.StackTrace,
			innerException = ex.InnerException != null ? new
			{
				type = ex.InnerException.GetType().FullName,
				message = ex.InnerException.Message,
				stackTrace = ex.InnerException.StackTrace
			} : null
		}).SerializeJson() ?? string.Empty;
	}
}
