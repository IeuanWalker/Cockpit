using Cockpit.Features.Updates;
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

	void Download()
	{
		if(_feature.CachedResult?.LatestRelease is not null)
		{
			_feature.OpenReleaseInBrowser(_feature.CachedResult.LatestRelease);
		}
	}

	void Dismiss()
	{
		if(_feature.CachedResult?.LatestRelease is not null)
		{
			_feature.DismissVersion(_feature.CachedResult.LatestRelease.TagName);
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
