using Microsoft.Extensions.Logging;

namespace Cockpit.Utilities.Logging;

internal sealed class FileLoggerProvider : ILoggerProvider
{
	const long MaxBytes = 5 * 1024 * 1024; // 5 MB

	readonly string _logPath;
	readonly object _lock = new();
	StreamWriter? _writer;

	public FileLoggerProvider()
	{
		_logPath = Path.Combine(LogDirectoryHelper.LogDirectory, "app.log");
		_writer = OpenWriter();
	}

	public ILogger CreateLogger(string categoryName) => new FileLogger(categoryName, this);

	public void WriteLine(string line)
	{
		lock(_lock)
		{
			try
			{
				RotateIfNeeded();
				_writer?.WriteLine(line);
				_writer?.Flush();
			}
			catch { /* best-effort: never crash the app due to logging */ }
		}
	}

	void RotateIfNeeded()
	{
		if(_writer is null)
			return;

		FileInfo info = new(_logPath);
		if(!info.Exists || info.Length < MaxBytes)
			return;

		_writer.Dispose();
		_writer = null;

		string backup = _logPath + ".old";
		if(File.Exists(backup))
			File.Delete(backup);
		File.Move(_logPath, backup);

		_writer = OpenWriter();
	}

	StreamWriter OpenWriter()
	{
		try
		{
			return new StreamWriter(_logPath, append: true) { AutoFlush = false };
		}
		catch
		{
			return StreamWriter.Null;
		}
	}

	public void Dispose()
	{
		lock(_lock)
		{
			_writer?.Flush();
			_writer?.Dispose();
			_writer = null;
		}
	}
}
