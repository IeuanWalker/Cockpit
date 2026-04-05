
using System.Text.Json.Serialization;

namespace Cockpit.Features.Updates.Models;

public sealed class GitHubReleaseModel
{
	[JsonPropertyName("tag_name")]
	public string? TagName { get; set; }
	public string? Name { get; set; }
	public string? Body { get; set; }
	public bool Prerelease { get; set; }
	public bool Draft { get; set; }
	[JsonPropertyName("published_at")]
	public DateTime? PublishedAt { get; set; }
	[JsonPropertyName("html_url")]
	public string? HtmlUrl { get; set; }
	public List<GitHubReleaseAssetModel>? Assets { get; set; } = [];
}
