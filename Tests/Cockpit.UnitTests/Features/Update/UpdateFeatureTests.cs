using System.Text.Json;
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

	static readonly JsonSerializerOptions jsonOptions = new()
	{
		PropertyNameCaseInsensitive = true,
		PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower
	};

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
		GitHubReleaseModel? release = JsonSerializer.Deserialize<GitHubReleaseModel>(sampleReleaseJson, jsonOptions);

		release.ShouldNotBeNull();
		release.TagName.ShouldBe("1.8.0");
	}

	[Fact]
	public void GitHubReleaseModel_Deserializes_Name()
	{
		GitHubReleaseModel? release = JsonSerializer.Deserialize<GitHubReleaseModel>(sampleReleaseJson, jsonOptions);

		release.ShouldNotBeNull();
		release.Name.ShouldBe("1.8.0");
	}

	[Fact]
	public void GitHubReleaseModel_Deserializes_HtmlUrl()
	{
		GitHubReleaseModel? release = JsonSerializer.Deserialize<GitHubReleaseModel>(sampleReleaseJson, jsonOptions);

		release.ShouldNotBeNull();
		release.HtmlUrl.ShouldBe("https://github.com/IeuanWalker/Cockpit/releases/tag/1.8.0");
	}

	[Fact]
	public void GitHubReleaseModel_Deserializes_Body()
	{
		GitHubReleaseModel? release = JsonSerializer.Deserialize<GitHubReleaseModel>(sampleReleaseJson, jsonOptions);

		release.ShouldNotBeNull();
		release.Body.ShouldNotBeNull();
		release.Body.ShouldContain("https://github.com/IeuanWalker/Cockpit/compare/1.7.0...1.8.0");
	}

	[Fact]
	public void GitHubReleaseModel_Deserializes_Draft_AsFalse()
	{
		GitHubReleaseModel? release = JsonSerializer.Deserialize<GitHubReleaseModel>(sampleReleaseJson, jsonOptions);

		release.ShouldNotBeNull();
		release.Draft.ShouldBeFalse();
	}

	[Fact]
	public void GitHubReleaseModel_Deserializes_Prerelease_AsFalse()
	{
		GitHubReleaseModel? release = JsonSerializer.Deserialize<GitHubReleaseModel>(sampleReleaseJson, jsonOptions);

		release.ShouldNotBeNull();
		release.Prerelease.ShouldBeFalse();
	}

	[Fact]
	public void GitHubReleaseModel_Deserializes_PublishedAt()
	{
		GitHubReleaseModel? release = JsonSerializer.Deserialize<GitHubReleaseModel>(sampleReleaseJson, jsonOptions);

		release.ShouldNotBeNull();
		release.PublishedAt.ShouldBe(new DateTime(2026, 3, 30, 0, 21, 29, DateTimeKind.Utc));
	}

	[Fact]
	public void GitHubReleaseModel_Deserializes_TwoAssets()
	{
		GitHubReleaseModel? release = JsonSerializer.Deserialize<GitHubReleaseModel>(sampleReleaseJson, jsonOptions);

		release.ShouldNotBeNull();
		release.Assets.ShouldNotBeNull();
		release.Assets.Count.ShouldBe(2);
	}

	[Fact]
	public void GitHubReleaseModel_Deserializes_SetupExeAsset()
	{
		GitHubReleaseModel? release = JsonSerializer.Deserialize<GitHubReleaseModel>(sampleReleaseJson, jsonOptions);

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
		GitHubReleaseModel? release = JsonSerializer.Deserialize<GitHubReleaseModel>(sampleReleaseJson, jsonOptions);

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
		Should.NotThrow(() => JsonSerializer.Deserialize<GitHubReleaseModel>(sampleReleaseJson, jsonOptions));
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
		GitHubReleaseModel? release = JsonSerializer.Deserialize<GitHubReleaseModel>(sampleReleaseJson, jsonOptions);

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
}
