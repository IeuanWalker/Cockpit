using Cockpit.Features.Instructions;
using GitHub.Copilot.SDK.Rpc;
using Shouldly;

namespace Cockpit.UnitTests.Features.Instructions;

public sealed class InstructionsFeatureTests
{
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
}
