using Cockpit.Features.Agents;
using Cockpit.Features.Agents.Models;
using Cockpit.Features.Sessions.Models;
using GitHub.Copilot;
using Shouldly;

namespace Cockpit.UnitTests.Features.Agents;

public sealed class AgentFeatureTests : IDisposable
{
	static readonly ModelInfo testModel = new() { Id = "test", Name = "Test Model" };
	readonly string _tempDir;

	public AgentFeatureTests()
	{
		_tempDir = Path.Combine(Path.GetTempPath(), "CockpitTests_" + Guid.NewGuid().ToString("N"));
		Directory.CreateDirectory(_tempDir);
	}

	public void Dispose()
	{
		if(Directory.Exists(_tempDir))
		{
			Directory.Delete(_tempDir, recursive: true);
		}
	}

	static SessionModel MakeSession(string? workspacePath) => new()
	{
		Id = Guid.NewGuid().ToString(),
		Title = "Test",
		Status = SessionStatusEnum.Idle,
		CreatedAt = DateTime.UtcNow,
		LastActivity = DateTime.UtcNow,
		Model = testModel,
		Context = new()
		{
			CurrentWorkingDirectory = workspacePath ?? string.Empty,
			WorkspacePath = workspacePath,
			GitRoot = null,
			Branch = null,
			Repository = null
		}
	};

	// ── DetermineSource ────────────────────────────────────────────────────────

	[Theory]
	[InlineData(null, null)]
	[InlineData("", null)]
	[InlineData(null, "")]
	[InlineData("", "")]
	public void DetermineSource_NullOrEmptyPathOrRoot_ReturnsGlobal(string? path, string? gitRoot)
	{
		AgentSource result = AgentFeature.DetermineSource(path, gitRoot);

		result.ShouldBe(AgentSource.Global);
	}

	[Fact]
	public void DetermineSource_PathUnderGitRoot_ReturnsRepo()
	{
		string gitRoot = Path.GetTempPath();
		string agentPath = Path.Combine(gitRoot, ".github", "agents", "my-agent.agent.md");

		AgentSource result = AgentFeature.DetermineSource(agentPath, gitRoot);

		result.ShouldBe(AgentSource.Repo);
	}

	[Fact]
	public void DetermineSource_PathIsGitRoot_ReturnsRepo()
	{
		string gitRoot = Path.GetTempPath();

		AgentSource result = AgentFeature.DetermineSource(gitRoot, gitRoot);

		result.ShouldBe(AgentSource.Repo);
	}

	[Fact]
	public void DetermineSource_PathOutsideGitRoot_ReturnsGlobal()
	{
		string gitRoot = Path.GetTempPath();
		string agentPath = Path.Combine(Path.GetPathRoot(gitRoot) ?? "/", "some", "other", "path", "agent.md");

		AgentSource result = AgentFeature.DetermineSource(agentPath, gitRoot);

		result.ShouldBe(AgentSource.Global);
	}

	[Theory]
	[InlineData(null)]
	[InlineData("")]
	public void DetermineSource_ValidPathButNoGitRoot_ReturnsGlobal(string? gitRoot)
	{
		string agentPath = Path.Combine(Path.GetTempPath(), "agents", "my-agent.agent.md");

		AgentSource result = AgentFeature.DetermineSource(agentPath, gitRoot);

		result.ShouldBe(AgentSource.Global);
	}

	// ── ReadUserInvocable ──────────────────────────────────────────────────────

	[Fact]
	public void ReadUserInvocable_NoFrontmatter_ReturnsTrue()
	{
		string filePath = Path.Combine(_tempDir, "agent.md");
		File.WriteAllText(filePath, "# My Agent\n\nSome content.");

		bool result = AgentFeature.ReadUserInvocable(filePath);

		result.ShouldBeTrue();
	}

	[Fact]
	public void ReadUserInvocable_FrontmatterWithoutKey_ReturnsTrue()
	{
		string filePath = Path.Combine(_tempDir, "agent.md");
		File.WriteAllText(filePath, "---\nname: my-agent\ndescription: Hello\n---\n\nBody.");

		bool result = AgentFeature.ReadUserInvocable(filePath);

		result.ShouldBeTrue();
	}

	[Fact]
	public void ReadUserInvocable_FrontmatterUserInvocableFalse_ReturnsFalse()
	{
		string filePath = Path.Combine(_tempDir, "agent.md");
		File.WriteAllText(filePath, "---\nname: my-agent\nuser-invocable: false\n---\n\nBody.");

		bool result = AgentFeature.ReadUserInvocable(filePath);

		result.ShouldBeFalse();
	}

