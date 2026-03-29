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

	/// <summary>
	/// Flags that this attachment was added via the # file mention picker (vs the file-picker button).
	/// </summary>
	public bool IsMention { get; init; }

	/// <summary>
	/// A UUID string assigned per chip instance; enables 1:1 chip↔attachment tracking so the same file can be referenced multiple times with independent lifecycle.
	/// </summary>
	public string? ChipId { get; init; }

	// Cached data URI — set immediately for new attachments, lazily on first GetPreviewSrc() for replayed ones
	string? _dataUri;

	public string? DataUri => _dataUri;

	public AttachmentModel(string fileName, string filePath, string? dataUri, string mimeType, bool isMention = false, string? chipId = null)
	{
		FileName = fileName;
		FilePath = filePath;
		MimeType = mimeType;
		_dataUri = dataUri;
		IsMention = isMention;
		ChipId = chipId;
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
