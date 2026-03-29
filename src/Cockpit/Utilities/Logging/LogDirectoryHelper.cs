namespace Cockpit.Utilities.Logging;

static class LogDirectoryHelper
{
	static string? logDirectory;

	public static string LogDirectory => logDirectory ??= ResolveAndCreate();

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
