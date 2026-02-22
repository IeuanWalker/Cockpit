namespace Cockpit.Features.Sessions.Models;

public record AttachmentModel(string FileName, string FilePath, string? DataUri, string MimeType)
{
	static readonly HashSet<string> imageExtensions = new(StringComparer.OrdinalIgnoreCase)
	{
		".png", ".jpg", ".jpeg", ".gif", ".webp", ".bmp", ".svg"
	};

	public bool IsImage =>
		MimeType.StartsWith("image/", StringComparison.OrdinalIgnoreCase) ||
		imageExtensions.Contains(Path.GetExtension(FileName));

	/// <summary>
	/// Returns a src suitable for an img tag from the cached data URI.
	/// </summary>
	public string? GetPreviewSrc() => DataUri;
}
