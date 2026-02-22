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
	/// Returns a src suitable for an img tag: uses DataUri if available,
	/// otherwise falls back to reading the file from disk.
	/// </summary>
	public string? GetPreviewSrc()
	{
		if(DataUri is not null)
		{
			return DataUri;
		}

		if(!IsImage || !File.Exists(FilePath))
		{
			return null;
		}

		try
		{
			byte[] bytes = File.ReadAllBytes(FilePath);
			string mime = MimeType.Length > 0 ? MimeType : "image/png";
			return $"data:{mime};base64,{Convert.ToBase64String(bytes)}";
		}
		catch
		{
			return null;
		}
	}
}
