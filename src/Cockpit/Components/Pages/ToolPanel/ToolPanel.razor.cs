using Cockpit.Features.UIState;
using Cockpit.Features.Workspace;
using Cockpit.Tooling;
using Microsoft.AspNetCore.Components;
using Microsoft.JSInterop;

namespace Cockpit.Components.Pages.ToolPanel;

public sealed partial class ToolPanel : ComponentBase, IDisposable
{
	readonly IUIStateFeature _uiStateFeature;
	readonly IJSRuntime _jsRuntime;
	[Inject] WorkspaceFeature Workspace { get; set; } = default!;
	DotNetObjectReference<ToolPanel>? _dotNetHelper;
	string? _toolPickerGroupId;
	string? _draggingInstanceId;
	PendingClose? _pendingClose;
	TabContextMenu? _tabContextMenu;
	bool _lastSidebarCollapsed;

	public ToolPanel(IUIStateFeature uiStateFeature, IJSRuntime jsRuntime)
	{
		_uiStateFeature = uiStateFeature;
		_jsRuntime = jsRuntime;
	}

	protected override void OnInitialized()
	{
		_uiStateFeature.OnStateChanged += OnStateChanged;
		Workspace.Changed += OnStateChanged;
		_lastSidebarCollapsed = _uiStateFeature.RightSidebarCollapsed;
		_ = Workspace.EnsureCurrentAsync();
	}

	protected override async Task OnAfterRenderAsync(bool firstRender)
	{
		if(firstRender)
		{
			_dotNetHelper = DotNetObjectReference.Create(this);
			await _jsRuntime.InvokeVoidAsync("cockpit.initializeWorkspaceDrag", "rightSidebar", _dotNetHelper);
			await _jsRuntime.InvokeVoidAsync("cockpit.initializeWorkspaceSplitResize", "rightSidebar", _dotNetHelper);
			await _jsRuntime.InvokeVoidAsync("cockpit.initializeResize", "rightResizeHandle", "rightSidebar", "right", _dotNetHelper);
		}
	}

	void ToggleToolPicker(string groupId) => _toolPickerGroupId = _toolPickerGroupId == groupId ? null : groupId;
	async Task OpenToolAsync((string ToolId, string GroupId) value) { _toolPickerGroupId = null; await Workspace.OpenToolAsync(value.ToolId, value.GroupId); }
	Task OpenToolFromLauncherAsync(string toolId) => Workspace.OpenToolAsync(toolId);
	bool HasVisibleTools => Workspace.Current?.Visible.Nodes.Values.OfType<WorkspaceGroupNode>()
		.Any(group => group.OrderedInstanceIds.Count > 0) == true;
	bool IsToolAvailable(string toolId) => Workspace.Current?.State.Availability.TryGetValue(toolId, out WorkspaceAvailabilityState? state) == true &&
		state.Status == WorkspaceToolAvailability.Available;
	static string ToolIcon(string? icon) => icon switch
	{
		"git" or "git-changes" => "⑂",
		"terminal" => ">_",
		"files" => "▱",
		"browser" => "◎",
		"context" => "◉",
		_ => "◇"
	};
	void SetDragging(string instanceId) => _draggingInstanceId = instanceId;
	void ClearDragging() => _draggingInstanceId = null;
	void Activate((string GroupId, string InstanceId) value) => Workspace.Activate(value.GroupId, value.InstanceId);
	void OpenTabContextMenu((string InstanceId, string GroupId, double ClientX, double ClientY) value)
	{
		_toolPickerGroupId = null;
		_tabContextMenu = new(value.InstanceId, value.GroupId, value.ClientX, value.ClientY);
	}
	void Move((string InstanceId, string GroupId, int Index) value) => Workspace.Move(value.InstanceId, value.GroupId, value.Index);
	void Snap((string InstanceId, string GroupId, string Edge) value)
	{
		if(value.Edge == "center") Workspace.MoveToGroupEnd(value.InstanceId, value.GroupId);
		else Workspace.Split(value.InstanceId, value.GroupId,
			value.Edge is "left" or "right" ? WorkspaceSplitAxis.Columns : WorkspaceSplitAxis.Rows,
			value.Edge is "left" or "top");
		_draggingInstanceId = null;
	}
	void ToggleMaximise(string groupId) => Workspace.ToggleMaximise(groupId);
	void SetSplitRatio((string SplitId, double Ratio) value) => Workspace.SetSplitRatio(value.SplitId, value.Ratio);

