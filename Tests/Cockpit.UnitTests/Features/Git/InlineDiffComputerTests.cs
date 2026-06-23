using Cockpit.Components.Controls.GitDiff;
using Shouldly;

namespace Cockpit.UnitTests.Features.Git;

public sealed class InlineDiffComputerTests
{
	[Fact]
	public void Compute_EmptyLeft_ReturnsEmptySpans()
	{
		(List<(int Start, int Length)> left, List<(int Start, int Length)> right) = InlineDiffComputer.Compute(string.Empty, "abc");

		left.ShouldBeEmpty();
		right.ShouldBeEmpty();
	}

	[Fact]
	public void Compute_EmptyRight_ReturnsEmptySpans()
	{
		(List<(int Start, int Length)> left, List<(int Start, int Length)> right) = InlineDiffComputer.Compute("abc", string.Empty);

		left.ShouldBeEmpty();
		right.ShouldBeEmpty();
	}

	[Fact]
	public void Compute_IdenticalStrings_ReturnsEmptySpans()
	{
		// Identical strings share all tokens — no diff spans.
		(List<(int Start, int Length)> left, List<(int Start, int Length)> right) = InlineDiffComputer.Compute("abc def", "abc def");

		left.ShouldBeEmpty();
		right.ShouldBeEmpty();
	}

	[Fact]
	public void Compute_WordChangedInContext_HighlightsDiffWord()
	{
		// "x = abc" vs "x = axc" — "x" and " = " are shared tokens; only "abc"/"axc" differ (start=4, length=3).
		(List<(int Start, int Length)> left, List<(int Start, int Length)> right) = InlineDiffComputer.Compute("x = abc", "x = axc");

		left.ShouldContain(s => s.Start == 4 && s.Length == 3);
		right.ShouldContain(s => s.Start == 4 && s.Length == 3);
	}

	[Fact]
	public void Compute_CompletelyDifferentWords_ReturnsEmptySpans_DueToLowSimilarity()
	{
		// Very different strings fall below the similarity threshold.
		(List<(int Start, int Length)> left, List<(int Start, int Length)> right) = InlineDiffComputer.Compute("aaaa", "zzzz");

		left.ShouldBeEmpty();
		right.ShouldBeEmpty();
	}

	[Fact]
	public void Compute_WordChangedAtEnd_SpansPointToChangedWord()
	{
		// "foo bar baz" → "foo bar qux" — only "baz"/"qux" differ.
		(List<(int Start, int Length)> left, List<(int Start, int Length)> right) = InlineDiffComputer.Compute("foo bar baz", "foo bar qux");

		// Left span covers "baz" (start=8, length=3)
		left.ShouldContain(s => s.Start == 8 && s.Length == 3);
		// Right span covers "qux" (start=8, length=3)
		right.ShouldContain(s => s.Start == 8 && s.Length == 3);
	}

	[Fact]
	public void Compute_ExcessivelyLongLines_ReturnsEmptySpans()
	{
		// Lines with more than 200 tokens are skipped for performance.
		string longLeft = string.Join(" ", Enumerable.Repeat("word", 210));
		string longRight = string.Join(" ", Enumerable.Repeat("other", 210));

		(List<(int Start, int Length)> left, List<(int Start, int Length)> right) = InlineDiffComputer.Compute(longLeft, longRight);

		left.ShouldBeEmpty();
		right.ShouldBeEmpty();
	}

	[Fact]
	public void Compute_SpansAreNonOverlapping()
	{
		(List<(int Start, int Length)> left, _) = InlineDiffComputer.Compute(
			"return value + offset;",
			"return value + delta;");

		for(int i = 1; i < left.Count; i++)
		{
			(left[i - 1].Start + left[i - 1].Length).ShouldBeLessThanOrEqualTo(left[i].Start);
		}
	}

	[Fact]
	public void Compute_SpanStartsAndLengthsAreNonNegative()
	{
		(List<(int Start, int Length)> left, List<(int Start, int Length)> right) = InlineDiffComputer.Compute(
			"int x = 1;",
			"int x = 42;");

		foreach((int start, int length) in left.Concat(right))
		{
			start.ShouldBeGreaterThanOrEqualTo(0);
			length.ShouldBeGreaterThan(0);
		}
	}
}
