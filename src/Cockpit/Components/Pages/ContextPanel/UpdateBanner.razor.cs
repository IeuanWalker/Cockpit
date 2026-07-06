using Cockpit.Features.Updates;
using Cockpit.Features.Updates.Models;
using Microsoft.AspNetCore.Components;

namespace Cockpit.Components.Pages.ContextPanel;

public partial class UpdateBanner : ComponentBase, IDisposable
{
	readonly UpdateFeature _feature;

	public UpdateBanner(UpdateFeature feature)
	{
		_feature = feature;
	}

	protected override void OnInitialized()
	{
		_feature.OnUpdateChecked += OnUpdateChecked;
	}

	void OnUpdateChecked() => InvokeAsync(StateHasChanged);

	async Task DownloadOrInstall()
	{
		if(_feature.CachedResult?.LatestRelease is null)
		{
			return;
		}

		if(!_feature.IsInstalledBuild)
		{
			_feature.OpenReleaseInBrowser(_feature.CachedResult.LatestRelease);
			return;
		}

		if(_feature.DownloadState.Status is UpdateDownloadStatusEnum.Downloading or UpdateDownloadStatusEnum.Installing)
		{
			return;
		}

		if(_feature.DownloadState.Status is UpdateDownloadStatusEnum.Downloaded)
		{
			await _feature.InstallDownloadedUpdateAsync();
			return;
		}

		await _feature.DownloadLatestInstallerAsync();
	}

	void ViewReleaseNotes()
	{
		if(_feature.CachedResult?.LatestRelease is not null)
		{
			_feature.OpenReleaseInBrowser(_feature.CachedResult.LatestRelease);
		}
	}

	async Task ToggleAutoInstall(ChangeEventArgs args)
	{
		bool enabled = args.Value is bool value && value;
		_feature.AutoInstallAfterDownloadIfNoActiveSession = enabled;
		await _feature.EvaluateAutoInstallAfterDownloadAsync();
	}

	string GetPrimaryActionLabel()
	{
		return _feature.DownloadState.Status switch
		{
			UpdateDownloadStatusEnum.Downloading => "Downloading...",
			UpdateDownloadStatusEnum.Downloaded => "Install",
			UpdateDownloadStatusEnum.Installing => "Installing...",
			_ => "Download"
		};
	}

	bool IsPrimaryActionDisabled()
	{
		return _feature.DownloadState.Status is UpdateDownloadStatusEnum.Downloading or UpdateDownloadStatusEnum.Installing;
	}

	string? GetProgressLabel()
	{
		UpdateDownloadStateModel state = _feature.DownloadState;
		if(state.Status is not UpdateDownloadStatusEnum.Downloading and not UpdateDownloadStatusEnum.Downloaded)
		{
			return null;
		}

		string downloaded = FormatBytes(state.BytesDownloaded);
		if(state.TotalBytes is > 0)
		{
			string total = FormatBytes(state.TotalBytes.Value);
			string percent = $"{state.ProgressPercent ?? 0:0.#}%";
			return $"{percent} ({downloaded} / {total})";
		}

		return $"{downloaded} downloaded";
	}

	static string FormatBytes(long bytes)
	{
		double value = bytes;
		string[] units = ["B", "KB", "MB", "GB", "TB"];
		int unitIndex = 0;

		while(value >= 1024 && unitIndex < units.Length - 1)
		{
			value /= 1024;
			unitIndex++;
		}

		return $"{value:0.#} {units[unitIndex]}";
	}

	void Dismiss()
	{
		if(_feature.CachedResult?.LatestRelease is { TagName: { } tagName })
		{
			_feature.DismissVersion(tagName);
			StateHasChanged();
		}
	}

	public void Dispose()
	{
		Dispose(true);
		GC.SuppressFinalize(this);
	}

	protected virtual void Dispose(bool disposing)
	{
		if(disposing)
		{
			_feature.OnUpdateChecked -= OnUpdateChecked;
		}
	}
}