	Task RequestCloseAsync(string instanceId) => RequestCloseManyAsync([instanceId]);

	async Task RequestCloseManyAsync(IEnumerable<string> instanceIds)
	{
		string[] requested = instanceIds.Distinct(StringComparer.Ordinal).ToArray();
		List<string> closable = [];
		List<ToolCloseDecision> confirmations = [];
		foreach(string instanceId in requested)
		{
			ToolCloseDecision decision = await Workspace.GetCloseDecisionAsync(instanceId);
			if(decision.Kind == ToolCloseDecisionKind.Allow)
			{
				closable.Add(instanceId);
			}
			else if(decision.Kind == ToolCloseDecisionKind.Confirm)
			{
				closable.Add(instanceId);
				confirmations.Add(decision);
			}
		}

		if(confirmations.Count > 0)
		{
			ToolCloseDecision decision = confirmations.Count == 1
				? confirmations[0]
				: ToolCloseDecision.Confirm($"{confirmations.Count} running tools must be stopped before these tabs can close.", "Close tabs");
			_pendingClose = new(closable, decision);
			return;
		}

		foreach(string instanceId in closable)
		{
			await Workspace.CloseAsync(instanceId, true);
		}
	}

	async Task CloseContextTabAsync()
	{
		if(_tabContextMenu is not { } menu) return;
		_tabContextMenu = null;
		await RequestCloseManyAsync([menu.InstanceId]);
	}

	async Task CloseOtherContextTabsAsync()
	{
		if(_tabContextMenu is not { } menu || ContextGroup(menu) is not { } group) return;
		_tabContextMenu = null;
		await RequestCloseManyAsync(group.OrderedInstanceIds.Where(id => id != menu.InstanceId));
	}

	async Task CloseAllContextTabsAsync()
	{
		if(_tabContextMenu is not { } menu || ContextGroup(menu) is not { } group) return;
		_tabContextMenu = null;
		await RequestCloseManyAsync(group.OrderedInstanceIds);
	}

	void CancelClose() => _pendingClose = null;
	async Task ConfirmCloseAsync()
	{
		PendingClose pending = _pendingClose!;
		_pendingClose = null;
		foreach(string instanceId in pending.InstanceIds)
		{
			await Workspace.CloseAsync(instanceId, true);
		}
	}

	string PendingCloseTitle => _pendingClose?.InstanceIds.Count > 1 ? "Close running tools?" : "Close running tool?";
	string ContextMenuStyle => _tabContextMenu is not { } menu ? string.Empty : FormattableString.Invariant(
		$"left:min({menu.ClientX}px, calc(100vw - 192px)); top:min({menu.ClientY}px, calc(100vh - 282px));");
	bool HasOtherContextTabs => _tabContextMenu is { } menu && ContextGroup(menu)?.OrderedInstanceIds.Count > 1;
	string ContextPlacementLabel(string edge)
	{
		string verb = CurrentSectionAssignments().Values.Contains(edge, StringComparer.Ordinal) ? "Move" : "Split";
		return $"{verb} {char.ToUpperInvariant(edge[0])}{edge[1..]}";
	}

	bool ShouldShowContextPlacement(string edge) => _tabContextMenu is not { } menu ||
		!CurrentSectionAssignments().TryGetValue(menu.GroupId, out string? currentEdge) || currentEdge != edge;

	WorkspaceGroupNode? ContextGroup(TabContextMenu menu) =>
		Workspace.Current?.State.Nodes.TryGetValue(menu.GroupId, out WorkspaceNode? node) == true ? node as WorkspaceGroupNode : null;

