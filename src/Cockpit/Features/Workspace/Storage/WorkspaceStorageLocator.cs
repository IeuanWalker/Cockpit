using System.Security.Cryptography;
using System.Text;

namespace Cockpit.Features.Workspace.Storage;

sealed record WorkspaceStorageLocations(string Primary, string? Fallback);

static class WorkspaceStorageLocator
{
	internal static WorkspaceStorageLocations ForSession(string appDataDirectory, string sdkSessionId, string? sdkWorkspacePath)
	{
		ArgumentException.ThrowIfNullOrWhiteSpace(appDataDirectory);
		ArgumentException.ThrowIfNullOrWhiteSpace(sdkSessionId);

		string fallback = Path.Combine(
			Path.GetFullPath(appDataDirectory),
			"Workspace",
			"Sessions",
			Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(sdkSessionId))).ToLowerInvariant());

		if(string.IsNullOrWhiteSpace(sdkWorkspacePath))
		{
			return new WorkspaceStorageLocations(fallback, null);
		}

		string canonical = Path.Combine(Path.GetFullPath(sdkWorkspacePath), "Cockpit", "Workspace");
		return new WorkspaceStorageLocations(canonical, fallback);
	}

	internal static string ForDefaults(string appDataDirectory)
	{
		ArgumentException.ThrowIfNullOrWhiteSpace(appDataDirectory);
		return Path.Combine(Path.GetFullPath(appDataDirectory), "Workspace", "Defaults");
	}
}
