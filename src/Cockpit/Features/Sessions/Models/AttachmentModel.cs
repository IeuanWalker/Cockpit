namespace Cockpit.Features.Sessions.Models;

public class AttachmentModel
{
	static readonly HashSet<string> imageExtensions = new(StringComparer.OrdinalIgnoreCase)
	{
		".png", ".jpg", ".jpeg", ".gif", ".webp", ".bmp", ".svg"
	};

	public string FileName { get; init; }
	public string FilePath { get; init; }
	public string MimeType { get; init; }

	// Cached data URI — set immediately for new attachments, lazily on first GetPreviewSrc() for replayed ones
	string? _dataUri;

	public string? DataUri => _dataUri;

	public AttachmentModel(string fileName, string filePath, string? dataUri, string mimeType)
	{
		FileName = fileName;
		FilePath = filePath;
		MimeType = mimeType;
		_dataUri = dataUri;
	}

	public bool IsImage =>
		MimeType.StartsWith("image/", StringComparison.OrdinalIgnoreCase) ||
		imageExtensions.Contains(Path.GetExtension(FileName));

	/// <summary>
	/// Returns a data URI suitable for an img src. Uses cached value if available,
	/// otherwise reads from disk and caches the result for subsequent renders.
	/// </summary>
	public string? GetPreviewSrc()
	{
		if(_dataUri is not null)
		{
			return _dataUri;
		}

		if(!IsImage || string.IsNullOrEmpty(FilePath) || !File.Exists(FilePath))
		{
			return null;
		}

		try
		{
			byte[] bytes = File.ReadAllBytes(FilePath);
			string mime = MimeType.Length > 0 ? MimeType : "image/png";
			_dataUri = $"data:{mime};base64,{Convert.ToBase64String(bytes)}";
			return _dataUri;
		}
		catch
		{
			return null;
		}
	}
}
