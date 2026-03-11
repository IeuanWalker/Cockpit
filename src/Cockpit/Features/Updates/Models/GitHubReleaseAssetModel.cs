namespace Cockpit.Features.Updates.Models;

public sealed class GitHubReleaseAssetModel
{
	public required string? Name { get; set; }
	public required string? BrowserDownloadUrl { get; set; }
	public required long Size { get; set; }
}
