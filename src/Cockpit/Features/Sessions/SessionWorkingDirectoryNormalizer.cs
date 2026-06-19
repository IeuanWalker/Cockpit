using Cockpit.Features.Sessions.Models;

namespace Cockpit.Features.Sessions;

internal static class SessionWorkingDirectoryNormalizer
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
			string currentProcessWorkingDirectory = Path.GetFullPath(Directory.GetCurrentDirectory());

			return string.Equals(normalizedWorkingDirectory, currentProcessWorkingDirectory, StringComparison.OrdinalIgnoreCase)
				? null
				: normalizedWorkingDirectory;
		}
		catch
		{
			return null;
		}
	}

	internal static void ApplyContextConsistency(SessionContext context)
	{
		context.CurrentWorkingDirectory = Normalize(context.CurrentWorkingDirectory);
		if(context.CurrentWorkingDirectory is null)
		{
			context.Branch = null;
			context.EditedFiles = [];
		}
	}
}
