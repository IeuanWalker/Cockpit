using Cockpit.Features.Git;
using Cockpit.Features.Git.Models;
using Microsoft.Extensions.Logging.Abstractions;
using Shouldly;

namespace Cockpit.UnitTests.Features.Git;

public sealed class GitFeatureTests
{
	// ---------------------------------------------------------------------------
	// Helpers
	// ---------------------------------------------------------------------------

	static GitFeature Create(Func<string, string[], Task<string?>> runner) =>
		new(NullLogger<GitFeature>.Instance, runner);

	// ---------------------------------------------------------------------------
	// ClassifyStatus
	// ---------------------------------------------------------------------------

	[Theory]
	[InlineData('A', ' ', GitFileStatusEnum.Added)]
	[InlineData('A', 'M', GitFileStatusEnum.Added)]
	[InlineData('D', ' ', GitFileStatusEnum.Deleted)]
	[InlineData(' ', 'D', GitFileStatusEnum.Deleted)]
	[InlineData('R', ' ', GitFileStatusEnum.Renamed)]
	[InlineData('M', ' ', GitFileStatusEnum.Modified)]
	[InlineData('M', 'M', GitFileStatusEnum.Modified)]
	[InlineData(' ', 'M', GitFileStatusEnum.Modified)]
	public void ClassifyStatus_ReturnsCorrectEnum(char staged, char unstaged, GitFileStatusEnum expected)
	{
		GitFeature.ClassifyStatus(staged, unstaged).ShouldBe(expected);
	}

	// ---------------------------------------------------------------------------
	// ParsePorcelainOutput
	// ---------------------------------------------------------------------------

	[Fact]
	public void ParsePorcelainOutput_EmptyOutput_ReturnsEmpty()
	{
		List<(string FilePath, GitFileStatusEnum Status)> result =
			GitFeature.ParsePorcelainOutput(string.Empty, "C:\\repo");

		result.ShouldBeEmpty();
	}

	[Fact]
	public void ParsePorcelainOutput_ModifiedFile_ReturnsSingleEntry()
	{
		string output = " M src/Foo.cs";

		List<(string FilePath, GitFileStatusEnum Status)> result =
			GitFeature.ParsePorcelainOutput(output, "C:\\repo");

		result.Count.ShouldBe(1);
		result[0].FilePath.ShouldBe("src/Foo.cs");
		result[0].Status.ShouldBe(GitFileStatusEnum.Modified);
	}

	[Fact]
	public void ParsePorcelainOutput_AddedFile_ReturnsAdded()
	{
		string output = "A  src/New.cs";

		List<(string FilePath, GitFileStatusEnum Status)> result =
			GitFeature.ParsePorcelainOutput(output, "C:\\repo");

		result[0].Status.ShouldBe(GitFileStatusEnum.Added);
	}

	[Fact]
	public void ParsePorcelainOutput_DeletedFile_ReturnsDeleted()
	{
		string output = " D src/Gone.cs";

		List<(string FilePath, GitFileStatusEnum Status)> result =
			GitFeature.ParsePorcelainOutput(output, "C:\\repo");

		result[0].Status.ShouldBe(GitFileStatusEnum.Deleted);
	}

	[Fact]
	public void ParsePorcelainOutput_RenamedFile_TakesNewPath()
	{
		string output = "R  old/Name.cs -> new/Name.cs";

		List<(string FilePath, GitFileStatusEnum Status)> result =
			GitFeature.ParsePorcelainOutput(output, "C:\\repo");

		result[0].FilePath.ShouldBe("new/Name.cs");
		result[0].Status.ShouldBe(GitFileStatusEnum.Renamed);
	}

	[Fact]
	public void ParsePorcelainOutput_UntrackedFile_ReturnsUntracked()
	{
		string output = "?? untracked.cs";

		List<(string FilePath, GitFileStatusEnum Status)> result =
			GitFeature.ParsePorcelainOutput(output, "C:\\repo");

		result[0].FilePath.ShouldBe("untracked.cs");
		result[0].Status.ShouldBe(GitFileStatusEnum.Untracked);
	}

	[Fact]
	public void ParsePorcelainOutput_MultipleFiles_ReturnsAll()
	{
		string output = " M src/A.cs\nA  src/B.cs\n D src/C.cs";

		List<(string FilePath, GitFileStatusEnum Status)> result =
			GitFeature.ParsePorcelainOutput(output, "C:\\repo");

		result.Count.ShouldBe(3);
	}

