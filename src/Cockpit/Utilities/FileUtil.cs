namespace Cockpit.Utilities;

public static class FileUtil
{
	public static string GetMimeType(string ext) => ext.TrimStart('.').ToLowerInvariant() switch
	{
		"png" => "image/png",
		"jpg" or "jpeg" => "image/jpeg",
		"gif" => "image/gif",
		"webp" => "image/webp",
		"svg" => "image/svg+xml",
		"bmp" => "image/bmp",
		"pdf" => "application/pdf",
		"txt" => "text/plain",
		"md" => "text/markdown",
		"cs" => "text/plain",
		"json" => "application/json",
		_ => "application/octet-stream"
	};
}