	bool CanPlaceContextTab(string edge)
	{
		if(_tabContextMenu is not { } menu || Workspace.Current is not { } current || ContextGroup(menu) is not { } source)
		{
			return false;
		}
		WorkspaceGroupNode[] groups = [.. current.State.Nodes.Values.OfType<WorkspaceGroupNode>()];
		if(groups.Length == 1 && source.OrderedInstanceIds.Count == 1)
		{
			return false;
		}
		return FindSectionGroup(current.State, edge) is not null ||
			WorkspaceLayoutPolicy.CanSplitRootWithExistingInstance(current.State, menu.InstanceId);
	}

	void PlaceContextTab(string edge)
	{
		if(_tabContextMenu is not { } menu || Workspace.Current is not { } current || !CanPlaceContextTab(edge)) return;
		WorkspaceGroupNode? destination = FindSectionGroup(current.State, edge);
		_tabContextMenu = null;
		if(destination is not null)
		{
			Workspace.MoveToGroupEnd(menu.InstanceId, destination.Id);
		}
		else
		{
			Workspace.SplitAtRoot(menu.InstanceId,
				edge is "left" or "right" ? WorkspaceSplitAxis.Columns : WorkspaceSplitAxis.Rows,
				edge is "left" or "top");
		}
	}

	IReadOnlyDictionary<string, string> CurrentSectionAssignments() => Workspace.Current is { } current
		? BuildSectionAssignments(current.State)
		: new Dictionary<string, string>(StringComparer.Ordinal);

	static WorkspaceGroupNode? FindSectionGroup(WorkspaceState state, string edge)
	{
		KeyValuePair<string, string> assignment = BuildSectionAssignments(state)
			.FirstOrDefault(pair => pair.Value == edge);
		return assignment.Key is not null && state.Nodes.TryGetValue(assignment.Key, out WorkspaceNode? node)
			? node as WorkspaceGroupNode
			: null;
	}

	static IReadOnlyDictionary<string, string> BuildSectionAssignments(WorkspaceState state)
	{
		Dictionary<string, LayoutBounds> bounds = new(StringComparer.Ordinal);
		CollectBounds(state, state.RootNodeId, new(0, 0, 1, 1), bounds);
		if(bounds.Count <= 1)
		{
			return new Dictionary<string, string>(StringComparer.Ordinal);
		}

		(string Edge, double X, double Y)[] slots =
		[
			("top", .5, 0),
			("right", 1, .5),
			("bottom", .5, 1),
			("left", 0, .5)
		];
		KeyValuePair<string, LayoutBounds>[] groups = bounds
			.OrderBy(pair => pair.Value.CenterY)
			.ThenBy(pair => pair.Value.CenterX)
			.ThenBy(pair => pair.Key, StringComparer.Ordinal)
			.ToArray();
		Dictionary<string, string> current = new(StringComparer.Ordinal);
		Dictionary<string, string>? best = null;
		double bestScore = double.MaxValue;

		void Assign(int index, HashSet<string> used, double score)
		{
			if(score >= bestScore) return;
			if(index == groups.Length)
			{
				bestScore = score;
				best = new Dictionary<string, string>(current, StringComparer.Ordinal);
				return;
			}

			(string groupId, LayoutBounds groupBounds) = groups[index];
			foreach((string edge, double x, double y) in slots)
			{
				if(!used.Add(edge)) continue;
				current[groupId] = edge;
				double dx = groupBounds.CenterX - x;
				double dy = groupBounds.CenterY - y;
				Assign(index + 1, used, score + dx * dx + dy * dy);
				current.Remove(groupId);
				used.Remove(edge);
			}
		}

		Assign(0, new(StringComparer.Ordinal), 0);
		return best ?? new Dictionary<string, string>(StringComparer.Ordinal);
	}

