using Cockpit.Features.Skills;
using GitHub.Copilot.SDK.Rpc;
using Shouldly;

namespace Cockpit.UnitTests.Features.Skills;

public sealed class SkillsFeatureTests
{
	[Fact]
	public void GroupBySource_EmptyList_ReturnsEmptyDictionary()
	{
		IReadOnlyDictionary<string, List<Skill>> result = SkillsFeature.GroupBySource([]);

		result.ShouldBeEmpty();
	}

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

	[Fact]
	public void GroupBySource_IsCaseInsensitive()
	{
		List<Skill> skills =
		[
			new() { Name = "skill-a", Source = "project" },
			new() { Name = "skill-b", Source = "Project" },
		];

		IReadOnlyDictionary<string, List<Skill>> result = SkillsFeature.GroupBySource(skills);

		result.Count.ShouldBe(1);
		result["project"].Count.ShouldBe(2);
		result["PROJECT"].Count.ShouldBe(2);
	}

	[Fact]
	public void GroupBySource_MixedBlankAndNamedSources_GroupsCorrectly()
	{
		List<Skill> skills =
		[
			new() { Name = "skill-a", Source = string.Empty },
			new() { Name = "skill-b", Source = "   " },
			new() { Name = "skill-c", Source = "builtin" },
		];

		IReadOnlyDictionary<string, List<Skill>> result = SkillsFeature.GroupBySource(skills);

		result.Count.ShouldBe(2);
		result["Unknown"].Count.ShouldBe(2);
		result["builtin"].ShouldHaveSingleItem();
	}
}
