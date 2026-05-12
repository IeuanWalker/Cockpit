using GitHub.Copilot.SDK;
using Microsoft.Extensions.Logging;

namespace Cockpit.Features.Sdk;

/// <summary>
/// Manages the singleton <see cref="CopilotClient"/> lifecycle: creation, lazy start,
/// graceful/force stop, and disposal. All other features obtain the client via
/// <see cref="GetClientAsync"/>.
/// </summary>
public sealed class CopilotClientFeature : IAsyncDisposable, ICopilotPingService
{
	/// <summary>30-minute session idle timeout, in seconds.</summary>
	const int sessionIdleTimeoutSeconds = 1800;

	readonly ILogger<CopilotClientFeature> _logger;
	readonly UserAppSettings _appSettings;
	readonly SemaphoreSlim _clientLock = new(1, 1);
	// Accessed only under _clientLock; volatile for the lock-free State property read.
	volatile CopilotClient? _client;
	// Set to true under _clientLock so GetClientAsync sees it after lock acquisition.
	bool _disposed;
	// Separate CAS guard so concurrent DisposeAsync calls are idempotent without a lock.
	int _disposeGuard;

	public event Action<ConnectionState>? OnConnectionStateChanged;
	public event Action<string>? OnError;

	/// <summary>
	/// Returns the current <see cref="ConnectionState"/>, or
	/// <see cref="ConnectionState.Disconnected"/> when no client exists.
	/// Uses a volatile read so the check is safe outside <see cref="_clientLock"/>.
	/// </summary>
	public ConnectionState State => _client?.State ?? ConnectionState.Disconnected;

	public CopilotClientFeature(ILogger<CopilotClientFeature> logger, UserAppSettings appSettings)
	{
		_logger = logger;
		_appSettings = appSettings;
	}

	/// <summary>
	/// Returns the singleton <see cref="CopilotClient"/>, creating and starting it on
	/// first call. Thread-safe: concurrent callers serialise through <see cref="_clientLock"/>.
	/// Events are fired after the lock is released to avoid handler re-entrancy deadlocks.
	/// </summary>
	public async Task<CopilotClient> GetClientAsync(CancellationToken cancellationToken = default)
	{
		// Captured under lock so events fire safely after the lock is released.
		ConnectionState? notifyState = null;
		string? notifyError = null;

		await _clientLock.WaitAsync(cancellationToken);
		try
		{
			ObjectDisposedException.ThrowIf(_disposed, this);

			if(_client is not null)
			{
				return _client;
			}

			_logger.LogInformation("Initializing CopilotClient (logLevel={LogLevel}, telemetry={TelemetryEnabled})",
				_appSettings.SdkLogLevel, _appSettings.TelemetryEnabled);

			CopilotClientOptions options = new()
			{
				AutoStart = true,
				LogLevel = _appSettings.SdkLogLevel,
				UseStdio = true,
				Logger = _logger,
				SessionIdleTimeoutSeconds = sessionIdleTimeoutSeconds
			};

			if(_appSettings.TelemetryEnabled)
			{
				string telemetryDir = Path.Combine(FileSystem.AppDataDirectory, "telemetry");
				Directory.CreateDirectory(telemetryDir);
				string telemetryPath = Path.Combine(telemetryDir, $"otel-{DateTime.UtcNow:yyyyMMdd}.jsonl");

				options.Telemetry = new TelemetryConfig
				{
					ExporterType = "file",
					FilePath = telemetryPath
				};
				_logger.LogInformation("SDK telemetry exporting to {TelemetryPath}", telemetryPath);
			}

			_client = new CopilotClient(options);

			await _client.StartAsync(cancellationToken);

			_logger.LogInformation("CopilotClient started successfully");

			notifyState = _client.State;
			return _client;
		}
		catch(OperationCanceledException)
		{
			// Cancellation is caller-initiated; clean up any partial state silently.
			if(_client is not null)
			{
				try
				{
					await _client.DisposeAsync();
				}
				catch(Exception disposeEx)
				{
					_logger.LogWarning(disposeEx, "Failed to dispose client after cancelled startup");
				}
				_client = null;
			}
			throw;
		}
		catch(Exception ex)
		{
			// Dispose and clear any partially-initialised client so the next call
			// retries creation rather than returning a poisoned instance.
			if(_client is not null)
			{
				try
				{
					await _client.DisposeAsync();
				}
				catch(Exception disposeEx)
				{
					_logger.LogWarning(disposeEx, "Failed to dispose client after startup failure");
				}
				_client = null;
			}

			_logger.LogError(ex, "Failed to initialize or start CopilotClient");
			notifyError = ex switch
			{
				OperationCanceledException => "Connection to Copilot timed out. Check your network and try again.",
				IOException ioEx => $"Copilot connection I/O error: {ioEx.Message}",
				InvalidOperationException invEx => $"Copilot configuration error: {invEx.Message}",
				_ => $"Failed to connect to Copilot ({ex.GetType().Name}): {ex.Message}"
			};
			throw;
		}
		finally
		{
			_clientLock.Release();
			// Fire events outside the lock so handlers cannot deadlock by calling back
			// into GetClientAsync or any other method that acquires _clientLock.
			// Guard each invocation so a misbehaving handler cannot mask the real exception.
			if(notifyState is not null)
			{
				try
				{
					OnConnectionStateChanged?.Invoke(notifyState.Value);
				}
				catch(Exception handlerEx)
				{
					_logger.LogWarning(handlerEx, "OnConnectionStateChanged handler threw");
				}
			}
			if(notifyError is not null)
			{
				try
				{
					OnError?.Invoke(notifyError);
				}
				catch(Exception handlerEx)
				{
					_logger.LogWarning(handlerEx, "OnError handler threw");
				}
			}
		}
	}