	[Fact]
	public void ReadUserInvocable_FrontmatterUserInvocableTrue_ReturnsTrue()
	{
		string filePath = Path.Combine(_tempDir, "agent.md");
		File.WriteAllText(filePath, "---\nname: my-agent\nuser-invocable: true\n---\n\nBody.");

		bool result = AgentFeature.ReadUserInvocable(filePath);

		result.ShouldBeTrue();
	}

	[Fact]
	public void ReadUserInvocable_UserInvocableFalse_CaseInsensitive_ReturnsFalse()
	{
		string filePath = Path.Combine(_tempDir, "agent.md");
		File.WriteAllText(filePath, "---\nUser-Invocable: FALSE\n---\n\nBody.");

		bool result = AgentFeature.ReadUserInvocable(filePath);

		result.ShouldBeFalse();
	}

	[Fact]
	public void ReadUserInvocable_FileDoesNotExist_ReturnsTrue()
	{
		string filePath = Path.Combine(_tempDir, "nonexistent.md");

		bool result = AgentFeature.ReadUserInvocable(filePath);

		result.ShouldBeTrue();
	}

	[Fact]
	public void ReadUserInvocable_FileWithBom_ParsesCorrectly()
	{
		string filePath = Path.Combine(_tempDir, "agent.md");
		File.WriteAllText(filePath, "\uFEFF---\nuser-invocable: false\n---\n\nBody.");

		bool result = AgentFeature.ReadUserInvocable(filePath);

		result.ShouldBeFalse();
	}

	[Fact]
	public void ReadUserInvocable_UnclosedFrontmatter_ReturnsTrue()
	{
		string filePath = Path.Combine(_tempDir, "agent.md");
		File.WriteAllText(filePath, "---\nuser-invocable: false\nNo closing dashes.");

		bool result = AgentFeature.ReadUserInvocable(filePath);

		result.ShouldBeTrue();
	}

	[Fact]
	public void ReadUserInvocable_EmptyFrontmatter_ReturnsTrue()
	{
		string filePath = Path.Combine(_tempDir, "agent.md");
		File.WriteAllText(filePath, "---\n---\n\nBody.");

		bool result = AgentFeature.ReadUserInvocable(filePath);

		result.ShouldBeTrue();
	}

	// ── AgentProfile.DisplayLabel ──────────────────────────────────────────────

	[Fact]
	public void AgentProfile_DisplayLabel_FallsBackToName_WhenDisplayNameIsNull()
	{
		AgentProfile profile = new() { Name = "my-agent", Source = AgentSource.Global };

		profile.DisplayLabel.ShouldBe("my-agent");
	}

	[Fact]
	public void AgentProfile_DisplayLabel_ReturnsDisplayName_WhenSet()
	{
		AgentProfile profile = new() { Name = "my-agent", DisplayName = "My Agent", Source = AgentSource.Global };

		profile.DisplayLabel.ShouldBe("My Agent");
	}

	[Fact]
	public void AgentProfile_UserInvocable_DefaultsToTrue()
	{
		AgentProfile profile = new() { Name = "x", Source = AgentSource.Global };

		profile.UserInvocable.ShouldBeTrue();
	}

	// ── AgentPersistence.GetAgentFilePath ─────────────────────────────────────

	[Theory]
	[InlineData(null)]
	[InlineData("")]
	[InlineData("   ")]
	public void GetAgentFilePath_NullOrEmptyWorkspace_ReturnsNull(string? workspacePath)
	{
		AgentPersistence persistence = new();
		SessionModel session = MakeSession(workspacePath ?? string.Empty);
		session.Context.WorkspacePath = workspacePath;

		string? result = persistence.GetAgentFilePath(session);

		result.ShouldBeNull();
	}

	[Fact]
	public void GetAgentFilePath_ValidWorkspace_ReturnsExpectedPath()
	{
		AgentPersistence persistence = new();
		SessionModel session = MakeSession(_tempDir);

		string? result = persistence.GetAgentFilePath(session);

		result.ShouldBe(Path.Combine(_tempDir, "Cockpit", "session-agent.json"));
	}

	// ── AgentPersistence round-trip ────────────────────────────────────────────

	[Fact]
	public async Task SaveAndRestore_WithAgent_RestoresCorrectly()
	{
		AgentPersistence persistence = new();
		SessionModel session = MakeSession(_tempDir);
		AgentProfile agent = new() { Name = "code-review", Source = AgentSource.Repo };
		session.Context.Agents = [agent];
		session.Context.SelectedAgent = agent;

		await persistence.SaveSessionAgent(session);

		SessionModel restoreTarget = MakeSession(_tempDir);
		AgentProfile available = new() { Name = "code-review", Source = AgentSource.Repo };
		restoreTarget.Context.Agents = [available];

		bool restored = await persistence.TryRestoreSessionAgent(restoreTarget);

		restored.ShouldBeTrue();
		restoreTarget.Context.SelectedAgent.ShouldNotBeNull();
		restoreTarget.Context.SelectedAgent!.Name.ShouldBe("code-review");
		restoreTarget.Context.SelectedAgent.Source.ShouldBe(AgentSource.Repo);
	}

