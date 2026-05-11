using Cockpit.Features.Markdown;
using Shouldly;

namespace Cockpit.UnitTests.Features.Markdown;

public sealed class MarkdownFeatureTests
{
	readonly MarkdownFeature _sut = new();

	// ── Empty / null input ──────────────────────────────────────────────────

	[Fact]
	public void ToHtml_NullString_ReturnsEmpty()
	{
		string result = _sut.ToHtml(null);
		result.ShouldBeEmpty();
	}

	[Fact]
	public void ToHtml_EmptyString_ReturnsEmpty()
	{
		string result = _sut.ToHtml(string.Empty);
		result.ShouldBeEmpty();
	}

	[Fact]
	public void ToHtml_WhitespaceOnly_ReturnsEmpty()
	{
		string result = _sut.ToHtml("   \t\n  ");
		result.ShouldBeEmpty();
	}

	// ── Basic block elements ────────────────────────────────────────────────

	[Theory]
	[InlineData("# H1", "<h1", ">H1</h1>")]
	[InlineData("## H2", "<h2", ">H2</h2>")]
	[InlineData("### H3", "<h3", ">H3</h3>")]
	public void ToHtml_Headings_ProducesCorrectTag(string markdown, string expectedTag, string expectedContent)
	{
		string html = _sut.ToHtml(markdown);
		html.ShouldContain(expectedTag);
		html.ShouldContain(expectedContent);
	}

	[Fact]
	public void ToHtml_BoldText_ProducesStrongTag()
	{
		string html = _sut.ToHtml("**bold**");
		html.ShouldContain("<strong>bold</strong>");
	}

	[Fact]
	public void ToHtml_ItalicText_ProducesEmTag()
	{
		string html = _sut.ToHtml("*italic*");
		html.ShouldContain("<em>italic</em>");
	}

	[Fact]
	public void ToHtml_UnorderedList_ProducesUlAndLiTags()
	{
		string html = _sut.ToHtml("- item1\n- item2");
		html.ShouldContain("<ul>");
		html.ShouldContain("<li>item1</li>");
		html.ShouldContain("<li>item2</li>");
	}

	[Fact]
	public void ToHtml_OrderedList_ProducesOlAndLiTags()
	{
		string html = _sut.ToHtml("1. first\n2. second");
		html.ShouldContain("<ol>");
		html.ShouldContain("<li>first</li>");
		html.ShouldContain("<li>second</li>");
	}

	[Fact]
	public void ToHtml_Blockquote_ProducesBlockquoteTag()
	{
		string html = _sut.ToHtml("> quote");
		html.ShouldContain("<blockquote>");
	}

	[Fact]
	public void ToHtml_HorizontalRule_ProducesHrTag()
	{
		string html = _sut.ToHtml("---");
		html.ShouldContain("<hr");
	}

	// ── Code blocks ─────────────────────────────────────────────────────────

	[Fact]
	public void ToHtml_FencedCodeBlockWithLanguage_ProducesPreCodeWithLanguageClass()
	{
		string html = _sut.ToHtml("```csharp\nvar x = 1;\n```");
		html.ShouldContain("<pre");
		html.ShouldContain("language-csharp");
		html.ShouldContain("var x = 1;");
	}

	[Fact]
	public void ToHtml_FencedCodeBlockWithoutLanguage_ProducesPreCode()
	{
		string html = _sut.ToHtml("```\nplain code\n```");
		html.ShouldContain("<pre");
		html.ShouldContain("plain code");
	}

	[Fact]
	public void ToHtml_FencedCodeBlock_WrapsWithCodeBlockDiv()
	{
		string html = _sut.ToHtml("```csharp\nvar x = 1;\n```");
		html.ShouldContain("<div class=\"code-block\">");
		html.ShouldContain("</pre></div>");
	}

	[Fact]
	public void ToHtml_FencedCodeBlock_InsertsCopyButton()
	{
		string html = _sut.ToHtml("```\ncode\n```");
		html.ShouldContain("class=\"code-copy-button\"");
	}

	[Fact]
	public void ToHtml_InlineCode_ProducesCodeTag()
	{
		string html = _sut.ToHtml("use `var x = 1;` inline");
		html.ShouldContain("<code>var x = 1;</code>");
	}

	[Fact]
	public void ToHtml_InlineCode_DoesNotAddCodeBlockWrapper()
	{
		string html = _sut.ToHtml("use `inline` here");
		html.ShouldNotContain("<div class=\"code-block\">");
	}

	// ── Code block with copy button positioning ──────────────────────────────

	[Fact]
	public void ToHtml_CodeBlock_CopyButtonAppearsBeforePreTag()
	{
		string html = _sut.ToHtml("```\ncode\n```");
		int copyButtonPos = html.IndexOf("code-copy-button", StringComparison.Ordinal);
		int prePos = html.IndexOf("<pre", StringComparison.Ordinal);
		copyButtonPos.ShouldBeLessThan(prePos);
	}

	// ── Tables ───────────────────────────────────────────────────────────────

	[Fact]
	public void ToHtml_PipeTable_ProducesTableTag()
	{
		string markdown = "| A | B |\n|---|---|\n| 1 | 2 |";
		string html = _sut.ToHtml(markdown);
		html.ShouldContain("<table>");
		html.ShouldContain("<th>A</th>");
		html.ShouldContain("<td>1</td>");
	}

