using System.Net;
using Cockpit.Extensions;
using Cockpit.Features.Sessions.Models;
using Cockpit.Features.Updates;
using Cockpit.Features.Updates.Models;
using Shouldly;

namespace Cockpit.UnitTests.Features.Update;

public class UpdateFeatureTests
{
	const string sampleReleaseJson = """
		{
		  "url": "https://api.github.com/repos/IeuanWalker/Cockpit/releases/302933803",
		  "html_url": "https://github.com/IeuanWalker/Cockpit/releases/tag/1.8.0",
		  "tag_name": "1.8.0",
		  "name": "1.8.0",
		  "draft": false,
		  "prerelease": false,
		  "published_at": "2026-03-30T00:21:29Z",
		  "assets": [
		    {
		      "name": "Cockpit-windows-x64-1.8.0-Setup.exe",
		      "label": "",
		      "content_type": "application/x-msdos-program",
		      "size": 134435888,
		      "browser_download_url": "https://github.com/IeuanWalker/Cockpit/releases/download/1.8.0/Cockpit-windows-x64-1.8.0-Setup.exe"
		    },
		    {
		      "name": "Cockpit-windows-x64-1.8.0.zip",
		      "label": "",
		      "content_type": "application/zip",
		      "size": 134674820,
		      "browser_download_url": "https://github.com/IeuanWalker/Cockpit/releases/download/1.8.0/Cockpit-windows-x64-1.8.0.zip"
		    }
		  ],
		  "body": "**Full Changelog**: https://github.com/IeuanWalker/Cockpit/compare/1.7.0...1.8.0"
		}
		""";

	#region IsNewerVersion

	[Theory]
	[InlineData("v1.2.3", "v1.2.2", true)]
	[InlineData("v1.2.3", "v1.2.3", false)]
	[InlineData("v1.2.3", "v1.2.4", false)]
	[InlineData("v2.0.0", "v1.9.9", true)]
	[InlineData("v1.2.3-beta", "v1.2.3", false)]
	[InlineData("v1.2.3", "v1.2", true)]
	[InlineData("v1.2", "v1.2.3", false)]
	[InlineData("v1.2.3", "v1.2.3+build", false)]
	[InlineData("v1.2.3+build", "v1.2.3", false)]
	[InlineData("1.8.0", "1.7.0", true)]
	[InlineData("1.8.0", "1.8.0", false)]
	[InlineData("1.8.0", "1.9.0", false)]
	[InlineData("1.8.0", "2.0.0", false)]
	[InlineData("2.0.0", "1.8.0", true)]
	public void IsNewerVersion_CorrectlyComparesVersions(string remote, string current, bool expected)
	{
		bool result = UpdateFeature.IsNewerVersion(remote, current);
		Assert.Equal(expected, result);
	}

	[Fact]
	public void IsNewerVersion_HandlesInvalidInputGracefully()
	{
		bool result = UpdateFeature.IsNewerVersion("not-a-version", "also-not-a-version");
		Assert.False(result);
	}

	#endregion

	#region GitHubReleaseModel deserialization

	[Fact]
	public void GitHubReleaseModel_Deserializes_TagName()
	{
		GitHubReleaseModel? release = sampleReleaseJson.DeserializeJson<GitHubReleaseModel>();

		release.ShouldNotBeNull();
		release.TagName.ShouldBe("1.8.0");
	}

	[Fact]
	public void GitHubReleaseModel_Deserializes_Name()
	{
		GitHubReleaseModel? release = sampleReleaseJson.DeserializeJson<GitHubReleaseModel>();

		release.ShouldNotBeNull();
		release.Name.ShouldBe("1.8.0");
	}

	[Fact]
	public void GitHubReleaseModel_Deserializes_HtmlUrl()
	{
		GitHubReleaseModel? release = sampleReleaseJson.DeserializeJson<GitHubReleaseModel>();

		release.ShouldNotBeNull();
		release.HtmlUrl.ShouldBe("https://github.com/IeuanWalker/Cockpit/releases/tag/1.8.0");
	}

	[Fact]
	public void GitHubReleaseModel_Deserializes_Body()
	{
		GitHubReleaseModel? release = sampleReleaseJson.DeserializeJson<GitHubReleaseModel>();

		release.ShouldNotBeNull();
		release.Body.ShouldNotBeNull();
		release.Body.ShouldContain("https://github.com/IeuanWalker/Cockpit/compare/1.7.0...1.8.0");
	}