	/// <inheritdoc/>
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

	/// <summary>
	/// Stops then recreates the client. Useful after unrecoverable connection errors.
	/// </summary>
	public async Task RestartAsync(CancellationToken cancellationToken = default)
	{
		_logger.LogInformation("Restarting CopilotClient");
		await StopAsync();
		await GetClientAsync(cancellationToken);
	}

	/// <summary>
	/// Gracefully stops the client (falling back to force-stop on failure) and releases it.
	/// No-op when no client has been created.
	/// The disconnect event is fired after releasing <see cref="_clientLock"/> to prevent
	/// handler re-entrancy deadlocks.
	/// </summary>
	public async Task StopAsync()
	{
		if(_disposed)
		{
			return;
		}

		await StopCoreAsync();
	}

	async Task StopCoreAsync()
	{
		bool notifyDisconnected = false;

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
				try
				{
					await _client.ForceStopAsync();
				}
				catch(Exception forceEx)
				{
					_logger.LogWarning(forceEx, "Force stop also failed");
				}
			}
			finally
			{
				// Null out before DisposeAsync so the lock-free State property returns
				// Disconnected immediately, even while the SDK client is still disposing.
				CopilotClient disposing = _client;
				_client = null;
				notifyDisconnected = true;
				try
				{
					await disposing.DisposeAsync();
				}
				catch(Exception disposeEx)
				{
					_logger.LogWarning(disposeEx, "Client dispose failed");
				}
			}
		}
		finally
		{
			_clientLock.Release();
			if(notifyDisconnected)
			{
				try
				{
					OnConnectionStateChanged?.Invoke(ConnectionState.Disconnected);
				}
				catch(Exception handlerEx)
				{
					_logger.LogWarning(handlerEx, "OnConnectionStateChanged handler threw");
				}
			}
		}
	}

	/// <inheritdoc/>
	/// <remarks>
	/// Idempotent: concurrent or repeated calls return without side-effects after the
	/// first completes. <see cref="_disposed"/> is set under <see cref="_clientLock"/>
	/// so that any <see cref="GetClientAsync"/> call racing with disposal sees the flag
	/// as soon as it acquires the lock.
	/// </remarks>
	public async ValueTask DisposeAsync()
	{
		if(Interlocked.Exchange(ref _disposeGuard, 1) != 0)
		{
			return;
		}

		// Signal to GetClientAsync callers that are waiting on the lock.
		await _clientLock.WaitAsync();
		_disposed = true;
		_clientLock.Release();

		try
		{
			await StopCoreAsync();
		}
		catch(Exception ex)
		{
			_logger.LogWarning(ex, "Error during disposal stop");
		}
		finally
		{
			_clientLock.Dispose();
		}
	}
}