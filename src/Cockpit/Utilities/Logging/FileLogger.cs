using Microsoft.Extensions.Logging;

namespace Cockpit.Utilities.Logging;

sealed class FileLogger(string category, FileLoggerProvider provider) : ILogger
{
	public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

	public bool IsEnabled(LogLevel logLevel) => logLevel != LogLevel.None;

	public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
	{
		if(!IsEnabled(logLevel))
		{
			return;
		}

		string level = logLevel switch
		{
			LogLevel.Trace => "Trace      ",
			LogLevel.Debug => "Debug      ",
			LogLevel.Information => "Information",
			LogLevel.Warning => "Warning    ",
			LogLevel.Error => "Error      ",
			LogLevel.Critical => "Critical   ",
			_ => logLevel.ToString()
		};

		string message = formatter(state, exception);
		string line = $"{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff} [{level}] {category}: {message}";
		if(exception is not null)
		{
			line += Environment.NewLine + exception;
		}

		provider.WriteLine(line);
	}
}
