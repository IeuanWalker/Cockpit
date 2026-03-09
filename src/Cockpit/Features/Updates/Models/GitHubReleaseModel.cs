
namespace Cockpit.Features.Updates.Models;

public sealed class GitHubReleaseModel
{
	public string? TagName { get; set; }
	public string? Name { get; set; }
	public string? Body { get; set; }
	public bool Prerelease { get; set; }
	public bool Draft { get; set; }
	public DateTime? PublishedAt { get; set; }
	public string? HtmlUrl { get; set; }
	public List<GitHubReleaseAssetModel>? Assets { get; set; } = [];
}
