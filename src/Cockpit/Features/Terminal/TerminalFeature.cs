using System.Buffers;
using System.Collections.Concurrent;
using System.Text;
using Microsoft.Extensions.Logging;
using Porta.Pty;

namespace Cockpit.Features.Terminal;

public sealed partial class TerminalFeature : IDisposable, IAsyncDisposable
{
	readonly ILogger<TerminalFeature> _logger;
	readonly Func<PtyOptions, CancellationToken, Task<IPtyConnection>> _connectionFactory;
	readonly ConcurrentDictionary<string, TerminalSessionModel> _sessions = new();

	public event Action<string, string>? OnDataReceived;

	public TerminalFeature(ILogger<TerminalFeature> logger)
		: this(logger, PtyProvider.SpawnAsync)
	{
	}

	/// <summary>
	/// Constructor used in tests to inject a custom PTY connection factory.
	/// </summary>
	internal TerminalFeature(ILogger<TerminalFeature> logger, Func<PtyOptions, CancellationToken, Task<IPtyConnection>> connectionFactory)
	{
		_logger = logger;
		_connectionFactory = connectionFactory;
	}

	/// <summary>
	/// Returns the live session model for the given ID, or <see langword="null"/> if not found.
	/// Exposed internally for testing and diagnostics.
	/// </summary>
	internal TerminalSessionModel? GetSession(string sessionId)
	{
		_sessions.TryGetValue(sessionId, out TerminalSessionModel? session);
		return session;
	}

	/// <summary>
	/// Raises <see cref="OnDataReceived"/> directly. Used in tests to assert event routing
	/// without going through the async PTY read loop.
	/// </summary>
	internal void RaiseDataReceived(string sessionId, string data) => OnDataReceived?.Invoke(sessionId, data);

	public async Task<bool> CreateSession(string sessionId, string workingDirectory, CancellationToken ct = default)
	{
		try
		{
			PtyOptions options = new()
			{
				Cols = 120,
				Rows = 30,
				Cwd = workingDirectory,
				// NOTE: Shell application is currently fixed; update here if user override is added.
				App = OperatingSystem.IsWindows() ? "powershell.exe" : "/bin/bash",
			};

			IPtyConnection ptyConnection = await _connectionFactory(options, ct);
			TerminalSessionModel session = new(sessionId, ptyConnection);

			CancellationTokenSource cts = new();
			session.ReadTaskCancellation = cts;

			session.ReadTask = Task.Run(async () =>
			{
				byte[] buffer = ArrayPool<byte>.Shared.Rent(4096);
				// Char buffer sized to the byte buffer: UTF-8 never produces more chars than bytes read.
				char[] charBuffer = ArrayPool<char>.Shared.Rent(buffer.Length);
				// Stateful decoder so partial multi-byte sequences are carried across reads.
				Decoder decoder = Encoding.UTF8.GetDecoder();
				try
				{
					while(!cts.Token.IsCancellationRequested)
					{
						int bytesRead = await ptyConnection.ReaderStream.ReadAsync(buffer, cts.Token);
						if(bytesRead <= 0)
						{
							_logger.LogDebug("Terminal session {SessionId} PTY stream reached EOF", sessionId);
							break;
						}

						int charCount = decoder.GetChars(buffer, 0, bytesRead, charBuffer, 0);
						string data = new(charBuffer, 0, charCount);
						session.BufferOutput(data);
						OnDataReceived?.Invoke(sessionId, data);
					}
				}
				catch(Exception ex) when(ex is not OperationCanceledException)
				{
					_logger.LogError(ex, "Terminal session {SessionId} background read task failed", sessionId);
				}
				finally
				{
					ArrayPool<byte>.Shared.Return(buffer);
					ArrayPool<char>.Shared.Return(charBuffer);
				}
			}, cts.Token);

			// If the session already exists, discard the newly spawned connection
			if(!_sessions.TryAdd(sessionId, session))
			{
				cts.Cancel();
				cts.Dispose();
				ptyConnection.Dispose();
				return false;
			}

			return true;
		}
		catch(Exception ex)
		{
			_logger.LogError(ex, "Failed to create terminal session {SessionId}", sessionId);
			return false;
		}
	}

	public string GetBufferedOutput(string sessionId) =>
		_sessions.TryGetValue(sessionId, out TerminalSessionModel? session)
			? session.GetBuffer()
			: string.Empty;

