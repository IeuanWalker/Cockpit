using Cockpit.Features.Workspace;
using Shouldly;

namespace Cockpit.UnitTests.Features.Workspace;

public sealed class WorkspaceDomainTests
{
	[Fact]
	public void OpenNewTool_DoesNotImposeAnArbitraryInstanceLimit()
	{
		WorkspaceState state = WorkspaceState.Empty("runtime", "storage", "root");
		for(int index = 0; index < 2_000; index++)
		{
			WorkspaceChangeResult result = WorkspaceReducer.OpenNewTool(
				state,
				"root",
				new WorkspaceInstance($"instance-{index}", "cockpit.test"));
			result.Status.ShouldBe(WorkspaceChangeStatus.Applied);
			state = result.State;
		}

		state.Instances.Count.ShouldBe(2_000);
		((WorkspaceGroupNode)state.Nodes["root"]).OrderedInstanceIds.Count.ShouldBe(2_000);
		WorkspaceValidation.Validate(state).IsValid.ShouldBeTrue();
	}

	[Fact]
	public void ValidationAndGeometry_HandleDeepFlatLayoutsIteratively()
	{
		const int depth = 2_000;
		Dictionary<string, WorkspaceNode> nodes = new(StringComparer.Ordinal)
		{
			["group-0"] = new WorkspaceGroupNode("group-0", [])
		};
		string root = "group-0";
		for(int index = 1; index <= depth; index++)
		{
			string groupId = $"group-{index}";
			string splitId = $"split-{index}";
			nodes[groupId] = new WorkspaceGroupNode(groupId, []);
			nodes[splitId] = new WorkspaceSplitNode(splitId, WorkspaceSplitAxis.Columns, 0.5, root, groupId);
			root = splitId;
		}
		WorkspaceState state = WorkspaceState.Empty("runtime", "storage", "group-0") with
		{
			RootNodeId = root,
			Nodes = nodes
		};

		WorkspaceValidation.Validate(state).IsValid.ShouldBeTrue();
		WorkspaceSize minimum = WorkspaceGeometryPolicy.CalculateMinimumSize(state);
		minimum.Width.ShouldBe(((depth + 1) * WorkspaceGeometryPolicy.minimumLeafWidth) + (depth * WorkspaceGeometryPolicy.dividerThickness));
		minimum.Height.ShouldBe(WorkspaceGeometryPolicy.minimumLeafHeight);
	}

	[Fact]
	public void MoveInstance_UsesDestinationIndexAfterSourceRemoval()
	{
		WorkspaceState state = CreateSingleGroup("A", "B", "C", "D");

		WorkspaceChangeResult result = WorkspaceReducer.MoveInstance(state, "B", "root", 3);

		result.Status.ShouldBe(WorkspaceChangeStatus.Applied);
		((WorkspaceGroupNode)result.State.Nodes["root"]).OrderedInstanceIds.ShouldBe(["A", "C", "D", "B"]);
	}

	[Fact]
	public void VisibleProjection_HidesUnavailableToolsWithoutMutatingCanonicalPositions()
	{
		WorkspaceState state = CreateSingleGroup("available", "dormant", "missing") with
		{
			Instances = new Dictionary<string, WorkspaceInstance>(StringComparer.Ordinal)
			{
				["available"] = new("available", "cockpit.available"),
				["dormant"] = new("dormant", "cockpit.dormant"),
				["missing"] = new("missing", "third.party.missing")
			},
			Availability = new Dictionary<string, WorkspaceAvailabilityState>(StringComparer.Ordinal)
			{
				["cockpit.available"] = WorkspaceAvailabilityState.available,
				["cockpit.dormant"] = new(WorkspaceToolAvailability.Unavailable, "Not a repository")
			}
		};

		VisibleWorkspaceProjection projection = WorkspaceVisibleProjection.Create(state);

		((WorkspaceGroupNode)projection.Nodes["root"]).OrderedInstanceIds.ShouldBe(["available", "missing"]);
		projection.DormantInstanceIds.ShouldContain("dormant");
		((WorkspaceGroupNode)state.Nodes["root"]).OrderedInstanceIds.ShouldBe(["available", "dormant", "missing"]);
	}

	[Fact]
	public void CloseLastInstanceInSplit_CollapsesOnlyTheGenuinelyEmptyBranch()
	{
		WorkspaceState initial = CreateSingleGroup("A");
		WorkspaceState split = WorkspaceReducer.SplitGroupWithInstance(
			initial,
			"root",
			new WorkspaceInstance("B", "cockpit.test"),
			"second",
			"split",
			WorkspaceSplitAxis.Columns,
			placeNewGroupFirst: false).State;

		WorkspaceChangeResult closed = WorkspaceReducer.CloseInstance(split, "B");

		closed.Status.ShouldBe(WorkspaceChangeStatus.Applied);
		closed.State.RootNodeId.ShouldBe("root");
		closed.State.Nodes.Keys.ShouldBe(["root"], ignoreOrder: true);
		closed.State.Instances.Keys.ShouldBe(["A"], ignoreOrder: true);
	}