	[Fact]
	public void GitHubReleaseModel_Deserializes_Draft_AsFalse()
	{
		GitHubReleaseModel? release = sampleReleaseJson.DeserializeJson<GitHubReleaseModel>();

		release.ShouldNotBeNull();
		release.Draft.ShouldBeFalse();
	}

	[Fact]
	public void GitHubReleaseModel_Deserializes_Prerelease_AsFalse()
	{
		GitHubReleaseModel? release = sampleReleaseJson.DeserializeJson<GitHubReleaseModel>();

		release.ShouldNotBeNull();
		release.Prerelease.ShouldBeFalse();
	}

	[Fact]
	public void GitHubReleaseModel_Deserializes_PublishedAt()
	{
		GitHubReleaseModel? release = sampleReleaseJson.DeserializeJson<GitHubReleaseModel>();

		release.ShouldNotBeNull();
		release.PublishedAt.ShouldBe(new DateTime(2026, 3, 30, 0, 21, 29, DateTimeKind.Utc));
	}

	[Fact]
	public void GitHubReleaseModel_Deserializes_TwoAssets()
	{
		GitHubReleaseModel? release = sampleReleaseJson.DeserializeJson<GitHubReleaseModel>();

		release.ShouldNotBeNull();
		release.Assets.ShouldNotBeNull();
		release.Assets.Count.ShouldBe(2);
	}

	[Fact]
	public void GitHubReleaseModel_Deserializes_SetupExeAsset()
	{
		GitHubReleaseModel? release = sampleReleaseJson.DeserializeJson<GitHubReleaseModel>();

		release.ShouldNotBeNull();
		GitHubReleaseAssetModel? setupAsset = release.Assets?.Find(a => a.Name?.EndsWith(".exe") is true);
		setupAsset.ShouldNotBeNull();
		setupAsset.Name.ShouldBe("Cockpit-windows-x64-1.8.0-Setup.exe");
		setupAsset.Size.ShouldBe(134435888L);
		setupAsset.BrowserDownloadUrl.ShouldBe("https://github.com/IeuanWalker/Cockpit/releases/download/1.8.0/Cockpit-windows-x64-1.8.0-Setup.exe");
	}

	[Fact]
	public void GitHubReleaseModel_Deserializes_ZipAsset()
	{
		GitHubReleaseModel? release = sampleReleaseJson.DeserializeJson<GitHubReleaseModel>();

		release.ShouldNotBeNull();
		GitHubReleaseAssetModel? zipAsset = release.Assets?.Find(a => a.Name?.EndsWith(".zip") is true);
		zipAsset.ShouldNotBeNull();
		zipAsset.Name.ShouldBe("Cockpit-windows-x64-1.8.0.zip");
		zipAsset.Size.ShouldBe(134674820L);
		zipAsset.BrowserDownloadUrl.ShouldBe("https://github.com/IeuanWalker/Cockpit/releases/download/1.8.0/Cockpit-windows-x64-1.8.0.zip");
	}

	[Fact]
	public void GitHubReleaseModel_Deserializes_ExtraFields_WithoutError()
	{
		// The full GitHub API response includes many fields not in the model - they should be silently ignored
		Should.NotThrow(() => sampleReleaseJson.DeserializeJson<GitHubReleaseModel>());
	}

	#endregion

	#region HasRequiredAssets

	[Fact]
	public void HasRequiredAssets_ReturnsTrue_WhenBothAssetsPresent()
	{
		GitHubReleaseModel release = MakeRelease("Cockpit-windows-x64-1.8.0-Setup.exe", "Cockpit-windows-x64-1.8.0.zip");

		UpdateFeature.HasRequiredAssets(release).ShouldBeTrue();
	}

	[Fact]
	public void HasRequiredAssets_ReturnsFalse_WhenSetupExeMissing()
	{
		GitHubReleaseModel release = MakeRelease("Cockpit-windows-x64-1.8.0.zip");

		UpdateFeature.HasRequiredAssets(release).ShouldBeFalse();
	}

	[Fact]
	public void HasRequiredAssets_ReturnsFalse_WhenZipMissing()
	{
		GitHubReleaseModel release = MakeRelease("Cockpit-windows-x64-1.8.0-Setup.exe");

		UpdateFeature.HasRequiredAssets(release).ShouldBeFalse();
	}