	[Fact]
	public void ParsePorcelainOutput_TooShortLine_IsSkipped()
	{
		string output = " M\nA  src/B.cs"; // first line has length < 4

		List<(string FilePath, GitFileStatusEnum Status)> result =
			GitFeature.ParsePorcelainOutput(output, "C:\\repo");

		result.Count.ShouldBe(1);
		result[0].FilePath.ShouldBe("src/B.cs");
	}

	[Fact]
	public void ParsePorcelainOutput_GitQuotedPath_IsUnquoted()
	{
		// git quotes paths with spaces or non-ASCII in double-quotes
		string output = "?? \"path with spaces/file.cs\"";

		List<(string FilePath, GitFileStatusEnum Status)> result =
			GitFeature.ParsePorcelainOutput(output, "C:\\repo");

		result.Count.ShouldBe(1);
		result[0].FilePath.ShouldBe("path with spaces/file.cs");
	}

	[Fact]
	public void ParsePorcelainOutput_GitQuotedOctalPath_IsUnescaped()
	{
		// Git encodes non-ASCII as octal e.g. ü = \303\274 in UTF-8
		string output = "?? \"\\303\\274ber/file.cs\"";

		List<(string FilePath, GitFileStatusEnum Status)> result =
			GitFeature.ParsePorcelainOutput(output, "C:\\repo");

		result.Count.ShouldBe(1);
		// The two octal bytes \303\274 decode to the UTF-8 encoding of 'ü'
		result[0].FilePath[0].ShouldBe('\u00C3'); // first byte of ü in Latin-1 view
	}

	// ---------------------------------------------------------------------------
	// GetBranch
	// ---------------------------------------------------------------------------

	[Fact]
	public async Task GetBranch_ValidRepo_ReturnsBranchName()
	{
		GitFeature feature = Create((_, args) =>
			Task.FromResult<string?>(args[0] == "rev-parse" ? "main" : null));

		string? branch = await feature.GetBranch("C:\\repo");

		branch.ShouldBe("main");
	}

	[Fact]
	public async Task GetBranch_DetachedHead_ReturnsHEAD()
	{
		GitFeature feature = Create((_, _) => Task.FromResult<string?>("HEAD"));

		string? branch = await feature.GetBranch("C:\\repo");

		branch.ShouldBe("HEAD");
	}

	[Fact]
	public async Task GetBranch_NotARepo_ReturnsNull()
	{
		GitFeature feature = Create((_, _) => Task.FromResult<string?>(null));

		string? branch = await feature.GetBranch("C:\\notarepo");

		branch.ShouldBeNull();
	}

	// ---------------------------------------------------------------------------
	// GetContext
	// ---------------------------------------------------------------------------

	[Fact]
	public async Task GetContext_ValidRepo_PopulatesAllFields()
	{
		string workingDirectory = Directory.CreateDirectory(Path.Combine(Path.GetTempPath(), $"gitfeature-{Guid.NewGuid():N}")).FullName;
		GitFeature feature = Create((_, args) => args[0] switch
		{
			"rev-parse" when args[1] == "--show-toplevel" => Task.FromResult<string?>("/repo"),
			"rev-parse" when args[1] == "--abbrev-ref"    => Task.FromResult<string?>("feature/my-branch"),
			"remote"                                       => Task.FromResult<string?>("https://github.com/owner/repo.git"),
			_                                              => Task.FromResult<string?>(null)
		});

		try
		{
			GitContext? ctx = await feature.GetContext(workingDirectory);

			ctx.ShouldNotBeNull();
			ctx.IsGitRepo.ShouldBeTrue();
			ctx.Branch.ShouldBe("feature/my-branch");
			ctx.Repository.ShouldBe("repo");
		}
		finally
		{
			Directory.Delete(workingDirectory);
		}
	}

	[Fact]
	public async Task GetContext_SshRemoteUrl_ParsesRepositoryName()
	{
		string workingDirectory = Directory.CreateDirectory(Path.Combine(Path.GetTempPath(), $"gitfeature-{Guid.NewGuid():N}")).FullName;
		GitFeature feature = Create((_, args) => args[0] switch
		{
			"rev-parse" when args[1] == "--show-toplevel" => Task.FromResult<string?>("/repo"),
			"rev-parse"                                    => Task.FromResult<string?>("main"),
			"remote"                                       => Task.FromResult<string?>("git@github.com:owner/my-project.git"),
			_                                              => Task.FromResult<string?>(null)
		});

		try
		{
			GitContext? ctx = await feature.GetContext(workingDirectory);

			ctx.ShouldNotBeNull();
			ctx.Repository.ShouldBe("my-project");
		}
		finally
		{
			Directory.Delete(workingDirectory);
		}
	}

