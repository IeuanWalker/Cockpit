using System.Text.Json.Serialization;

namespace Cockpit.Features.Updates.Models;

public sealed class GitHubReleaseAssetModel
{
	public required string? Name { get; set; }
	[JsonPropertyName("browser_download_url")]
	public required string? BrowserDownloadUrl { get; set; }
	public required long Size { get; set; }
}
