namespace Cockpit.Features.Updates.Models;

public record UpdateCheckResult(bool UpdateAvailable, string CurrentVersion, GitHubReleaseModel? LatestRelease);
