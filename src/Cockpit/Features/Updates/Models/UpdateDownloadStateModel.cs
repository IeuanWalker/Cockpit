namespace Cockpit.Features.Updates.Models;

public sealed record UpdateDownloadStateModel(
	UpdateDownloadStatusEnum Status,
	string? VersionTag,
	string? InstallerPath,
	long BytesDownloaded,
	long? TotalBytes,
	string? ErrorMessage)
{
	public static UpdateDownloadStateModel Idle { get; } = new(
		UpdateDownloadStatusEnum.Idle,
		null,
		null,
		0,
		null,
		null);

	public double? ProgressPercent => TotalBytes is > 0
		? Math.Round((double)BytesDownloaded / TotalBytes.Value * 100, 1)
		: null;
}
