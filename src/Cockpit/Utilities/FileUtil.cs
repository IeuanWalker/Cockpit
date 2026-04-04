using System.Diagnostics;

namespace Cockpit.Utilities;

public static class FileUtil
{
	public static string GetNormalizedExtension(string fileName) =>
		Path.GetExtension(fileName ?? string.Empty).TrimStart('.').ToLowerInvariant();

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

	public static void RevealFile(string? filePath)
	{
		if(string.IsNullOrWhiteSpace(filePath))
		{
			return;
		}

		// Test

		try
		{
			if(OperatingSystem.IsWindows())
			{
				Process.Start(new ProcessStartInfo { FileName = "explorer.exe", Arguments = $"/select,\"{filePath}\"", UseShellExecute = true });
			}
			else if(OperatingSystem.IsMacOS())
			{
				Process.Start(new ProcessStartInfo { FileName = "open", Arguments = $"-R \"{filePath}\"", UseShellExecute = false });
			}
		}
		catch { /* best-effort */ }
	}
}
