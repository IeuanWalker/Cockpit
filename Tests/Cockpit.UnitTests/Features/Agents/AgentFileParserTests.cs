using Cockpit.Features.Agents;
using Cockpit.Features.Agents.Models;
using Shouldly;

namespace Cockpit.UnitTests.Features.Agents;

public sealed class AgentFileParserTests : IDisposable
{
	readonly string _tempDir = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());

	public AgentFileParserTests()
	{
		Directory.CreateDirectory(_tempDir);
	}

	public void Dispose()
	{
		Directory.Delete(_tempDir, recursive: true);
	}

	string WriteAgent(string fileName, string content)
	{
		string path = Path.Combine(_tempDir, fileName);
		File.WriteAllText(path, content);
		return path;
	}

	// ── Valid full frontmatter ────────────────────────────────────────────────

	[Fact]
	public async Task TryParse_AllFrontmatterFields_PopulatesConfig()
	{
		string path = WriteAgent("my-agent.agent.md",
			"""
			---
			name: MyAgent
			displayName: My Agent
			description: Does things
			prompt: System prompt here
			infer: true
			tools:
			- tool_a
			- tool_b
			---
			""");

		AgentProfile? result = await AgentFileParser.TryParse(path, AgentSource.Global);

		result.ShouldNotBeNull();
		result.Config.Name.ShouldBe("MyAgent");
		result.Config.DisplayName.ShouldBe("My Agent");
		result.Config.Description.ShouldBe("Does things");
		result.Config.Prompt.ShouldBe("System prompt here");
		result.Config.Infer.ShouldBe(true);
		result.Config.Tools.ShouldNotBeNull();
		result.Config.Tools.ShouldBe(["tool_a", "tool_b"]);
		result.Source.ShouldBe(AgentSource.Global);
		result.FilePath.ShouldBe(path);
	}

	// ── Prompt body overrides frontmatter prompt ─────────────────────────────

	[Fact]
	public async Task TryParse_BodyPresent_BodyOverridesFrontmatterPrompt()
	{
		string path = WriteAgent("override.agent.md",
			"""
			---
			name: Override
			prompt: Frontmatter prompt
			---
			Body prompt wins
			""");

		AgentProfile? result = await AgentFileParser.TryParse(path, AgentSource.Repo);

		result.ShouldNotBeNull();
		result.Config.Prompt.ShouldBe("Body prompt wins");
	}

	[Fact]
	public async Task TryParse_EmptyBody_FrontmatterPromptKept()
	{
		string path = WriteAgent("nofallback.agent.md",
			"""
			---
			name: NoFallback
			prompt: Keep this
			---
			""");

		AgentProfile? result = await AgentFileParser.TryParse(path, AgentSource.Global);

		result.ShouldNotBeNull();
		result.Config.Prompt.ShouldBe("Keep this");
	}

	// ── Tools — inline list ───────────────────────────────────────────────────

	[Fact]
	public async Task TryParse_InlineToolsList_ParsedCorrectly()
	{
		string path = WriteAgent("inline-tools.agent.md",
			"""
			---
			name: InlineTools
			tools: [tool_x, "tool_y", 'tool_z']
			---
			""");

		AgentProfile? result = await AgentFileParser.TryParse(path, AgentSource.Global);

		result.ShouldNotBeNull();
		result.Config.Tools.ShouldBe(["tool_x", "tool_y", "tool_z"]);
	}

	// ── Tools — block list ────────────────────────────────────────────────────

	[Fact]
	public async Task TryParse_BlockToolsList_ParsedCorrectly()
	{
		string path = WriteAgent("block-tools.agent.md",
			"""
			---
			name: BlockTools
			tools:
			- read_file
			- write_file
			- run_terminal_command
			---
			""");

		AgentProfile? result = await AgentFileParser.TryParse(path, AgentSource.Repo);

		result.ShouldNotBeNull();
		result.Config.Tools.ShouldBe(["read_file", "write_file", "run_terminal_command"]);
	}

	// ── Name fallback from filename ───────────────────────────────────────────

	[Fact]
	public async Task TryParse_NoNameInFrontmatter_FallsBackToFilename()
	{
		string path = WriteAgent("my-fallback.agent.md",
			"""
			---
			description: No name here
			---
			""");

		AgentProfile? result = await AgentFileParser.TryParse(path, AgentSource.Global);

		result.ShouldNotBeNull();
		result.Config.Name.ShouldBe("my-fallback");
	}

	// ── Invalid cases ─────────────────────────────────────────────────────────

	[Fact]
	public async Task TryParse_NoFrontmatterDelimiter_ReturnsNull()
	{
		string path = WriteAgent("plain.agent.md",
			"""
			name: NoDelimiters
			Just body text with no frontmatter.
			""");

		AgentProfile? result = await AgentFileParser.TryParse(path, AgentSource.Global);

		result.ShouldBeNull();
	}

	[Fact]
	public async Task TryParse_UnclosedFrontmatter_ReturnsNull()
	{
		string path = WriteAgent("unclosed.agent.md",
			"""
			---
			name: Unclosed
			description: Missing closing delimiter
			""");

		AgentProfile? result = await AgentFileParser.TryParse(path, AgentSource.Global);

		result.ShouldBeNull();
	}

	[Fact]
	public async Task TryParse_EmptyNameAndEmptyFilename_ReturnsNull()
	{
		// Create a file whose double-stripped name is empty (".agent.md → "" after stripping twice)
		string path = WriteAgent(".agent.md",
			"""
			---
			description: anonymous
			---
			""");

		AgentProfile? result = await AgentFileParser.TryParse(path, AgentSource.Global);

		result.ShouldBeNull();
	}

	[Fact]
	public async Task TryParse_NonExistentFile_ReturnsNull()
	{
		string path = Path.Combine(_tempDir, "does-not-exist.agent.md");

		AgentProfile? result = await AgentFileParser.TryParse(path, AgentSource.Global);

		result.ShouldBeNull();
	}

	// ── Source is forwarded ───────────────────────────────────────────────────

	[Theory]
	[InlineData(AgentSource.Global)]
	[InlineData(AgentSource.Repo)]
	public async Task TryParse_Source_ForwardedToProfile(AgentSource source)
	{
		string path = WriteAgent($"src-{source}.agent.md",
			"""
			---
			name: SourceTest
			---
			""");

		AgentProfile? result = await AgentFileParser.TryParse(path, source);

		result.ShouldNotBeNull();
		result.Source.ShouldBe(source);
	}
}
