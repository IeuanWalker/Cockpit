using GitHub.Copilot.SDK;
using Microsoft.Extensions.Logging;

namespace Cockpit.Features.Sdk;

public class CopilotClientFeature : IAsyncDisposable
{
	readonly ILogger<CopilotClientFeature> _logger;
	CopilotClient? _client;
	readonly SemaphoreSlim _clientLock = new(1, 1);
	bool _disposed;

	public event Action<ConnectionState>? OnConnectionStateChanged;
	public event Action<string>? OnError;

	public ConnectionState State => _client?.State ?? ConnectionState.Disconnected;

	public CopilotClientFeature(ILogger<CopilotClientFeature> logger)
	{
		_logger = logger;
	}

	public async Task<CopilotClient> GetClientAsync(CancellationToken cancellationToken = default)
	{
		await _clientLock.WaitAsync(cancellationToken);
		try
		{
			ObjectDisposedException.ThrowIf(_disposed, nameof(CopilotClientFeature));

			if(_client is not null)
			{
				return _client;
			}

			_logger.LogInformation("Initializing CopilotClient");

			_client = new CopilotClient(new CopilotClientOptions
			{
				AutoStart = true,
				LogLevel = "info",
				UseStdio = true,
				Logger = _logger,
				SessionIdleTimeoutSeconds = (int)new TimeSpan(0, 30, 0).TotalSeconds
			});

			await _client.StartAsync(cancellationToken);

			_logger.LogInformation("CopilotClient started successfully");

			OnConnectionStateChanged?.Invoke(_client.State);

			return _client;
		}
		catch(Exception ex)
		{
			_logger.LogError(ex, "Failed to initialize or start CopilotClient");
			OnError?.Invoke($"Failed to connect to Copilot: {ex.Message}");
			throw;
		}
		finally
		{
			_clientLock.Release();
		}
	}

	public async Task<PingResponse?> PingAsync(CancellationToken cancellationToken = default)
	{
		try
		{
			CopilotClient client = await GetClientAsync(cancellationToken);
			return await client.PingAsync("health check", cancellationToken);
		}
		catch(Exception ex)
		{
			_logger.LogError(ex, "Ping failed");

			return null;
		}
	}

	public async Task RestartAsync(CancellationToken cancellationToken = default)
	{
		_logger.LogInformation("Restarting CopilotClient");

		await StopAsync();
		await GetClientAsync(cancellationToken);
	}

	public async Task StopAsync()
	{
		await _clientLock.WaitAsync();
		try
		{
			if(_client is null)
			{
				return;
			}

			_logger.LogInformation("Stopping CopilotClient");

			try
			{
				await _client.StopAsync();
			}
			catch(Exception ex)
			{
				_logger.LogWarning(ex, "Error during normal stop, attempting force stop");

				await _client.ForceStopAsync();
			}
			finally
			{
				await _client.DisposeAsync();
				_client = null;
				OnConnectionStateChanged?.Invoke(ConnectionState.Disconnected);
			}
		}
		finally
		{
			_clientLock.Release();
		}
	}

	public async ValueTask DisposeAsync()
	{
		if(_disposed)
		{
			return;
		}

		_disposed = true;
		await StopAsync();
		_clientLock.Dispose();

		GC.SuppressFinalize(this);
	}
}
