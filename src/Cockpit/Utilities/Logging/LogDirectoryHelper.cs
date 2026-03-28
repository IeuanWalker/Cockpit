namespace Cockpit.Utilities.Logging;

internal static class LogDirectoryHelper
{
	static string? _logDirectory;

	public static string LogDirectory => _logDirectory ??= ResolveAndCreate();

	static string ResolveAndCreate()
	{
		string appData = OperatingSystem.IsMacOS()
			? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Personal), "Library", "Application Support")
			: Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);

		string dir = Path.Combine(appData, "Cockpit", "logs");
		Directory.CreateDirectory(dir);
		return dir;
	}
}
