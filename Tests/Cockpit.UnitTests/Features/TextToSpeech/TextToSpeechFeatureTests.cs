using Cockpit.Features.TextToSpeech;
using Shouldly;

namespace Cockpit.UnitTests.Features.TextToSpeech;

public class TextToSpeechFeatureTests
{
	// -------------------------------------------------------------------------
	// StripMarkdown edge cases
	// -------------------------------------------------------------------------

	[Theory]
	[InlineData("", "")]
	[InlineData("   ", "")]
	[InlineData("\t\n", "")]
	[InlineData("plain text", "plain text")]
	[InlineData("hello **world**", "hello world")]
	[InlineData("hello *world*", "hello world")]
	[InlineData("hello ***world***", "hello world")]
	[InlineData("hello __world__", "hello world")]
	[InlineData("hello _world_", "hello world")]
	[InlineData("hello ___world___", "hello world")]
	[InlineData("`inline code`", "inline code")]
	[InlineData("# Heading 1", "Heading 1")]
	[InlineData("## Heading 2", "Heading 2")]
	[InlineData("###### Heading 6", "Heading 6")]
	[InlineData("[link text](http://example.com)", "link text")]
	[InlineData("![alt text](http://example.com/img.png)", "")]
	[InlineData("> blockquote text", "blockquote text")]
	[InlineData("- unordered item", "- unordered item")]
	[InlineData("* unordered item", "* unordered item")]
	[InlineData("+ unordered item", "+ unordered item")]
	[InlineData("1. ordered item", "1. ordered item")]
	[InlineData("42. ordered item", "42. ordered item")]
	public void StripMarkdown_ReturnsExpected(string input, string expected)
	{
		string result = TextToSpeechFeature.StripMarkdown(input);
		result.ShouldBe(expected);
	}

	[Fact]
	public void StripMarkdown_CodeBlock_ReplacedWithCodeBlockPlaceholder()
	{
		string input = "```\nsome code\n```";
		string result = TextToSpeechFeature.StripMarkdown(input);
		result.ShouldBe("code block");
	}

	[Fact]
	public void StripMarkdown_HorizontalRule_Removed()
	{
		string input = "before\n---\nafter";
		string result = TextToSpeechFeature.StripMarkdown(input);
		result.ShouldNotContain("---");
		result.ShouldContain("before");
		result.ShouldContain("after");
	}

	[Fact]
	public void StripMarkdown_ExcessNewlines_Collapsed()
	{
		string input = "line one\n\n\n\nline two";
		string result = TextToSpeechFeature.StripMarkdown(input);
		result.ShouldBe("line one\n\nline two");
	}

	[Fact]
	public void StripMarkdown_MixedMarkdown_StripsAllFormatting()
	{
		string input = "# Title\n**Bold** and *italic* with `code` and [link](url)";
		string result = TextToSpeechFeature.StripMarkdown(input);
		result.ShouldBe("Title\nBold and italic with code and link");
	}

	[Fact]
	public void StripMarkdown_FencedCodeBlockWithLanguage_ReplacedWithPlaceholder()
	{
		string input = "```csharp\nvar x = 1;\n```";
		string result = TextToSpeechFeature.StripMarkdown(input);
		result.ShouldBe("code block");
	}

	[Fact]
	public void StripMarkdown_MultipleCodeBlocks_EachReplacedWithPlaceholder()
	{
		string input = "```\nblock one\n```\nsome text\n```\nblock two\n```";
		string result = TextToSpeechFeature.StripMarkdown(input);
		result.ShouldContain("code block");
		result.ShouldContain("some text");
		result.ShouldNotContain("block one");
		result.ShouldNotContain("block two");
	}

	[Fact]
	public void StripMarkdown_NestedInlineMarkup_StrippedCompletely()
	{
		string input = "Text with [**bold link**](http://example.com) inline";
		string result = TextToSpeechFeature.StripMarkdown(input);
		result.ShouldBe("Text with bold link inline");
	}

	// -------------------------------------------------------------------------
	// Speak / Stop state-machine tests
	// -------------------------------------------------------------------------

	[Fact]
	public async Task Speak_SetsSpeakingStateWhileActive()
	{
		FakeTextToSpeech fake = new();
		TestableTextToSpeechFeature feature = new(fake);

		Task speak = feature.Speak("msg1", "hello world");
		await fake.WaitForSpeakStartAsync(TestContext.Current.CancellationToken);

		feature.IsSpeaking.ShouldBeTrue();
		feature.ActiveMessageId.ShouldBe("msg1");

		fake.CompleteCurrentSpeech();
		await speak;
	}

	[Fact]
	public async Task Speak_CompletesNaturally_ClearsState()
	{
		FakeTextToSpeech fake = new();
		TestableTextToSpeechFeature feature = new(fake);

		Task speak = feature.Speak("msg1", "hello");
		await fake.WaitForSpeakStartAsync(TestContext.Current.CancellationToken);

		fake.CompleteCurrentSpeech();
		await speak;

		feature.IsSpeaking.ShouldBeFalse();
		feature.ActiveMessageId.ShouldBeNull();
	}

	[Fact]
	public async Task Speak_StripMarkdownApplied_SpeaksPlainText()
	{
		FakeTextToSpeech fake = new();
		TestableTextToSpeechFeature feature = new(fake);

		Task speak = feature.Speak("msg1", "**bold** text");
		await fake.WaitForSpeakStartAsync(TestContext.Current.CancellationToken);

		fake.LastSpokenText.ShouldBe("bold text");

		fake.CompleteCurrentSpeech();
		await speak;
	}