	[Fact]
	public void HasRequiredAssets_ReturnsFalse_WhenAssetsEmpty()
	{
		GitHubReleaseModel release = new() { TagName = "1.8.0", Assets = [] };

		UpdateFeature.HasRequiredAssets(release).ShouldBeFalse();
	}

	[Fact]
	public void HasRequiredAssets_ReturnsFalse_WhenAssetsNull()
	{
		GitHubReleaseModel release = new() { TagName = "1.8.0", Assets = null };

		UpdateFeature.HasRequiredAssets(release).ShouldBeFalse();
	}

	[Fact]
	public void HasRequiredAssets_IsCaseInsensitive()
	{
		GitHubReleaseModel release = MakeRelease("Cockpit-windows-x64-1.8.0-SETUP.EXE", "Cockpit-windows-x64-1.8.0.ZIP");

		UpdateFeature.HasRequiredAssets(release).ShouldBeTrue();
	}

	[Fact]
	public void HasRequiredAssets_ReturnsFalse_WhenOnlyUnrelatedAssetsPresent()
	{
		GitHubReleaseModel release = MakeRelease("Cockpit-windows-x64-1.8.0.tar.gz", "Cockpit-macos-1.8.0.pkg");

		UpdateFeature.HasRequiredAssets(release).ShouldBeFalse();
	}

	[Fact]
	public void HasRequiredAssets_SampleJson_ReturnsTrue()
	{
		GitHubReleaseModel? release = sampleReleaseJson.DeserializeJson<GitHubReleaseModel>();

		release.ShouldNotBeNull();
		UpdateFeature.HasRequiredAssets(release).ShouldBeTrue();
	}

	static GitHubReleaseModel MakeRelease(params string[] assetNames) => new()
	{
		TagName = "1.8.0",
		Assets = [.. assetNames.Select(n => new GitHubReleaseAssetModel
		{
			Name = n,
			BrowserDownloadUrl = $"https://example.com/{n}",
			Size = 1000
		})]
	};

	#endregion

	#region CheckForUpdate

	[Fact]
	public async Task CheckForUpdate_ReturnsUpdateAvailable_WhenNewerVersionExists()
	{
		using UpdateFeature feature = MakeFeature(sampleReleaseJson, currentVersion: "1.7.0");

		UpdateCheckResult result = await feature.CheckForUpdate(TestContext.Current.CancellationToken);

		result.UpdateAvailable.ShouldBeTrue();
		result.LatestRelease.ShouldNotBeNull();
		result.LatestRelease.TagName.ShouldBe("1.8.0");
		result.CurrentVersion.ShouldBe("1.7.0");
	}

	[Fact]
	public async Task CheckForUpdate_ReturnsNoUpdate_WhenSameVersion()
	{
		using UpdateFeature feature = MakeFeature(sampleReleaseJson, currentVersion: "1.8.0");

		UpdateCheckResult result = await feature.CheckForUpdate(TestContext.Current.CancellationToken);

		result.UpdateAvailable.ShouldBeFalse();
		result.CurrentVersion.ShouldBe("1.8.0");
	}

	[Fact]
	public async Task CheckForUpdate_ReturnsNoUpdate_WhenCurrentIsNewer()
	{
		using UpdateFeature feature = MakeFeature(sampleReleaseJson, currentVersion: "1.9.0");

		UpdateCheckResult result = await feature.CheckForUpdate(TestContext.Current.CancellationToken);

		result.UpdateAvailable.ShouldBeFalse();
	}

	[Fact]
	public async Task CheckForUpdate_ReturnsNoUpdate_WhenHttpFails()
	{
		using HttpClient httpClient = new(new MockHttpMessageHandler(HttpStatusCode.ServiceUnavailable));
		using UpdateFeature feature = new(httpClient, "1.7.0");

		UpdateCheckResult result = await feature.CheckForUpdate(TestContext.Current.CancellationToken);

		result.UpdateAvailable.ShouldBeFalse();
	}

	[Fact]
	public async Task CheckForUpdate_ReturnsNoUpdate_WhenReleaseHasNoAssets()
	{
		string noAssetsJson = BuildReleaseJson("1.8.0", assets: []);
		using UpdateFeature feature = MakeFeature(noAssetsJson, currentVersion: "1.7.0");

		UpdateCheckResult result = await feature.CheckForUpdate(TestContext.Current.CancellationToken);

		result.UpdateAvailable.ShouldBeFalse();
	}