	public async Task<bool> WriteAsync(string sessionId, string data, CancellationToken ct = default)
	{
		if(!_sessions.TryGetValue(sessionId, out TerminalSessionModel? session))
		{
			return false;
		}

		try
		{
			int maxByteCount = Encoding.UTF8.GetMaxByteCount(data.Length);
			byte[] rentedBuffer = ArrayPool<byte>.Shared.Rent(maxByteCount);
			try
			{
				int byteCount = Encoding.UTF8.GetBytes(data, rentedBuffer);
				await session.Connection.WriterStream.WriteAsync(rentedBuffer.AsMemory(0, byteCount), ct);
				await session.Connection.WriterStream.FlushAsync(ct);
			}
			finally
			{
				ArrayPool<byte>.Shared.Return(rentedBuffer);
			}
			return true;
		}
		catch(Exception ex)
		{
			_logger.LogDebug(ex, "Failed to write to terminal session {SessionId}", sessionId);
			return false;
		}
	}

	public void ResizePty(string sessionId, int cols, int rows)
	{
		if(_sessions.TryGetValue(sessionId, out TerminalSessionModel? session))
		{
			session.Cols = cols;
			session.Rows = rows;
			session.Connection.Resize(cols, rows);
		}
	}

	public async Task RestartSession(string sessionId, string workingDirectory, CancellationToken ct = default)
	{
		if(_sessions.TryRemove(sessionId, out TerminalSessionModel? existing))
		{
			// Clear buffer before teardown so old output can't re-appear in the new session
			existing.ClearBuffer();
			await TearDownSessionAsync(existing, sessionId);
		}

		await CreateSession(sessionId, workingDirectory, ct);
	}

	public void SoftClear(string sessionId)
	{
		if(_sessions.TryGetValue(sessionId, out TerminalSessionModel? session))
		{
			session.ClearBuffer();
		}
	}

	/// <summary>
	/// Fire-and-forget wrapper for backward API compatibility. Exceptions are not observed by the caller.
	/// Use <see cref="CloseSessionAsync"/> when you need to await completion or handle exceptions.
	/// </summary>
	public void CloseSession(string sessionId) => _ = CloseSessionAsync(sessionId);

	/// <summary>
	/// Closes the terminal session, cancelling any background read tasks and disposing resources.
	/// </summary>
	public async Task CloseSessionAsync(string sessionId)
	{
		if(_sessions.TryRemove(sessionId, out TerminalSessionModel? session))
		{
			await TearDownSessionAsync(session, sessionId);
		}
	}

	public void Dispose()
	{
		// Synchronously cancel all sessions without waiting for read tasks to drain.
		// For awaited cleanup, prefer DisposeAsync.
		foreach(TerminalSessionModel session in _sessions.Values)
		{
			session.ReadTaskCancellation?.Cancel();
			session.ReadTaskCancellation?.Dispose();
			try
			{
				session.Connection.Dispose();
			}
			catch(Exception ex)
			{
				_logger.LogDebug(ex, "Failed to dispose terminal connection for session {SessionId} during synchronous dispose", session.Id);
			}
		}

		_sessions.Clear();
		GC.SuppressFinalize(this);
	}

	public async ValueTask DisposeAsync()
	{
		// Snapshot the sessions and clear the dictionary so no new operations race against teardown.
		TerminalSessionModel[] sessions = [.. _sessions.Values];
		_sessions.Clear();

		// Tear down all sessions concurrently rather than serially.
		await Task.WhenAll(sessions.Select(s => TearDownSessionAsync(s, s.Id))).ConfigureAwait(false);

		GC.SuppressFinalize(this);
	}

	async Task TearDownSessionAsync(TerminalSessionModel session, string sessionId)
	{
		session.ReadTaskCancellation?.Cancel();

		try
		{
			if(session.ReadTask is not null)
			{
				await session.ReadTask.WaitAsync(TimeSpan.FromSeconds(2));
			}
		}
		catch(Exception ex)
		{
			_logger.LogDebug(ex, "Failed to wait for read task to complete for session {SessionId}", sessionId);
		}
		finally
		{
			session.ReadTaskCancellation?.Dispose();
		}

		try
		{
			session.Connection.Dispose();
		}
		catch(Exception ex)
		{
			_logger.LogDebug(ex, "Failed to dispose terminal connection for session {SessionId}", sessionId);
		}
	}
}