	static void CollectBounds(WorkspaceState state, string nodeId, LayoutBounds bounds, IDictionary<string, LayoutBounds> result)
	{
		if(!state.Nodes.TryGetValue(nodeId, out WorkspaceNode? node)) return;
		if(node is WorkspaceGroupNode)
		{
			result[nodeId] = bounds;
			return;
		}
		if(node is not WorkspaceSplitNode split) return;
		if(split.Axis == WorkspaceSplitAxis.Columns)
		{
			double firstWidth = bounds.Width * split.Ratio;
			CollectBounds(state, split.FirstNodeId, bounds with { Width = firstWidth }, result);
			CollectBounds(state, split.SecondNodeId, new(bounds.X + firstWidth, bounds.Y, bounds.Width - firstWidth, bounds.Height), result);
		}
		else
		{
			double firstHeight = bounds.Height * split.Ratio;
			CollectBounds(state, split.FirstNodeId, bounds with { Height = firstHeight }, result);
			CollectBounds(state, split.SecondNodeId, new(bounds.X, bounds.Y + firstHeight, bounds.Width, bounds.Height - firstHeight), result);
		}
	}

	void OnStateChanged()
	{
		if(_lastSidebarCollapsed != _uiStateFeature.RightSidebarCollapsed)
		{
			_lastSidebarCollapsed = _uiStateFeature.RightSidebarCollapsed;
			_ = Workspace.RefreshVisibilityAsync();
		}
		_ = InvokeAsync(StateHasChanged);
	}

	[JSInvokable]
	public Task DropWorkspaceTab(string instanceId, string groupId, string? anchorId, bool after, string? edge) => InvokeAsync(() =>
	{
		if(Workspace.Current is not { } current || !current.State.Instances.ContainsKey(instanceId) ||
			!current.State.Nodes.TryGetValue(groupId, out WorkspaceNode? node) || node is not WorkspaceGroupNode group) return;
		if(edge is "left" or "right" or "top" or "bottom")
		{
			Workspace.SplitAtRoot(instanceId,
				edge is "left" or "right" ? WorkspaceSplitAxis.Columns : WorkspaceSplitAxis.Rows,
				edge is "left" or "top");
		}
		else if(anchorId is not null && anchorId != instanceId)
		{
			List<string> ids = group.OrderedInstanceIds.Where(id => id != instanceId).ToList();
			int index = ids.IndexOf(anchorId);
			if(index >= 0) Workspace.Move(instanceId, groupId, index + (after ? 1 : 0));
		}
		else if(anchorId is null) Workspace.MoveToGroupEnd(instanceId, groupId);
		StateHasChanged();
	});

	[JSInvokable]
	public void OnResize(int width) => _uiStateFeature.SetRightSidebarWidth(width);

	[JSInvokable]
	public Task DismissWorkspaceToolPicker() => InvokeAsync(() =>
	{
		if(_toolPickerGroupId is null) return;
		_toolPickerGroupId = null;
		StateHasChanged();
	});

	[JSInvokable]
	public Task DismissWorkspaceTabContextMenu() => InvokeAsync(() =>
	{
		if(_tabContextMenu is null) return;
		_tabContextMenu = null;
		StateHasChanged();
	});

	[JSInvokable]
	public Task OnWorkspaceSplitResize(string splitId, double ratio) => InvokeAsync(() => Workspace.SetSplitRatio(splitId, ratio));

	public void Dispose()
	{
		_uiStateFeature.OnStateChanged -= OnStateChanged;
		Workspace.Changed -= OnStateChanged;
		_dotNetHelper?.Dispose();
		GC.SuppressFinalize(this);
	}

	sealed record PendingClose(IReadOnlyList<string> InstanceIds, ToolCloseDecision Decision);
	sealed record TabContextMenu(string InstanceId, string GroupId, double ClientX, double ClientY);
	readonly record struct LayoutBounds(double X, double Y, double Width, double Height)
	{
		internal double CenterX => X + Width / 2;
		internal double CenterY => Y + Height / 2;
	}
}
