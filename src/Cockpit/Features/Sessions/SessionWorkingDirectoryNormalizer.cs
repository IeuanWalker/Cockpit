using Cockpit.Features.Sessions.Models;

namespace Cockpit.Features.Sessions;

/// <summary>
/// Workaround for - https://github.com/github/copilot-sdk/issues/1735
/// </summary>
static class SessionWorkingDirectoryNormalizer
{
	internal static string? Normalize(string? workingDirectory)
	{
		if(string.IsNullOrWhiteSpace(workingDirectory))
		{
			return null;
		}

		try
		{
			string normalizedWorkingDirectory = Path.GetFullPath(workingDirectory);
			return IsLaunchWorkingDirectory(normalizedWorkingDirectory)
				? null
				: normalizedWorkingDirectory;
		}
		catch
		{
			return null;
		}
	}

	static bool IsLaunchWorkingDirectory(string normalizedWorkingDirectory)
	{
		string appLaunchDirectory = NormalizeDirectoryPath(AppContext.BaseDirectory);
		string currentProcessDirectory = NormalizeDirectoryPath(Directory.GetCurrentDirectory());
		string candidateDirectory = NormalizeDirectoryPath(normalizedWorkingDirectory);

		return string.Equals(candidateDirectory, appLaunchDirectory, StringComparison.OrdinalIgnoreCase)
			|| string.Equals(candidateDirectory, currentProcessDirectory, StringComparison.OrdinalIgnoreCase);
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