	[Fact]
	public async Task CheckForUpdate_UpdatesCachedResult_WhenReleaseHasNoAssets()
	{
		string noAssetsJson = BuildReleaseJson("1.8.0", assets: []);
		using UpdateFeature feature = MakeFeature(noAssetsJson, currentVersion: "1.7.0");

		await feature.CheckForUpdate(TestContext.Current.CancellationToken);

		feature.CachedResult.ShouldNotBeNull();
		feature.CachedResult.UpdateAvailable.ShouldBeFalse();
	}

	[Fact]
	public async Task CheckForUpdate_UpdatesCachedResult_WhenUpdateAvailable()
	{
		using UpdateFeature feature = MakeFeature(sampleReleaseJson, currentVersion: "1.7.0");

		await feature.CheckForUpdate(TestContext.Current.CancellationToken);

		feature.CachedResult.ShouldNotBeNull();
		feature.CachedResult.UpdateAvailable.ShouldBeTrue();
	}

	[Fact]
	public async Task CheckForUpdate_SetsLastChecked_AfterSuccessfulCheck()
	{
		DateTime before = DateTime.UtcNow;
		using UpdateFeature feature = MakeFeature(sampleReleaseJson, currentVersion: "1.7.0");

		await feature.CheckForUpdate(TestContext.Current.CancellationToken);

		feature.LastChecked.ShouldNotBeNull();
		feature.LastChecked.Value.ShouldBeGreaterThanOrEqualTo(before);
	}

	[Fact]
	public async Task CheckForUpdate_SetsLastChecked_EvenWhenHttpFails()
	{
		DateTime before = DateTime.UtcNow;
		using HttpClient httpClient = new(new MockHttpMessageHandler(HttpStatusCode.InternalServerError));
		using UpdateFeature feature = new(httpClient, "1.7.0");

		await feature.CheckForUpdate(TestContext.Current.CancellationToken);

		feature.LastChecked.ShouldNotBeNull();
		feature.LastChecked.Value.ShouldBeGreaterThanOrEqualTo(before);
	}

	[Fact]
	public async Task CheckForUpdate_RaisesOnUpdateCheckedEvent()
	{
		using UpdateFeature feature = MakeFeature(sampleReleaseJson, currentVersion: "1.7.0");
		bool eventFired = false;
		feature.OnUpdateChecked += () => eventFired = true;

		await feature.CheckForUpdate(TestContext.Current.CancellationToken);

		eventFired.ShouldBeTrue();
	}

	[Fact]
	public async Task CheckForUpdate_RaisesOnUpdateCheckedEvent_EvenWhenHttpFails()
	{
		using HttpClient httpClient = new(new MockHttpMessageHandler(HttpStatusCode.BadGateway));
		using UpdateFeature feature = new(httpClient, "1.7.0");
		bool eventFired = false;
		feature.OnUpdateChecked += () => eventFired = true;

		await feature.CheckForUpdate(TestContext.Current.CancellationToken);

		eventFired.ShouldBeTrue();
	}

	[Fact]
	public async Task CheckForUpdate_RespectsPassedCancellationToken()
	{
		using UpdateFeature feature = MakeFeature(sampleReleaseJson, currentVersion: "1.7.0");
		using CancellationTokenSource cts = new();
		cts.Cancel();

		UpdateCheckResult result = await feature.CheckForUpdate(cts.Token);

		// Cancelled request is caught and treated as no-update
		result.UpdateAvailable.ShouldBeFalse();
	}

	static UpdateFeature MakeFeature(string json, string currentVersion)
	{
		HttpClient httpClient = new(new MockHttpMessageHandler(json));
		return new UpdateFeature(httpClient, currentVersion);
	}

	static string BuildReleaseJson(string tagName, IEnumerable<(string name, string url)> assets)
	{
		string assetArray = string.Join(",\n", assets.Select(a => $$"""
			{
			  "name": "{{a.name}}",
			  "size": 1000,
			  "browser_download_url": "{{a.url}}"
			}
			"""));

		return $$"""
			{
			  "tag_name": "{{tagName}}",
			  "html_url": "https://github.com/IeuanWalker/Cockpit/releases/tag/{{tagName}}",
			  "draft": false,
			  "prerelease": false,
			  "assets": [{{assetArray}}]
			}
			""";
	}

