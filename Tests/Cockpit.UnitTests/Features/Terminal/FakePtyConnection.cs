using Porta.Pty;

namespace Cockpit.UnitTests.Features.Terminal;

/// <summary>
/// Minimal in-memory fake of <see cref="IPtyConnection"/> for unit testing.
/// Uses <see cref="MemoryStream"/> instances so tests can control readable data
/// without spawning a real process.
/// </summary>
sealed class FakePtyConnection : IPtyConnection, IDisposable
{
	readonly MemoryStream _readerStream;
	readonly MemoryStream _writerStream = new();

	/// <summary>Creates a fake connection with an empty reader stream (EOF on first read).</summary>
	public FakePtyConnection() : this(ReadOnlySpan<byte>.Empty) { }

	/// <summary>
	/// Creates a fake connection whose reader stream is pre-populated with <paramref name="readerData"/>.
	/// The read loop will consume this data then reach EOF.
	/// </summary>
	public FakePtyConnection(ReadOnlySpan<byte> readerData)
	{
		_readerStream = new MemoryStream(readerData.ToArray(), writable: false);
	}

	public Stream ReaderStream => _readerStream;
	public Stream WriterStream => _writerStream;

	public int Pid => -1;
	public int ExitCode => 0;

	public event EventHandler<PtyExitedEventArgs>? ProcessExited;

	public bool WaitForExit(int milliseconds) => true;
	public void Kill() { }
	public void Resize(int cols, int rows) { }

	public void Dispose()
	{
		_readerStream.Dispose();
		_writerStream.Dispose();
	}
}
