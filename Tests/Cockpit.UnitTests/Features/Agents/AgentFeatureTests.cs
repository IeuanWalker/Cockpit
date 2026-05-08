using Cockpit.Features.Agents;
using Cockpit.Features.Agents.Models;
using Shouldly;

namespace Cockpit.UnitTests.Features.Agents;

public sealed class AgentFeatureTests
{
	// ── DetermineSource — null/empty path → Global (defensive default) ────────

	[Fact]
	public void DetermineSource_NullPath_ReturnsGlobal()
	{
		AgentSource result = AgentFeature.DetermineSource(null, null);

		result.ShouldBe(AgentSource.Global);
	}

	[Fact]
	public void DetermineSource_EmptyPath_ReturnsGlobal()
	{
		AgentSource result = AgentFeature.DetermineSource(string.Empty, null);

		result.ShouldBe(AgentSource.Global);
	}

	// ── DetermineSource — path under gitRoot → Repo ───────────────────────────

	[Fact]
	public void DetermineSource_PathUnderGitRoot_ReturnsRepo()
	{
		string gitRoot = Path.GetTempPath();
		string agentPath = Path.Combine(gitRoot, ".github", "agents", "my-agent.agent.md");

		AgentSource result = AgentFeature.DetermineSource(agentPath, gitRoot);

		result.ShouldBe(AgentSource.Repo);
	}

	// ── DetermineSource — path outside gitRoot → Global ──────────────────────

	[Fact]
	public void DetermineSource_PathOutsideGitRoot_ReturnsGlobal()
	{
		string gitRoot = Path.GetTempPath();
		string agentPath = Path.Combine(Path.GetPathRoot(gitRoot) ?? "/", "some", "other", "path", "agent.md");

		AgentSource result = AgentFeature.DetermineSource(agentPath, gitRoot);

		result.ShouldBe(AgentSource.Global);
	}

	// ── DetermineSource — no gitRoot → Global ─────────────────────────────────

	[Fact]
	public void DetermineSource_NoGitRoot_ReturnsGlobal()
	{
		string agentPath = Path.Combine(Path.GetTempPath(), "agents", "my-agent.agent.md");

		AgentSource result = AgentFeature.DetermineSource(agentPath, null);

		result.ShouldBe(AgentSource.Global);
	}

	// ── DetermineSource — empty gitRoot → Global ──────────────────────────────

	[Fact]
	public void DetermineSource_EmptyGitRoot_ReturnsGlobal()
	{
		string agentPath = Path.Combine(Path.GetTempPath(), "agents", "my-agent.agent.md");

		AgentSource result = AgentFeature.DetermineSource(agentPath, string.Empty);

		result.ShouldBe(AgentSource.Global);
	}
}