	#endregion

	#region DownloadLatestInstallerAsync

	[Fact]
	public async Task DownloadLatestInstallerAsync_DownloadsInstaller_AndTracksBytes()
	{
		string root = Path.Combine(Path.GetTempPath(), "Cockpit-UpdateTests", Guid.NewGuid().ToString("N"));
		byte[] installerBytes = [.. Enumerable.Range(0, 220_000).Select(i => (byte)(i % 251))];
		try
		{
			using HttpClient httpClient = new(new DownloadAwareMockHttpMessageHandler(sampleReleaseJson, installerBytes));
			using UpdateFeature feature = new(httpClient, "1.7.0", isInstalledBuild: true, downloadRootDirectory: root);

			await feature.CheckForUpdate(TestContext.Current.CancellationToken);
			await feature.DownloadLatestInstallerAsync(TestContext.Current.CancellationToken);

			feature.DownloadState.Status.ShouldBe(UpdateDownloadStatusEnum.Downloaded);
			feature.DownloadState.TotalBytes.ShouldBe(installerBytes.LongLength);
			feature.DownloadState.BytesDownloaded.ShouldBe(installerBytes.LongLength);
			feature.DownloadState.InstallerPath.ShouldNotBeNull();
			File.Exists(feature.DownloadState.InstallerPath).ShouldBeTrue();
			File.ReadAllBytes(feature.DownloadState.InstallerPath).Length.ShouldBe(installerBytes.Length);
		}
		finally
		{
			if(Directory.Exists(root))
			{
				Directory.Delete(root, true);
			}
		}
	}

	#endregion

	#region DismissVersion

	[Fact]
	public void DismissVersion_SetsDismissedVersion()
	{
		using UpdateFeature feature = new(new HttpClient(), "1.7.0");

		feature.DismissVersion("1.8.0");

		feature.DismissedVersion.ShouldBe("1.8.0");
	}

	[Fact]
	public void DismissVersion_OverwritesPreviousDismissedVersion()
	{
		using UpdateFeature feature = new(new HttpClient(), "1.7.0");
		feature.DismissVersion("1.8.0");

		feature.DismissVersion("2.0.0");

		feature.DismissedVersion.ShouldBe("2.0.0");
	}

	[Fact]
	public void DismissedVersion_IsNullByDefault()
	{
		using UpdateFeature feature = new(new HttpClient(), "1.7.0");

		feature.DismissedVersion.ShouldBeNull();
	}

	#endregion

	#region Edge cases

	[Fact]
	public void HasRequiredAssets_ReturnsFalse_WhenAssetNameIsNull()
	{
		GitHubReleaseModel release = new()
		{
			TagName = "1.8.0",
			Assets =
			[
				new GitHubReleaseAssetModel { Name = null, BrowserDownloadUrl = "https://example.com/x", Size = 1000 },
				new GitHubReleaseAssetModel { Name = null, BrowserDownloadUrl = "https://example.com/y", Size = 1000 }
			]
		};

		UpdateFeature.HasRequiredAssets(release).ShouldBeFalse();
	}

	[Theory]
	[InlineData("", "", false)]
	[InlineData("1.0.0", "", true)]
	[InlineData("", "1.0.0", false)]
	[InlineData("v1.2.4-beta", "v1.2.3", true)]
	[InlineData("v1.0.0", "v1.0.0-beta", true)]
	public void IsNewerVersion_EdgeCases(string remote, string current, bool expected)
	{
		bool result = UpdateFeature.IsNewerVersion(remote, current);
		result.ShouldBe(expected);
	}

	[Fact]
	public void CachedResult_IsNullBeforeFirstCheck()
	{
		using UpdateFeature feature = new(new HttpClient(), "1.7.0");

		feature.CachedResult.ShouldBeNull();
	}

	[Fact]
	public void LastChecked_IsNullBeforeFirstCheck()
	{
		using UpdateFeature feature = new(new HttpClient(), "1.7.0");

		feature.LastChecked.ShouldBeNull();
	}