	[Fact]
	public void SplitGroupWithExistingInstance_RestoresAMaximisedLayoutSoTheNewPaneIsVisible()
	{
		WorkspaceState state = WorkspaceReducer.MaximiseGroup(CreateSingleGroup("A", "B"), "root").State;

		WorkspaceChangeResult result = WorkspaceReducer.SplitGroupWithExistingInstance(
			state,
			"B",
			"root",
			"second",
			"split",
			WorkspaceSplitAxis.Columns,
			placeNewGroupFirst: false);

		result.Status.ShouldBe(WorkspaceChangeStatus.Applied);
		result.State.MaximisedGroupId.ShouldBeNull();
		result.State.RootNodeId.ShouldBe("split");
		result.State.Nodes["split"].ShouldBeOfType<WorkspaceSplitNode>();
		((WorkspaceGroupNode)result.State.Nodes["root"]).OrderedInstanceIds.ShouldBe(["A"]);
		((WorkspaceGroupNode)result.State.Nodes["second"]).OrderedInstanceIds.ShouldBe(["B"]);
	}

	[Fact]
	public void SplitRootWithExistingInstance_WrapsTheWholeExistingLayout()
	{
		WorkspaceState columns = WorkspaceReducer.SplitGroupWithExistingInstance(
			CreateSingleGroup("A", "B", "C"),
			"B",
			"root",
			"right",
			"columns",
			WorkspaceSplitAxis.Columns,
			placeNewGroupFirst: false).State;

		WorkspaceChangeResult result = WorkspaceReducer.SplitRootWithExistingInstance(
			columns,
			"C",
			"top",
			"rows",
			WorkspaceSplitAxis.Rows,
			placeNewGroupFirst: true);

		result.Status.ShouldBe(WorkspaceChangeStatus.Applied);
		result.State.RootNodeId.ShouldBe("rows");
		WorkspaceSplitNode rows = result.State.Nodes["rows"].ShouldBeOfType<WorkspaceSplitNode>();
		rows.Axis.ShouldBe(WorkspaceSplitAxis.Rows);
		rows.FirstNodeId.ShouldBe("top");
		rows.SecondNodeId.ShouldBe("columns");
		result.State.Nodes["columns"].ShouldBeOfType<WorkspaceSplitNode>().Axis.ShouldBe(WorkspaceSplitAxis.Columns);
		((WorkspaceGroupNode)result.State.Nodes["top"]).OrderedInstanceIds.ShouldBe(["C"]);
	}

	[Fact]
	public void SplitGroupWithExistingInstance_RejectsAFifthSection()
	{
		WorkspaceState state = CreateSingleGroup("A", "E");
		for(int index = 2; index <= WorkspaceLayoutPolicy.MaxSections; index++)
		{
			state = WorkspaceReducer.SplitGroupWithInstance(
				state,
				"root",
				new WorkspaceInstance($"instance-{index}", "cockpit.test"),
				$"group-{index}",
				$"split-{index}",
				WorkspaceSplitAxis.Columns,
				placeNewGroupFirst: false).State;
		}

		WorkspaceChangeResult result = WorkspaceReducer.SplitGroupWithExistingInstance(
			state,
			"E",
			"root",
			"fifth",
			"fifth-split",
			WorkspaceSplitAxis.Rows,
			placeNewGroupFirst: false);

		result.Status.ShouldBe(WorkspaceChangeStatus.Rejected);
		result.Error.ShouldBe(WorkspaceChangeError.SectionLimitReached);
		result.State.ShouldBeSameAs(state);
	}

	[Fact]
	public void StaleLayoutCommand_IsRejectedWithoutARevisionChange()
	{
		WorkspaceState state = CreateSingleGroup("A");

		WorkspaceChangeResult result = WorkspaceReducer.CloseInstance(state, "A", expectedLayoutRevision: state.LayoutRevision + 1);

		result.Status.ShouldBe(WorkspaceChangeStatus.Rejected);
		result.Error.ShouldBe(WorkspaceChangeError.StaleCommand);
		result.State.ShouldBeSameAs(state);
	}

	static WorkspaceState CreateSingleGroup(params string[] instanceIds)
	{
		WorkspaceGroupNode group = new("root", instanceIds, instanceIds.FirstOrDefault());
		return new WorkspaceState
		{
			RuntimeSessionKey = "runtime",
			SessionStorageId = "storage",
			RootNodeId = "root",
			FocusedGroupId = "root",
			Nodes = new Dictionary<string, WorkspaceNode>(StringComparer.Ordinal) { ["root"] = group },
			Instances = instanceIds.ToDictionary(id => id, id => new WorkspaceInstance(id, "cockpit.test"), StringComparer.Ordinal),
			Availability = new Dictionary<string, WorkspaceAvailabilityState>(StringComparer.Ordinal)
			{
				["cockpit.test"] = WorkspaceAvailabilityState.available
			}
		};
	}
}
