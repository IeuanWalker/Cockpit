using Cockpit.Features.Skills;
using GitHub.Copilot.SDK.Rpc;
using Shouldly;

namespace Cockpit.UnitTests.Features.Skills;

public sealed class SkillsFeatureTests
{
	// ── GroupBySource — empty input → empty dictionary ────────────────────────

	[Fact]
	public void GroupBySource_EmptyList_ReturnsEmptyDictionary()
	{
		IReadOnlyDictionary<string, List<Skill>> result = SkillsFeature.GroupBySource([]);

		result.ShouldBeEmpty();
	}

	// ── GroupBySource — single item → one group ───────────────────────────────

	[Fact]
	public void GroupBySource_SingleSkill_ReturnsSingleGroup()
	{
		Skill skill = new() { Name = "skill-a", Source = "project" };

		IReadOnlyDictionary<string, List<Skill>> result = SkillsFeature.GroupBySource([skill]);

		result.Count.ShouldBe(1);
		result["project"].ShouldHaveSingleItem();
	}

	// ── GroupBySource — same source → single group with all items ─────────────

	[Fact]
	public void GroupBySource_MultipleSkillsSameSource_GroupedTogether()
	{
		List<Skill> skills =
		[
			new() { Name = "skill-a", Source = "builtin" },
			new() { Name = "skill-b", Source = "builtin" },
		];

		IReadOnlyDictionary<string, List<Skill>> result = SkillsFeature.GroupBySource(skills);

		result.Count.ShouldBe(1);
		result["builtin"].Count.ShouldBe(2);
	}

	// ── GroupBySource — different sources → separate groups ───────────────────

	[Fact]
	public void GroupBySource_MultipleSkillsDifferentSources_SeparateGroups()
	{
		List<Skill> skills =
		[
			new() { Name = "skill-a", Source = "project" },
			new() { Name = "skill-b", Source = "builtin" },
			new() { Name = "skill-c", Source = "user" },
		];

		IReadOnlyDictionary<string, List<Skill>> result = SkillsFeature.GroupBySource(skills);

		result.Count.ShouldBe(3);
		result["project"].ShouldHaveSingleItem();
		result["builtin"].ShouldHaveSingleItem();
		result["user"].ShouldHaveSingleItem();
	}

	// ── GroupBySource — OrdinalIgnoreCase key lookup works ────────────────────

	[Fact]
	public void GroupBySource_IsCaseInsensitive()
	{
		List<Skill> skills =
		[
			new() { Name = "skill-a", Source = "project" },
			new() { Name = "skill-b", Source = "Project" },
		];

		IReadOnlyDictionary<string, List<Skill>> result = SkillsFeature.GroupBySource(skills);

		// OrdinalIgnoreCase grouping merges "project" and "Project" into one group.
		result.Count.ShouldBe(1);
		result["project"].Count.ShouldBe(2);
		result["PROJECT"].Count.ShouldBe(2);
	}
}