	[Fact]
	public void Initialize_CanBeCalledMultipleTimes_WithoutError()
	{
		using HttpClient httpClient = new(new MockHttpMessageHandler(HttpStatusCode.OK));
		using UpdateFeature feature = new(httpClient, "1.7.0");

		Should.NotThrow(() =>
		{
			feature.Initialize();
			feature.Initialize();
		});
	}

	[Fact]
	public void IsInstalledPath_ReturnsTrue_WhenExeIsInsideInstallDirectory()
	{
		bool result = UpdateFeature.IsInstalledPath(
			@"C:\Program Files\Cockpit\Cockpit.exe",
			@"C:\Program Files\Cockpit");

		result.ShouldBeTrue();
	}

	[Fact]
	public void IsInstalledPath_ReturnsFalse_WhenExeIsOutsideInstallDirectory()
	{
		bool result = UpdateFeature.IsInstalledPath(
			@"D:\Apps\Cockpit\Cockpit.exe",
			@"C:\Program Files\Cockpit");

		result.ShouldBeFalse();
	}

	[Fact]
	public void FindSetupAsset_ReturnsSetupExecutable_WhenPresent()
	{
		GitHubReleaseModel release = MakeRelease("Cockpit-windows-x64-1.8.0-Setup.exe", "Cockpit-windows-x64-1.8.0.zip");

		GitHubReleaseAssetModel? setupAsset = UpdateFeature.FindSetupAsset(release);

		setupAsset.ShouldNotBeNull();
		setupAsset.Name.ShouldEndWith("-Setup.exe");
	}

	[Fact]
	public void IsSessionActive_ReturnsTrue_ForRunningAndBlockingStates()
	{
		UpdateFeature.IsSessionActive(SessionStatusEnum.Running).ShouldBeTrue();
		UpdateFeature.IsSessionActive(SessionStatusEnum.NeedsPermission).ShouldBeTrue();
		UpdateFeature.IsSessionActive(SessionStatusEnum.NeedsUserInput).ShouldBeTrue();
		UpdateFeature.IsSessionActive(SessionStatusEnum.NeedsElicitation).ShouldBeTrue();
		UpdateFeature.IsSessionActive(SessionStatusEnum.Idle).ShouldBeFalse();
		UpdateFeature.IsSessionActive(SessionStatusEnum.Error).ShouldBeFalse();
	}

	#endregion
}

sealed class MockHttpMessageHandler : HttpMessageHandler
{
	readonly string? _responseJson;
	readonly HttpStatusCode _statusCode;

	internal MockHttpMessageHandler(string responseJson, HttpStatusCode statusCode = HttpStatusCode.OK)
	{
		_responseJson = responseJson;
		_statusCode = statusCode;
	}

	internal MockHttpMessageHandler(HttpStatusCode statusCode)
	{
		_statusCode = statusCode;
	}

	protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
	{
		cancellationToken.ThrowIfCancellationRequested();

		HttpResponseMessage response = new(_statusCode);
		if(_responseJson is not null)
		{
			response.Content = new StringContent(_responseJson, System.Text.Encoding.UTF8, "application/json");
		}

		return Task.FromResult(response);
	}
}

sealed class DownloadAwareMockHttpMessageHandler : HttpMessageHandler
{
	readonly string _releaseJson;
	readonly byte[] _installerBytes;

	internal DownloadAwareMockHttpMessageHandler(string releaseJson, byte[] installerBytes)
	{
		_releaseJson = releaseJson;
		_installerBytes = installerBytes;
	}

	protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
	{
		cancellationToken.ThrowIfCancellationRequested();
		string requestUri = request.RequestUri?.AbsoluteUri ?? string.Empty;

		if(requestUri.Contains("/releases/latest", StringComparison.OrdinalIgnoreCase))
		{
			HttpResponseMessage releaseResponse = new(HttpStatusCode.OK)
			{
				Content = new StringContent(_releaseJson, System.Text.Encoding.UTF8, "application/json")
			};
			return Task.FromResult(releaseResponse);
		}

		if(requestUri.EndsWith("-Setup.exe", StringComparison.OrdinalIgnoreCase))
		{
			ByteArrayContent content = new(_installerBytes);
			HttpResponseMessage installerResponse = new(HttpStatusCode.OK)
			{
				Content = content
			};
			return Task.FromResult(installerResponse);
		}

		return Task.FromResult(new HttpResponseMessage(HttpStatusCode.NotFound));
	}
}