	[Fact]
	public async Task Speak_SameMessageId_TogglesOffSpeech()
	{
		FakeTextToSpeech fake = new();
		TestableTextToSpeechFeature feature = new(fake);

		Task speak = feature.Speak("msg1", "hello");
		await fake.WaitForSpeakStartAsync(TestContext.Current.CancellationToken);

		// Speaking same message ID should stop it
		await feature.Speak("msg1", "hello");
		await speak;

		feature.IsSpeaking.ShouldBeFalse();
		feature.ActiveMessageId.ShouldBeNull();
	}

	[Fact]
	public async Task Speak_ConcurrentCalls_StopsFirstAndStartsSecond()
	{
		FakeTextToSpeech fake = new();
		TestableTextToSpeechFeature feature = new(fake);

		Task speak1 = feature.Speak("msg1", "first message");
		await fake.WaitForSpeakStartAsync(TestContext.Current.CancellationToken);

		feature.ActiveMessageId.ShouldBe("msg1");

		// Start second while first is still running
		Task speak2 = feature.Speak("msg2", "second message");
		await fake.WaitForSpeakStartAsync(TestContext.Current.CancellationToken);

		// First should have been cancelled, second should be active
		await speak1;
		feature.ActiveMessageId.ShouldBe("msg2");

		fake.CompleteCurrentSpeech();
		await speak2;

		feature.IsSpeaking.ShouldBeFalse();
		feature.ActiveMessageId.ShouldBeNull();
	}

	[Fact]
	public async Task Stop_WhileSpeaking_CancelsAndClearsState()
	{
		FakeTextToSpeech fake = new();
		TestableTextToSpeechFeature feature = new(fake);

		Task speak = feature.Speak("msg1", "hello");
		await fake.WaitForSpeakStartAsync(TestContext.Current.CancellationToken);

		feature.IsSpeaking.ShouldBeTrue();

		await feature.Stop();
		await speak;

		feature.IsSpeaking.ShouldBeFalse();
		feature.ActiveMessageId.ShouldBeNull();
	}

	[Fact]
	public async Task Stop_WhenIdle_DoesNotThrow()
	{
		TestableTextToSpeechFeature feature = new(new FakeTextToSpeech());

		feature.IsSpeaking.ShouldBeFalse();

		// Should complete without throwing
		await feature.Stop();

		feature.IsSpeaking.ShouldBeFalse();
	}

	[Fact]
	public async Task Stop_WhenIdle_DoesNotFireOnStateChanged()
	{
		TestableTextToSpeechFeature feature = new(new FakeTextToSpeech());

		int eventCount = 0;
		feature.OnStateChanged += () => eventCount++;

		await feature.Stop();

		eventCount.ShouldBe(0);
	}

	[Fact]
	public async Task Stop_AllowsSubsequentSpeakToSucceed()
	{
		// Tests CancellationTokenSource disposal indirectly: after Stop() the feature
		// must be in a clean enough state for a new Speak() to complete correctly.
		FakeTextToSpeech fake = new();
		TestableTextToSpeechFeature feature = new(fake);

		Task speak1 = feature.Speak("msg1", "hello");
		await fake.WaitForSpeakStartAsync(TestContext.Current.CancellationToken);

		await feature.Stop();
		await speak1;

		feature.IsSpeaking.ShouldBeFalse();

		// A subsequent Speak should work correctly, proving the CTS was properly reset.
		Task speak2 = feature.Speak("msg2", "world");
		await fake.WaitForSpeakStartAsync(TestContext.Current.CancellationToken);

		feature.IsSpeaking.ShouldBeTrue();
		feature.ActiveMessageId.ShouldBe("msg2");

		fake.CompleteCurrentSpeech();
		await speak2;

		feature.IsSpeaking.ShouldBeFalse();
		feature.ActiveMessageId.ShouldBeNull();
	}

	// -------------------------------------------------------------------------
	// OnStateChanged event tests
	// -------------------------------------------------------------------------

	[Fact]
	public async Task OnStateChanged_FiresTwice_WhenSpeechStartsAndEnds()
	{
		FakeTextToSpeech fake = new();
		TestableTextToSpeechFeature feature = new(fake);

		int eventCount = 0;
		feature.OnStateChanged += () => eventCount++;

		Task speak = feature.Speak("msg1", "hello");
		await fake.WaitForSpeakStartAsync(TestContext.Current.CancellationToken);

		eventCount.ShouldBe(1); // fired when speaking started

		fake.CompleteCurrentSpeech();
		await speak;

		eventCount.ShouldBe(2); // fired again when speaking ended
	}

	[Fact]
	public async Task OnStateChanged_FiresOnStop()
	{
		FakeTextToSpeech fake = new();
		TestableTextToSpeechFeature feature = new(fake);

		Task speak = feature.Speak("msg1", "hello");
		await fake.WaitForSpeakStartAsync(TestContext.Current.CancellationToken);

		int eventCount = 0;
		feature.OnStateChanged += () => eventCount++;

		await feature.Stop();
		await speak;

		eventCount.ShouldBe(1);
	}

	// -------------------------------------------------------------------------
	// GetLocales tests
	// -------------------------------------------------------------------------

	[Fact]
	public async Task GetLocales_ReturnsLocalesFromUnderlyingImplementation()
	{
		FakeTextToSpeech fake = new();
		TestableTextToSpeechFeature feature = new(fake);

		IEnumerable<Locale> locales = await feature.GetLocales();

		locales.ShouldNotBeNull();
	}

	// -------------------------------------------------------------------------
	// ITextToSpeechFeature interface tests
	// -------------------------------------------------------------------------

	[Fact]
	public void Feature_ImplementsInterface()
	{
		FakeTextToSpeech fake = new();
		TestableTextToSpeechFeature feature = new(fake);

		feature.ShouldBeAssignableTo<ITextToSpeechFeature>();
	}
}

