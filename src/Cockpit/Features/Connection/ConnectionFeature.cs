using System.Text.Json;
using Cockpit.Services;
using GitHub.Copilot.SDK;
using Microsoft.Extensions.Logging;

namespace Cockpit.Features.Connection;

public enum ConnectionHealthStatus { Unknown, Checking, Connected, Disconnected }

public record ConnectionCheckRecord(ConnectionHealthStatus Status, DateTime CheckedAt, string ResponseJson);

/// <summary>
/// Singleton feature that periodically pings the Copilot backend and exposes connection health state.
/// </summary>
public sealed class ConnectionFeature : IDisposable
{
	readonly CopilotClientService _clientService;
	readonly ILogger<ConnectionFeature> _logger;
	Timer? _timer;
	bool _initialized = false;

	public event Action? OnStatusChanged;

	public ConnectionHealthStatus Status { get; private set; } = ConnectionHealthStatus.Unknown;
	public PingResponse? LastResponse { get; private set; }
	public DateTime? LastChecked { get; private set; }
	public IReadOnlyList<ConnectionCheckRecord> History => _history;

	readonly List<ConnectionCheckRecord> _history = [];

	public const int PollIntervalSeconds = 20;

	public ConnectionFeature(CopilotClientService clientService, ILogger<ConnectionFeature> logger)
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
		Status = ConnectionHealthStatus.Checking;
		OnStatusChanged?.Invoke();

		LastResponse = await _clientService.PingAsync();
		LastChecked = DateTime.UtcNow;
		Status = LastResponse is not null ? ConnectionHealthStatus.Connected : ConnectionHealthStatus.Disconnected;

		_history.Add(new ConnectionCheckRecord(Status, LastChecked.Value, GetResponseJson()));

		_logger.LogDebug("Ping result: {Status}", Status);
		OnStatusChanged?.Invoke();
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
