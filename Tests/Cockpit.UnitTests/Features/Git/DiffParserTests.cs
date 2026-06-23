using Cockpit.Components.Controls.GitDiff;
using Cockpit.Components.Controls.GitDiff.Models;
using Shouldly;

namespace Cockpit.UnitTests.Features.Git;

public sealed class DiffParserTests
{
	[Fact]
	public void Parse_NullInput_ReturnsEmptyResult()
	{
		ParsedDiffModel result = DiffParser.Parse(null);

		result.Hunks.ShouldBeEmpty();
		result.LinesAdded.ShouldBe(0);
		result.LinesRemoved.ShouldBe(0);
		result.OldPath.ShouldBe(string.Empty);
		result.NewPath.ShouldBe(string.Empty);
	}

	[Fact]
	public void Parse_EmptyInput_ReturnsEmptyResult()
	{
		ParsedDiffModel result = DiffParser.Parse(string.Empty);

		result.Hunks.ShouldBeEmpty();
	}

	[Fact]
	public void Parse_WhitespaceInput_ReturnsEmptyResult()
	{
		ParsedDiffModel result = DiffParser.Parse("   \n  ");

		result.Hunks.ShouldBeEmpty();
	}

	[Fact]
	public void Parse_SimpleDiff_ExtractsPaths()
	{
		string diff = """
			--- a/src/Foo.cs
			+++ b/src/Foo.cs
			@@ -1,3 +1,3 @@
			 context
			-old line
			+new line
			""";

		ParsedDiffModel result = DiffParser.Parse(diff);

		result.OldPath.ShouldBe("src/Foo.cs");
		result.NewPath.ShouldBe("src/Foo.cs");
	}

	[Fact]
	public void Parse_SimpleDiff_CountsAddedAndRemoved()
	{
		string diff = """
			--- a/src/Foo.cs
			+++ b/src/Foo.cs
			@@ -1,4 +1,4 @@
			 context
			-removed1
			-removed2
			+added1
			+added2
			+added3
			""";

		ParsedDiffModel result = DiffParser.Parse(diff);

		result.LinesAdded.ShouldBe(3);
		result.LinesRemoved.ShouldBe(2);
	}

	[Fact]
	public void Parse_SimpleDiff_PopulatesHunkWithCorrectLines()
	{
		string diff = """
			--- a/file.txt
			+++ b/file.txt
			@@ -1,3 +1,3 @@
			 ctx
			-old
			+new
			""";

		ParsedDiffModel result = DiffParser.Parse(diff);

		result.Hunks.Count.ShouldBe(1);
		DiffHunkModel hunk = result.Hunks[0];
		hunk.OldStartLine.ShouldBe(1);
		hunk.NewStartLine.ShouldBe(1);
		hunk.Lines.Count.ShouldBe(3);
		hunk.Lines[0].Type.ShouldBe(DiffLineTypeEnum.Context);
		hunk.Lines[0].Content.ShouldBe("ctx");
		hunk.Lines[1].Type.ShouldBe(DiffLineTypeEnum.Removed);
		hunk.Lines[1].Content.ShouldBe("old");
		hunk.Lines[2].Type.ShouldBe(DiffLineTypeEnum.Added);
		hunk.Lines[2].Content.ShouldBe("new");
	}

	[Fact]
	public void Parse_MultipleHunks_ParsesAll()
	{
		string diff = """
			--- a/file.txt
			+++ b/file.txt
			@@ -1,3 +1,3 @@
			 ctx1
			-old1
			+new1
			@@ -10,3 +10,3 @@
			 ctx2
			-old2
			+new2
			""";

		ParsedDiffModel result = DiffParser.Parse(diff);

		result.Hunks.Count.ShouldBe(2);
		result.Hunks[0].OldStartLine.ShouldBe(1);
		result.Hunks[1].OldStartLine.ShouldBe(10);
	}

	[Fact]
	public void Parse_HunkHeader_ParsesStartLines()
	{
		string diff = """
			--- a/file.txt
			+++ b/file.txt
			@@ -5,7 +8,3 @@
			 ctx
			""";

		ParsedDiffModel result = DiffParser.Parse(diff);

		result.Hunks[0].OldStartLine.ShouldBe(5);
		result.Hunks[0].NewStartLine.ShouldBe(8);
	}

	[Fact]
	public void Parse_NoNewlineAtEndMarker_IsSkipped()
	{
		string diff = """
			--- a/file.txt
			+++ b/file.txt
			@@ -1,2 +1,2 @@
			-old
			\ No newline at end of file
			+new
			\ No newline at end of file
			""";

		ParsedDiffModel result = DiffParser.Parse(diff);

		result.Hunks[0].Lines.Count.ShouldBe(2);
		result.LinesAdded.ShouldBe(1);
		result.LinesRemoved.ShouldBe(1);
	}

	[Fact]
	public void Parse_RemovedLineTracksOldLineNumbers()
	{
		string diff = """
			--- a/file.txt
			+++ b/file.txt
			@@ -3,2 +3,1 @@
			-removed
			 context
			""";

		ParsedDiffModel result = DiffParser.Parse(diff);

		DiffLineModel removed = result.Hunks[0].Lines[0];
		removed.Type.ShouldBe(DiffLineTypeEnum.Removed);
		removed.OldLineNumber.ShouldBe(3);
		removed.NewLineNumber.ShouldBeNull();
	}

