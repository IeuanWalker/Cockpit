using Cockpit.Components.Pages.SessionsPanel;
using Cockpit.Features.Sessions.Models;
using GitHub.Copilot;
using Shouldly;

namespace Cockpit.UnitTests.Components.SessionsPanel;

public class SessionProjectIdentityResolverTests
{
	static readonly ModelInfo testModel = new() { Id = "test", Name = "Test" };

	[Fact]
	public void Resolve_WithoutAWorkingDirectory_ReturnsNull()
	{
		SessionModel session = CreateSession(null, gitRoot: ProjectPath("Cockpit"));

		SessionProjectIdentityResolver.Resolve(session).ShouldBeNull();
	}

	[Fact]
	public void Resolve_WithAQualifiedRepository_UsesRepositoryIdentityAndKeepsTheGitRootLocation()
	{
		string projectPath = ProjectPath("Cockpit");
		SessionModel session = CreateSession(
			Path.Combine(projectPath, "src"),
			gitRoot: projectPath + Path.DirectorySeparatorChar,
			repository: "IeuanWalker/Cockpit");

		SessionProjectIdentity identity = SessionProjectIdentityResolver.Resolve(session).ShouldNotBeNull();

		identity.Id.ShouldBe("repo:IEUANWALKER/COCKPIT");
		identity.RootId.ShouldBe(ProjectId(projectPath));
		identity.RepositoryId.ShouldBe("repo:IEUANWALKER/COCKPIT");
		identity.RootPath.ShouldBe(projectPath);
		identity.BaseName.ShouldBe("Cockpit");
		identity.Repository.ShouldBe("IeuanWalker/Cockpit");
	}

	[Fact]
	public void Resolve_WithoutAGitRoot_UsesTheCompleteWorkingDirectory()
	{
		string projectPath = ProjectPath("Scratch");
		SessionModel session = CreateSession(projectPath + Path.DirectorySeparatorChar);

		SessionProjectIdentity identity = SessionProjectIdentityResolver.Resolve(session).ShouldNotBeNull();

		identity.Id.ShouldBe(ProjectId(projectPath));
		identity.RootId.ShouldBe(ProjectId(projectPath));
		identity.RepositoryId.ShouldBeNull();
		identity.RootPath.ShouldBe(projectPath);
		identity.BaseName.ShouldBe("Scratch");
	}

	[Fact]
	public void Resolve_NormalizesRelativeSegmentsAndDirectorySeparators()
	{
		string path = Path.Combine(Path.GetTempPath(), "parent", "..", "project") + Path.AltDirectorySeparatorChar;
		SessionModel session = CreateSession(path);
		string expectedPath = Path.Combine(Path.GetTempPath(), "project");

		SessionProjectIdentity identity = SessionProjectIdentityResolver.Resolve(session).ShouldNotBeNull();

		identity.RootPath.ShouldBe(expectedPath);
		identity.Id.ShouldBe(ProjectId(expectedPath));
	}

	[Fact]
	public void NormalizePath_PreservesTheFilesystemRoot()
	{
		string filesystemRoot = Path.GetPathRoot(Path.GetFullPath("."))!;

		SessionProjectIdentityResolver.NormalizePath(filesystemRoot).ShouldBe(filesystemRoot);
	}

	[Fact]
	public void NormalizePath_PreservesAUncShareRootOnWindows()
	{
		if(!OperatingSystem.IsWindows())
		{
			return;
		}

		const string uncRoot = "\\\\server\\share\\";

		SessionProjectIdentityResolver.NormalizePath(uncRoot).ShouldBe(uncRoot);
	}

	[Fact]
	public void Resolve_UsesTheSamePrefixForGitAndNonGitLocations()
	{
		string projectPath = ProjectPath("Cockpit");
		SessionProjectIdentity gitProject = SessionProjectIdentityResolver.Resolve(
			CreateSession(Path.Combine(projectPath, "src"), gitRoot: projectPath)).ShouldNotBeNull();
		SessionProjectIdentity nonGitProject = SessionProjectIdentityResolver.Resolve(
			CreateSession(projectPath)).ShouldNotBeNull();

		gitProject.Id.ShouldBe(nonGitProject.Id);
	}

	[Fact]
	public void Resolve_QualifiedRepositoryIsStableAcrossCheckoutAndWorktreeRoots()
	{
		SessionProjectIdentity checkout = SessionProjectIdentityResolver.Resolve(CreateSession(
			ProjectPath("Cockpit"),
			gitRoot: ProjectPath("Cockpit"),
			repository: "IeuanWalker/Cockpit")).ShouldNotBeNull();
		SessionProjectIdentity worktree = SessionProjectIdentityResolver.Resolve(CreateSession(
			ProjectPath("worktrees", "feature"),
			gitRoot: ProjectPath("worktrees", "feature"),
			repository: "ieuanwalker/cockpit.git")).ShouldNotBeNull();

		checkout.Id.ShouldBe(worktree.Id);
		checkout.RootId.ShouldNotBe(worktree.RootId);
		checkout.BaseName.ShouldBe("Cockpit");
	}

	[Fact]
	public void Resolve_WhenGitRootIsMalformed_FallsBackToTheWorkingDirectory()
	{
		string workingDirectory = ProjectPath("Cockpit");
		SessionModel session = CreateSession(workingDirectory, gitRoot: "invalid\0git-root");

		SessionProjectIdentity identity = SessionProjectIdentityResolver.Resolve(session).ShouldNotBeNull();

		identity.RootPath.ShouldBe(workingDirectory);
		identity.Id.ShouldBe(ProjectId(workingDirectory));
	}

	[Fact]
	public void Resolve_CanonicalizesIdentityCasingOnWindows()
	{
		string projectPath = ProjectPath("MixedCase");
		SessionProjectIdentity original = SessionProjectIdentityResolver.Resolve(CreateSession(projectPath)).ShouldNotBeNull();
		SessionProjectIdentity changedCase = SessionProjectIdentityResolver.Resolve(CreateSession(projectPath.ToUpperInvariant())).ShouldNotBeNull();

		if(OperatingSystem.IsWindows())
		{
			original.Id.ShouldBe(changedCase.Id);
			SessionProjectIdentityResolver.ProjectIdComparer.ShouldBe(StringComparer.OrdinalIgnoreCase);
			SessionProjectIdentityResolver.PathComparer.ShouldBe(StringComparer.OrdinalIgnoreCase);
		}
		else
		{
			original.Id.ShouldNotBe(changedCase.Id);
			SessionProjectIdentityResolver.ProjectIdComparer.ShouldBe(StringComparer.Ordinal);
			SessionProjectIdentityResolver.PathComparer.ShouldBe(StringComparer.Ordinal);
		}
	}

	static string ProjectPath(params string[] segments) => Path.Combine([Path.GetTempPath(), "CockpitTests", .. segments]);
	static string ProjectId(string path) => $"path:{(OperatingSystem.IsWindows() ? path.ToUpperInvariant() : path)}";

	static SessionModel CreateSession(string? cwd, string? gitRoot = null, string? repository = null) => new()
	{
		Id = "session",
		Title = "Session",
		CreatedAt = DateTime.UnixEpoch,
		LastActivity = DateTime.UnixEpoch,
		Model = testModel,
		Context = new Cockpit.Features.Sessions.Models.SessionContext
		{
			CurrentWorkingDirectory = cwd,
			WorkspacePath = null,
			GitRoot = gitRoot,
			Repository = repository,
			Branch = null
		}
	};
}