	[Fact]
	public async Task SaveAndRestore_NullAgent_ReturnsFalse()
	{
		AgentPersistence persistence = new();
		SessionModel session = MakeSession(_tempDir);
		session.Context.SelectedAgent = null;

		await persistence.SaveSessionAgent(session);

		SessionModel restoreTarget = MakeSession(_tempDir);

		// Empty AgentName in the file → cannot restore → returns false
		bool restored = await persistence.TryRestoreSessionAgent(restoreTarget);

		restored.ShouldBeFalse();
	}

	[Fact]
	public async Task TryRestore_FileDoesNotExist_ReturnsFalse()
	{
		AgentPersistence persistence = new();
		SessionModel session = MakeSession(_tempDir);

		bool result = await persistence.TryRestoreSessionAgent(session);

		result.ShouldBeFalse();
	}

	[Fact]
	public async Task TryRestore_CorruptJson_ReturnsFalse()
	{
		AgentPersistence persistence = new();
		SessionModel session = MakeSession(_tempDir);
		string agentFilePath = persistence.GetAgentFilePath(session)!;
		Directory.CreateDirectory(Path.GetDirectoryName(agentFilePath)!);
		await File.WriteAllTextAsync(agentFilePath, "not valid json {{{", TestContext.Current.CancellationToken);

		bool result = await persistence.TryRestoreSessionAgent(session);

		result.ShouldBeFalse();
	}

	[Fact]
	public async Task TryRestore_AgentNotInList_SetsSelectedToNull()
	{
		AgentPersistence persistence = new();
		SessionModel session = MakeSession(_tempDir);
		session.Context.SelectedAgent = new() { Name = "missing-agent", Source = AgentSource.Repo };

		await persistence.SaveSessionAgent(session);

		SessionModel restoreTarget = MakeSession(_tempDir);
		restoreTarget.Context.Agents = []; // agent not in the available list

		bool restored = await persistence.TryRestoreSessionAgent(restoreTarget);

		restored.ShouldBeTrue();
		restoreTarget.Context.SelectedAgent.ShouldBeNull();
	}

	[Fact]
	public async Task TryRestore_FallsBackToNameMatch_WhenSourceDoesNotMatch()
	{
		AgentPersistence persistence = new();
		SessionModel session = MakeSession(_tempDir);
		session.Context.SelectedAgent = new() { Name = "my-agent", Source = AgentSource.Repo };

		await persistence.SaveSessionAgent(session);

		// Available agent has same name but different source
		SessionModel restoreTarget = MakeSession(_tempDir);
		AgentProfile globalAgent = new() { Name = "my-agent", Source = AgentSource.Global };
		restoreTarget.Context.Agents = [globalAgent];

		bool restored = await persistence.TryRestoreSessionAgent(restoreTarget);

		restored.ShouldBeTrue();
		restoreTarget.Context.SelectedAgent.ShouldNotBeNull();
		restoreTarget.Context.SelectedAgent!.Name.ShouldBe("my-agent");
	}

	[Fact]
	public async Task TryRestore_PrefersSourceExactMatchOverNameOnly()
	{
		AgentPersistence persistence = new();
		SessionModel session = MakeSession(_tempDir);
		session.Context.SelectedAgent = new() { Name = "shared-agent", Source = AgentSource.Repo };

		await persistence.SaveSessionAgent(session);

		AgentProfile globalAgent = new() { Name = "shared-agent", Source = AgentSource.Global };
		AgentProfile repoAgent = new() { Name = "shared-agent", Source = AgentSource.Repo };

		SessionModel restoreTarget = MakeSession(_tempDir);
		restoreTarget.Context.Agents = [globalAgent, repoAgent];

		bool restored = await persistence.TryRestoreSessionAgent(restoreTarget);

		restored.ShouldBeTrue();
		restoreTarget.Context.SelectedAgent!.Source.ShouldBe(AgentSource.Repo);
	}

	[Fact]
	public async Task SaveSessionAgent_NoWorkspace_DoesNotThrow()
	{
		AgentPersistence persistence = new();
		SessionModel session = MakeSession(null);

		// Should silently return without writing anything
		await persistence.SaveSessionAgent(session);

		// No file should have been written
		persistence.GetAgentFilePath(session).ShouldBeNull();
	}
}