	[Fact]
	public void Parse_AddedLineTracksNewLineNumbers()
	{
		string diff = """
			--- a/file.txt
			+++ b/file.txt
			@@ -3,1 +3,2 @@
			 context
			+added
			""";

		ParsedDiffModel result = DiffParser.Parse(diff);

		DiffLineModel added = result.Hunks[0].Lines[1];
		added.Type.ShouldBe(DiffLineTypeEnum.Added);
		added.OldLineNumber.ShouldBeNull();
		added.NewLineNumber.ShouldBe(4);
	}

	[Fact]
	public void Parse_UntrackedPseudoDiff_ParsesCorrectly()
	{
		// Format emitted by GitFeature.GetUntrackedFileDiffAsync
		string diff = """
			--- /dev/null
			+++ b/NewFile.cs
			@@ -0,0 +1,2 @@
			+line one
			+line two
			""";

		ParsedDiffModel result = DiffParser.Parse(diff);

		result.OldPath.ShouldBe("/dev/null");
		result.NewPath.ShouldBe("NewFile.cs");
		result.LinesAdded.ShouldBe(2);
		result.LinesRemoved.ShouldBe(0);
		result.Hunks.Count.ShouldBe(1);
	}

	[Fact]
	public void Parse_PathWithoutGitPrefix_LeftAsIs()
	{
		string diff = """
			--- path/without/prefix.txt
			+++ path/without/prefix.txt
			@@ -1,1 +1,1 @@
			-x
			+y
			""";

		ParsedDiffModel result = DiffParser.Parse(diff);

		result.OldPath.ShouldBe("path/without/prefix.txt");
	}

	[Theory]
	[InlineData("--- a/src/foo.cs", "src/foo.cs")]
	[InlineData("--- b/src/foo.cs", "src/foo.cs")]
	[InlineData("--- /dev/null", "/dev/null")]
	[InlineData("--- c/src/foo.cs", "c/src/foo.cs")] // non a/b prefix — kept as-is
	public void Parse_OldPathPrefixStripping(string headerLine, string expectedPath)
	{
		string diff = $"{headerLine}\n+++ b/dst.cs\n@@ -1 +1 @@\n-x\n+y";

		ParsedDiffModel result = DiffParser.Parse(diff);

		result.OldPath.ShouldBe(expectedPath);
	}

	[Fact]
	public void BuildSplitRows_ContextLine_MapsToSelf()
	{
		DiffHunkModel hunk = new()
		{
			Header = "@@ -1,1 +1,1 @@",
			OldStartLine = 1,
			NewStartLine = 1,
			Lines =
			[
				new DiffLineModel { Type = DiffLineTypeEnum.Context, Content = "ctx", OldLineNumber = 1, NewLineNumber = 1 }
			]
		};

		List<SplitRowModel> rows = DiffParser.BuildSplitRows(hunk);

		rows.Count.ShouldBe(1);
		rows[0].Left.ShouldBe(rows[0].Right);
		rows[0].Left!.Content.ShouldBe("ctx");
	}

	[Fact]
	public void BuildSplitRows_PairedRemoveAdd_ProducesOneRow()
	{
		DiffHunkModel hunk = new()
		{
			Header = "@@ -1,1 +1,1 @@",
			OldStartLine = 1,
			NewStartLine = 1,
			Lines =
			[
				new DiffLineModel { Type = DiffLineTypeEnum.Removed, Content = "old", OldLineNumber = 1, NewLineNumber = null },
				new DiffLineModel { Type = DiffLineTypeEnum.Added,   Content = "new", OldLineNumber = null, NewLineNumber = 1 }
			]
		};

		List<SplitRowModel> rows = DiffParser.BuildSplitRows(hunk);

		rows.Count.ShouldBe(1);
		rows[0].Left!.Content.ShouldBe("old");
		rows[0].Right!.Content.ShouldBe("new");
	}

	[Fact]
	public void BuildSplitRows_MoreRemovedThanAdded_PadsRightWithNull()
	{
		DiffHunkModel hunk = new()
		{
			Header = "@@ -1,2 +1,1 @@",
			OldStartLine = 1,
			NewStartLine = 1,
			Lines =
			[
				new DiffLineModel { Type = DiffLineTypeEnum.Removed, Content = "r1", OldLineNumber = 1, NewLineNumber = null },
				new DiffLineModel { Type = DiffLineTypeEnum.Removed, Content = "r2", OldLineNumber = 2, NewLineNumber = null },
				new DiffLineModel { Type = DiffLineTypeEnum.Added,   Content = "a1", OldLineNumber = null, NewLineNumber = 1 }
			]
		};

		List<SplitRowModel> rows = DiffParser.BuildSplitRows(hunk);

		rows.Count.ShouldBe(2);
		rows[1].Left!.Content.ShouldBe("r2");
		rows[1].Right.ShouldBeNull();
	}
}
