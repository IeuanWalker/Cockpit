using Cockpit.Features.Instructions;
using GitHub.Copilot.SDK.Rpc;
using Shouldly;

namespace Cockpit.UnitTests.Features.Instructions;

public sealed class InstructionsFeatureTests
{
	// ── GetDisplayLabel ───────────────────────────────────────────────────────

	[Fact]
	public void GetDisplayLabel_LabelWithGitHubPrefix_StripsPrefix()
	{
		string result = InstructionsFeature.GetDisplayLabel(".github/copilot-instructions.md");

		result.ShouldBe("copilot-instructions.md");
	}

	[Fact]
	public void GetDisplayLabel_LabelWithGitHubPrefixCaseInsensitive_StripsPrefix()
	{
		string result = InstructionsFeature.GetDisplayLabel(".GitHub/copilot-instructions.md");

		result.ShouldBe("copilot-instructions.md");
	}

	[Fact]
	public void GetDisplayLabel_LabelWithoutGitHubPrefix_ReturnsUnchanged()
	{
		string result = InstructionsFeature.GetDisplayLabel("custom-instructions.md");

		result.ShouldBe("custom-instructions.md");
	}

	[Fact]
	public void GetDisplayLabel_EmptyLabel_ReturnsEmpty()
	{
		string result = InstructionsFeature.GetDisplayLabel(string.Empty);

		result.ShouldBe(string.Empty);
	}

	[Fact]
	public void GetDisplayLabel_LabelIsOnlyPrefix_ReturnsEmpty()
	{
		string result = InstructionsFeature.GetDisplayLabel(".github/");

		result.ShouldBe(string.Empty);
	}

	[Fact]
	public void GetDisplayLabel_LabelContainsPrefixMidString_ReturnsUnchanged()
	{
		// Prefix must be at the start; mid-string occurrences are not stripped.
		string result = InstructionsFeature.GetDisplayLabel("foo/.github/bar.md");

		result.ShouldBe("foo/.github/bar.md");
	}

	// ── GroupByLocation — empty input → empty dictionary ─────────────────────

	[Fact]
	public void GroupByLocation_EmptyList_ReturnsEmptyDictionary()
	{
		IReadOnlyDictionary<string, List<InstructionsSources>> result = InstructionsFeature.GroupByLocation([]);

		result.ShouldBeEmpty();
	}

	// ── GroupByLocation — single item → one group ─────────────────────────────

	[Fact]
	public void GroupByLocation_SingleSource_ReturnsSingleGroup()
	{
		InstructionsSources source = new() { Id = "a", Label = "A", Location = InstructionsSourcesLocation.User };

		IReadOnlyDictionary<string, List<InstructionsSources>> result = InstructionsFeature.GroupByLocation([source]);

		result.Count.ShouldBe(1);
		result[InstructionsSourcesLocation.User.ToString()].ShouldHaveSingleItem();
	}

	// ── GroupByLocation — same location → single group with all items ─────────

	[Fact]
	public void GroupByLocation_MultipleSourcesSameLocation_GroupedTogether()
	{
		List<InstructionsSources> sources =
		[
			new() { Id = "a", Label = "A", Location = InstructionsSourcesLocation.Repository },
			new() { Id = "b", Label = "B", Location = InstructionsSourcesLocation.Repository },
		];

		IReadOnlyDictionary<string, List<InstructionsSources>> result = InstructionsFeature.GroupByLocation(sources);

		result.Count.ShouldBe(1);
		result[InstructionsSourcesLocation.Repository.ToString()].Count.ShouldBe(2);
	}

	// ── GroupByLocation — different locations → separate groups ───────────────

	[Fact]
	public void GroupByLocation_MultipleSourcesDifferentLocations_SeparateGroups()
	{
		List<InstructionsSources> sources =
		[
			new() { Id = "a", Label = "A", Location = InstructionsSourcesLocation.User },
			new() { Id = "b", Label = "B", Location = InstructionsSourcesLocation.Repository },
			new() { Id = "c", Label = "C", Location = InstructionsSourcesLocation.WorkingDirectory },
		];

		IReadOnlyDictionary<string, List<InstructionsSources>> result = InstructionsFeature.GroupByLocation(sources);

		result.Count.ShouldBe(3);
		result[InstructionsSourcesLocation.User.ToString()].ShouldHaveSingleItem();
		result[InstructionsSourcesLocation.Repository.ToString()].ShouldHaveSingleItem();
		result[InstructionsSourcesLocation.WorkingDirectory.ToString()].ShouldHaveSingleItem();
	}

	// ── GroupByLocation — OrdinalIgnoreCase key lookup works ──────────────────

	[Fact]
	public void GroupByLocation_IsCaseInsensitive()
	{
		InstructionsSources source = new() { Id = "a", Label = "A", Location = InstructionsSourcesLocation.User };

		IReadOnlyDictionary<string, List<InstructionsSources>> result = InstructionsFeature.GroupByLocation([source]);

		// The dictionary uses OrdinalIgnoreCase — both casings resolve to the same group.
		result.ContainsKey("user").ShouldBeTrue();
		result.ContainsKey("USER").ShouldBeTrue();
		result["user"].ShouldBeSameAs(result["USER"]);
	}

	// ── GroupByLocation — preserves source identity ────────────────────────────

	[Fact]
	public void GroupByLocation_PreservesSourceOrder()
	{
		InstructionsSources first = new() { Id = "a", Label = "A", Location = InstructionsSourcesLocation.Repository };
		InstructionsSources second = new() { Id = "b", Label = "B", Location = InstructionsSourcesLocation.Repository };

		IReadOnlyDictionary<string, List<InstructionsSources>> result = InstructionsFeature.GroupByLocation([first, second]);

		List<InstructionsSources> group = result[InstructionsSourcesLocation.Repository.ToString()];
		group[0].ShouldBeSameAs(first);
		group[1].ShouldBeSameAs(second);
	}
}
