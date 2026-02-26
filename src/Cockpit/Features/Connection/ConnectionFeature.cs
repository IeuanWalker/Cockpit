using System.Text.Json;
using Cockpit.Features.Sdk;
using GitHub.Copilot.SDK;
using Microsoft.Extensions.Logging;

namespace Cockpit.Features.Connection;

/// <summary>
/// Periodically pings the Copilot backend and exposes connection health state.
/// </summary>
public sealed partial class ConnectionFeature : IDisposable
{
	static string SerializeExceptionToJson(Exception ex)
	{
		return JsonSerializer.Serialize(new
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
		}, new JsonSerializerOptions { WriteIndented = true });
	}
	readonly CopilotClientFeature _clientService;
	readonly ILogger<ConnectionFeature> _logger;
	Timer? _timer;
	bool _initialized = false;

	public event Action? OnStatusChanged;

	public ConnectionStatusEnum Status { get; private set; } = ConnectionStatusEnum.Unknown;
	public PingResponse? LastResponse { get; private set; }
	public DateTime? LastChecked { get; private set; }
	public IReadOnlyList<ConnectionCheckRecordModel> History => _history;

	readonly List<ConnectionCheckRecordModel> _history = [];

	public const int PollIntervalSeconds = 20;
	public const int MaxHistorySize = 100;

	public ConnectionFeature(CopilotClientFeature clientService, ILogger<ConnectionFeature> logger)
	{
		_clientService = clientService;
		_logger = logger;
	}

	/// <summary>
	/// Runs an initial ping and starts the polling timer. Idempotent — safe to call multiple times.
	/// </summary>
	public async Task Initialize()
	{
		if(_initialized)
		{
			return;
		}

		_initialized = true;

		await Ping();

		_timer = new Timer(async _ => await Ping(), null,
			TimeSpan.FromSeconds(PollIntervalSeconds),
			TimeSpan.FromSeconds(PollIntervalSeconds));
	}

	/// <summary>
	/// Executes a single ping, updates <see cref="Status"/> and fires <see cref="OnStatusChanged"/>.
	/// </summary>
	public async Task Ping()
	{
		try
		{
			Task<PingResponse?> pingTask = _clientService.PingAsync();
			Task delayTask = Task.Delay(100); //! Important: Avoids flickering status
			Task completedTask = await Task.WhenAny(pingTask, delayTask);
			if(completedTask == delayTask)
			{
				Status = ConnectionStatusEnum.Checking;
				OnStatusChanged?.Invoke();
			}

			LastResponse = await pingTask;
			LastChecked = DateTime.UtcNow;
			Status = LastResponse is not null ? ConnectionStatusEnum.Connected : ConnectionStatusEnum.Disconnected;

			AddToHistory(Status, LastChecked.Value, GetResponseJson());
		}
		catch(Exception ex)
		{
			LastChecked = DateTime.UtcNow;
			Status = ConnectionStatusEnum.Error;

			AddToHistory(Status, LastChecked.Value, SerializeExceptionToJson(ex));
		}

		_logger.LogDebug("Ping result: {Status}", Status);
		OnStatusChanged?.Invoke();

		void AddToHistory(ConnectionStatusEnum status, DateTime lastChecked, string jsonResult)
		{
			if(_history.Count >= MaxHistorySize)
			{
				_history.RemoveAt(0);
			}
			_history.Add(new ConnectionCheckRecordModel()
			{
				Status = status,
				CheckedAt = lastChecked,
				ResponseJson = jsonResult
			});
		}
	}

	/// <summary>Returns the last ping response serialized as indented JSON.</summary>
	public string GetResponseJson()
	{
		if(LastResponse is null)
		{
			return JsonSerializer.Serialize(
				new { status = "unreachable", timestamp = DateTime.UtcNow },
				new JsonSerializerOptions { WriteIndented = true });
		}

		try
		{
			return JsonSerializer.Serialize(LastResponse, new JsonSerializerOptions { WriteIndented = true });
		}
		catch(Exception ex)
		{
			_logger.LogDebug(ex, "Failed to serialize ping response");
			return "{}";
		}
	}

	public void Dispose()
	{
		_timer?.Dispose();
		GC.SuppressFinalize(this);
	}
}
