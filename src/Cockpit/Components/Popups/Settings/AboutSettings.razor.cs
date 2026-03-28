using Cockpit.Features.Updates;
using Microsoft.AspNetCore.Components;

namespace Cockpit.Components.Popups.Settings;

public partial class AboutSettings : ComponentBase, IDisposable
{
	readonly UpdateFeature _updateFeature;

	public AboutSettings(UpdateFeature updateFeature)
	{
		_updateFeature = updateFeature;
	}

	LicensesPopup _licensesPopup = default!;
	bool _isChecking;

	DateTime? _installedDate => _updateFeature.InstalledDate?.ToLocalTime();
	DateTime? _updatedDate => _updateFeature.CachedResult?.LatestRelease?.PublishedAt;

	protected override void OnInitialized()
	{
		_updateFeature.OnUpdateChecked += OnUpdateChecked;
	}

	void OnUpdateChecked()
	{
		InvokeAsync(() =>
		{
			_isChecking = false;
			StateHasChanged();
		});
	}

	async Task CheckForUpdate()
	{
		_isChecking = true;
		StateHasChanged();
		await _updateFeature.CheckForUpdate();
	}

	void OpenGitHub()
	{
		_ = Launcher.Default.OpenAsync(new Uri("https://github.com/IeuanWalker/Cockpit"));
	}

	void OpenLicenses()
	{
		_licensesPopup.Open();
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
			_updateFeature.OnUpdateChecked -= OnUpdateChecked;
		}
	}
}
