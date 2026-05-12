using Cockpit.Features.Skills;
using Shouldly;

namespace Cockpit.UnitTests.Features.Skills;

public sealed class SkillFileReaderTests
{
	// ── StripFrontmatter — no front-matter ────────────────────────────────────

	[Fact]
	public void StripFrontmatter_NoFrontmatter_ReturnsContentUnchanged()
	{
		string input = "# Hello\n\nBody text here.";

		string result = SkillFileReader.StripFrontmatter(input);

		result.ShouldBe("# Hello\n\nBody text here.");
	}

	// ── StripFrontmatter — valid YAML front-matter ────────────────────────────

	[Fact]
	public void StripFrontmatter_ValidFrontmatter_ReturnsBodyOnly()
	{
		string input = "---\ntitle: My Skill\ndescription: Does things\n---\n# Body\n\nSome content.";

		string result = SkillFileReader.StripFrontmatter(input);

		result.ShouldBe("# Body\n\nSome content.");
	}

	// ── StripFrontmatter — BOM stripped ───────────────────────────────────────

	[Fact]
	public void StripFrontmatter_WithBom_StripsBom()
	{
		string input = "\uFEFF# Content";

		string result = SkillFileReader.StripFrontmatter(input);

		result.ShouldBe("# Content");
	}

	// ── StripFrontmatter — empty input ────────────────────────────────────────

	[Fact]
	public void StripFrontmatter_EmptyString_ReturnsEmpty()
	{
		string result = SkillFileReader.StripFrontmatter(string.Empty);

		result.ShouldBeEmpty();
	}

	// ── StripFrontmatter — front-matter open but no closing marker ───────────

	[Fact]
	public void StripFrontmatter_OpenFrontmatterNoClose_ReturnsFullContent()
	{
		string input = "---\ntitle: My Skill\n# Body";

		string result = SkillFileReader.StripFrontmatter(input);

		// No closing "---" found — return everything as-is.
		result.ShouldBe(input);
	}

	// ── StripFrontmatter — blank lines between front-matter and body ──────────

	[Fact]
	public void StripFrontmatter_BlankLinesAfterFrontmatter_StripsBlankLines()
	{
		string input = "---\ntitle: Skill\n---\n\n\n# Body";

		string result = SkillFileReader.StripFrontmatter(input);

		result.ShouldBe("# Body");
	}

	// ── StripFrontmatter — CRLF line endings normalised ──────────────────────

	[Fact]
	public void StripFrontmatter_CrlfLineEndings_NormalisedAndParsedCorrectly()
	{
		string input = "---\r\ntitle: Skill\r\n---\r\n# Body";

		string result = SkillFileReader.StripFrontmatter(input);

		result.ShouldBe("# Body");
	}

	// ── StripFrontmatter — no leading dashes → not treated as front-matter ────

	[Fact]
	public void StripFrontmatter_ContentNotStartingWithDashes_ReturnsContentUnchanged()
	{
		string input = "Some intro\n---\nNot front-matter\n---\nBody";

		string result = SkillFileReader.StripFrontmatter(input);

		result.ShouldBe(input);
	}
}