	// ── Task lists ────────────────────────────────────────────────────────────

	[Fact]
	public void ToHtml_TaskList_ProducesCheckboxInputs()
	{
		string markdown = "- [x] done\n- [ ] todo";
		string html = _sut.ToHtml(markdown);
		html.ShouldContain("type=\"checkbox\"");
		html.ShouldContain("checked");
	}

	// ── Unicode / surrogate handling ─────────────────────────────────────────

	[Fact]
	public void ToHtml_ZeroWidthSpace_IsRemovedBeforeParsing()
	{
		// A zero-width space on a code-fence line would break fence detection
		string markdown = "\u200B```\u200B\ncode\n```";
		string html = _sut.ToHtml(markdown);
		html.ShouldContain("<pre");
		html.ShouldNotContain("\u200B");
	}

	[Fact]
	public void ToHtml_LoneHighSurrogate_IsReplacedWithReplacementCharacter()
	{
		string markdown = "text\uD800end";
		string html = _sut.ToHtml(markdown);
		html.ShouldContain('\uFFFD'.ToString());
		html.ShouldNotContain('\uD800'.ToString());
	}

	[Fact]
	public void ToHtml_LoneLowSurrogate_IsReplacedWithReplacementCharacter()
	{
		string markdown = "text\uDC00end";
		string html = _sut.ToHtml(markdown);
		html.ShouldContain('\uFFFD'.ToString());
	}

	[Fact]
	public void ToHtml_ValidSurrogatePair_IsPreserved()
	{
		// U+1F600 GRINNING FACE — encoded as a surrogate pair in .NET
		string emoji = "\uD83D\uDE00";
		string markdown = $"emoji {emoji} here";
		string html = _sut.ToHtml(markdown);
		html.ShouldContain(emoji);
	}

	// ── HTML injection / XSS ─────────────────────────────────────────────────

	[Fact]
	public void ToHtml_RawHtmlInMarkdown_IsStrippedByDisableHtml()
	{
		// DisableHtml() is configured — raw HTML tags must not pass through
		string html = _sut.ToHtml("<script>alert('xss')</script>");
		html.ShouldNotContain("<script>");
	}

	[Fact]
	public void ToHtml_HtmlEntitiesInText_AreEscaped()
	{
		string html = _sut.ToHtml("<b>not bold</b>");
		html.ShouldNotContain("<b>");
	}

	[Fact]
	public void ToHtml_ScriptTagInCodeFence_IsRenderedAsLiteralText()
	{
		string html = _sut.ToHtml("```\n<script>alert(1)</script>\n```");
		// Inside a code block the content is HTML-encoded
		html.ShouldContain("&lt;script&gt;");
		html.ShouldNotContain("<script>");
	}

	// ── Caching ───────────────────────────────────────────────────────────────

	[Fact]
	public void ToHtml_SameInputCalledTwice_ReturnsSameReference()
	{
		const string markdown = "**cached**";
		string first = _sut.ToHtml(markdown);
		string second = _sut.ToHtml(markdown);
		ReferenceEquals(first, second).ShouldBeTrue();
	}

	[Fact]
	public void ToHtml_DifferentInputs_ReturnDifferentOutputs()
	{
		string a = _sut.ToHtml("# Hello");
		string b = _sut.ToHtml("# World");
		a.ShouldNotBe(b);
	}

	[Fact]
	public void ToHtml_AfterCacheEviction_StillReturnsCorrectHtml()
	{
		// Fill cache beyond MaxCacheEntries (64) to trigger eviction, then verify correctness
		for(int i = 0; i < 70; i++)
		{
			_sut.ToHtml($"entry {i}");
		}

		string result = _sut.ToHtml("**bold after eviction**");
		result.ShouldContain("<strong>bold after eviction</strong>");
	}

	// ── Emoji ─────────────────────────────────────────────────────────────────

	[Fact]
	public void ToHtml_EmojiShortcode_IsExpanded()
	{
		string html = _sut.ToHtml(":smile:");
		// UseEmojiAndSmiley replaces shortcodes — the literal shortcode must not survive
		html.ShouldNotContain(":smile:");
		// And the output must be non-trivial (more than an empty paragraph)
		html.Length.ShouldBeGreaterThan("<p></p>".Length);
	}

	// ── Links ─────────────────────────────────────────────────────────────────

	[Fact]
	public void ToHtml_InlineLink_ProducesAnchorTag()
	{
		string html = _sut.ToHtml("[GitHub](https://github.com)");
		html.ShouldContain("<a");
		html.ShouldContain("href=\"https://github.com\"");
		html.ShouldContain("GitHub");
	}

	// ── Paragraph text ────────────────────────────────────────────────────────

	[Fact]
	public void ToHtml_PlainParagraph_ProducesPTag()
	{
		string html = _sut.ToHtml("Hello world");
		html.ShouldContain("<p>Hello world</p>");
	}

	[Fact]
	public void ToHtml_MultiLineParagraph_TreatedAsHardBreakDueToSoftlineExtension()
	{
		// UseSoftlineBreakAsHardlineBreak — single newline becomes <br>
		string html = _sut.ToHtml("line one\nline two");
		html.ShouldContain("<br");
	}
}