	[Fact]
	public async Task GetContext_NotARepo_ReturnsEmptyContext()
	{
		string workingDirectory = Directory.CreateDirectory(Path.Combine(Path.GetTempPath(), $"gitfeature-{Guid.NewGuid():N}")).FullName;
		GitFeature feature = Create((_, _) => Task.FromResult<string?>(null));

		try
		{
			GitContext? ctx = await feature.GetContext(workingDirectory);

			ctx.ShouldNotBeNull();
			ctx.IsGitRepo.ShouldBeFalse();
			ctx.Branch.ShouldBeNull();
			ctx.Repository.ShouldBeNull();
			ctx.GitRoot.ShouldBeNull();
		}
		finally
		{
			Directory.Delete(workingDirectory);
		}
	}

	[Fact]
	public async Task GetContext_NoRemote_RepositoryIsNull()
	{
		string workingDirectory = Directory.CreateDirectory(Path.Combine(Path.GetTempPath(), $"gitfeature-{Guid.NewGuid():N}")).FullName;
		GitFeature feature = Create((_, args) => args[0] switch
		{
			"rev-parse" when args[1] == "--show-toplevel" => Task.FromResult<string?>("/repo"),
			"rev-parse"                                    => Task.FromResult<string?>("main"),
			_                                              => Task.FromResult<string?>(null) // no remote
		});

		try
		{
			GitContext? ctx = await feature.GetContext(workingDirectory);

			ctx.ShouldNotBeNull();
			ctx.Repository.ShouldBeNull();
			ctx.Branch.ShouldBe("main");
		}
		finally
		{
			Directory.Delete(workingDirectory);
		}
	}

	[Fact]
	public async Task GetContext_NonExistentDirectory_ReturnsNull()
	{
		GitFeature feature = Create((_, _) => Task.FromResult<string?>(null));
		string missingPath = Path.Combine(Path.GetTempPath(), $"gitfeature-missing-{Guid.NewGuid():N}");

		GitContext? ctx = await feature.GetContext(missingPath);

		ctx.ShouldBeNull();
	}

	// ---------------------------------------------------------------------------
	// GetChangedFiles
	// ---------------------------------------------------------------------------

	[Fact]
	public async Task GetChangedFiles_EmptyOutput_ReturnsEmpty()
	{
		GitFeature feature = Create((_, args) =>
			args[0] == "status" ? Task.FromResult<string?>("")
			                    : Task.FromResult<string?>(null));

		List<GitChangedFileModel> files = await feature.GetChangedFiles("C:\\repo");

		files.ShouldBeEmpty();
	}

	[Fact]
	public async Task GetChangedFiles_NullOutput_ReturnsEmpty()
	{
		GitFeature feature = Create((_, _) => Task.FromResult<string?>(null));

		List<GitChangedFileModel> files = await feature.GetChangedFiles("C:\\repo");

		files.ShouldBeEmpty();
	}

	[Fact]
	public async Task GetChangedFiles_ModifiedFile_ReturnsEntry()
	{
		GitFeature feature = Create((_, args) => args[0] switch
		{
			"status" => Task.FromResult<string?>(" M src/Foo.cs"),
			"diff"   => Task.FromResult<string?>("--- a/src/Foo.cs\n+++ b/src/Foo.cs\n@@ -1 +1 @@\n-old\n+new"),
			_        => Task.FromResult<string?>(null)
		});

		List<GitChangedFileModel> files = await feature.GetChangedFiles("C:\\repo");

		files.Count.ShouldBe(1);
		files[0].Name.ShouldBe("Foo.cs");
		files[0].Status.ShouldBe(GitFileStatusEnum.Modified);
	}

	[Fact]
	public async Task GetChangedFiles_RenamedFile_UsesNewPath()
	{
		GitFeature feature = Create((_, args) => args[0] switch
		{
			"status" => Task.FromResult<string?>("R  old/Name.cs -> new/Name.cs"),
			_        => Task.FromResult<string?>(null)
		});

		List<GitChangedFileModel> files = await feature.GetChangedFiles("C:\\repo");

		files.Count.ShouldBe(1);
		files[0].Path.ShouldBe("new/Name.cs");
		files[0].Status.ShouldBe(GitFileStatusEnum.Renamed);
	}

	[Fact]
	public async Task GetChangedFiles_GitNotAvailable_ReturnsEmpty()
	{
		// Simulate git not being available by throwing from the runner.
		GitFeature feature = Create((_, _) => throw new InvalidOperationException("git not found"));

		List<GitChangedFileModel> files = await feature.GetChangedFiles("C:\\repo");

		files.ShouldBeEmpty();
	}
}
