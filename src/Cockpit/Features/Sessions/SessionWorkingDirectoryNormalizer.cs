using Cockpit.Features.Sessions.Models;

namespace Cockpit.Features.Sessions;

/// <summary>
/// Workaround for - https://github.com/github/copilot-sdk/issues/1735
/// </summary>
static class SessionWorkingDirectoryNormalizer
{
	internal static string? Normalize(string? workingDirectory)
		=> Normalize(workingDirectory, LaunchDirectories.Capture());

	/// <summary>
	/// Normalizes a working directory against a pre-captured set of launch directories. Use this
	/// overload in bulk loops (e.g. loading many sessions) so the launch directories — which are
	/// constant for the duration of the operation — are computed once via
	/// <see cref="LaunchDirectories.Capture"/> instead of on every call. The single-argument
	/// <see cref="Normalize(string?)"/> overload captures them per-call for one-off use.
	/// </summary>
	internal static string? Normalize(string? workingDirectory, in LaunchDirectories launchDirectories)
	{
		if(string.IsNullOrWhiteSpace(workingDirectory))
		{
			return null;
		}

		try
		{
			string normalizedWorkingDirectory = Path.GetFullPath(workingDirectory);
			string trimmed = normalizedWorkingDirectory.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
			return launchDirectories.Matches(trimmed)
				? null
				: normalizedWorkingDirectory;
		}
		catch
		{
			return null;
		}
	}

	/// <summary>
	/// A snapshot of the directories that indicate "no real working directory" (the app launch
	/// directory and the current process directory), normalized once so repeated comparisons in a
	/// loop avoid recomputing <see cref="Path.GetFullPath(string)"/> and
	/// <see cref="Directory.GetCurrentDirectory"/> for each item.
	/// </summary>
	internal readonly struct LaunchDirectories
	{
		readonly string _appLaunchDirectory;
		readonly string _currentProcessDirectory;

		LaunchDirectories(string appLaunchDirectory, string currentProcessDirectory)
		{
			_appLaunchDirectory = appLaunchDirectory;
			_currentProcessDirectory = currentProcessDirectory;
		}

		internal static LaunchDirectories Capture()
			=> new(
				NormalizeDirectoryPath(AppContext.BaseDirectory),
				NormalizeDirectoryPath(Directory.GetCurrentDirectory()));

		internal bool Matches(string normalizedDirectory)
			=> string.Equals(normalizedDirectory, _appLaunchDirectory, StringComparison.OrdinalIgnoreCase)
			|| string.Equals(normalizedDirectory, _currentProcessDirectory, StringComparison.OrdinalIgnoreCase);
	}

	static string NormalizeDirectoryPath(string path)
	{
		string normalized = Path.GetFullPath(path);
		return normalized.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
	}

	internal static void ApplyContextConsistency(SessionContext context)
	{
		context.CurrentWorkingDirectory = Normalize(context.CurrentWorkingDirectory);
		if(context.CurrentWorkingDirectory is null)
		{
			context.GitRoot = null;
			context.Repository = null;
			context.Branch = null;
			context.EditedFiles = [];
		}
	}
}
