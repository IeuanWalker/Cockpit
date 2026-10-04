using System.Text;
using System.Text.Json;
using Cockpit.Features.Sessions;
using Cockpit.Features.Sessions.Models;
using Cockpit.Features.UIState;
using Cockpit.Features.Workspace.Storage;
using Cockpit.Tooling;
using Cockpit.Tooling.Discovery;
using Microsoft.Extensions.Logging;

namespace Cockpit.Features.Workspace;

sealed record WorkspaceRenderedInstance(
	WorkspaceInstance Model,
	WorkspaceToolDescriptor? Descriptor,
	IWorkspaceToolInstance? Controller,
	WorkspaceToolComponentContext? ComponentContext,
	string? Error);

public sealed class WorkspaceRenderedState
{
	internal WorkspaceRenderedState(WorkspaceState state, VisibleWorkspaceProjection visible, IReadOnlyDictionary<string, WorkspaceRenderedInstance> instances)
	{
		State = state;
		Visible = visible;
		Instances = instances;
	}
	internal WorkspaceState State { get; }
	internal VisibleWorkspaceProjection Visible { get; }
	internal IReadOnlyDictionary<string, WorkspaceRenderedInstance> Instances { get; }
}

sealed class WorkspaceFeature : IAsyncDisposable
{
	readonly WorkspaceToolRegistry _registry;
	readonly SessionListFeature _sessions;
	readonly WorkspaceStateFeature _states;
	readonly IWorkspaceGenerationStore _store;
	readonly IUIStateFeature _uiState;
	readonly ILogger<WorkspaceFeature> _logger;
	readonly SemaphoreSlim _gate = new(1, 1);
	readonly Dictionary<string, Runtime> _runtimes = new(StringComparer.Ordinal);
	string? _currentSessionId;

	internal WorkspaceFeature(
		WorkspaceToolRegistry registry,
		SessionListFeature sessions,
		WorkspaceStateFeature states,
		IWorkspaceGenerationStore store,
		IUIStateFeature uiState,
		ILogger<WorkspaceFeature> logger)
	{
		_registry = registry;
		_sessions = sessions;
		_states = states;
		_store = store;
		_uiState = uiState;
		_logger = logger;
		_sessions.OnStateChanged += HandleSessionsChanged;
		_states.StateChanged += HandleWorkspaceChanged;
	}

	internal event Action? Changed;
	internal IReadOnlyList<RegisteredWorkspaceTool> Tools => _registry.Tools;
	internal WorkspaceRenderedState? Current { get; private set; }
	internal bool HasCurrentWorkspace => Current is not null;

	internal async Task SaveCurrentAsDefaultAsync(CancellationToken cancellationToken = default)
	{
		Runtime runtime = RequireCurrent();
		await SaveAsync(runtime, cancellationToken, WorkspaceStorageLocator.ForDefaults(FileSystem.AppDataDirectory), exportTemplate: true);
		// Save again to the session location because capturing the default advances
		// the live checkpoint references to the newly captured content objects.
		await SaveAsync(runtime, cancellationToken);
	}

	internal async Task ResetDefaultAsync(CancellationToken cancellationToken = default)
	{
		string rootId = Guid.NewGuid().ToString("N");
		WorkspaceState state = WorkspaceState.Empty("default", "default", rootId);
		foreach(string toolId in new[] { "cockpit.session-context", "cockpit.git-changes" })
		{
			if(_registry.TryGet(toolId, out _))
			{
				WorkspaceChangeResult opened = WorkspaceReducer.OpenNewTool(state, state.FocusedGroupId, new(Guid.NewGuid().ToString("N"), toolId));
				state = opened.State;
			}
		}
		IWorkspaceWriteLease? lease = await _store.TryAcquireWriteLeaseAsync(WorkspaceStorageLocator.ForDefaults(FileSystem.AppDataDirectory), cancellationToken);
		if(lease is null)
		{
			return;
		}

		await using(lease)
		{
			await _store.CommitAsync(lease, new($"{DateTimeOffset.UtcNow:yyyyMMddHHmmssfffffff}-{Guid.NewGuid():N}", WorkspaceManifest.FromState(state), []), cancellationToken);
		}
	}

	internal async Task EnsureCurrentAsync(CancellationToken cancellationToken = default)
	{
		await _gate.WaitAsync(cancellationToken);
		try
		{
			await ReleaseDeletedSessionsAsync(cancellationToken);
			SessionModel? session = _sessions.CurrentSession;
			if(session is null)
			{
				_currentSessionId = null;
				Current = null;
				Changed?.Invoke();
				return;
			}
			if(!_runtimes.TryGetValue(session.Id, out Runtime? runtime))
			{
				runtime = await CreateRuntimeAsync(session, cancellationToken);
				_runtimes.Add(session.Id, runtime);
			}
			_currentSessionId = session.Id;
			Publish(runtime);
			await ActivateVisibleAsync(runtime, cancellationToken);
		}
		finally
		{
			_gate.Release();
		}
	}

	async Task ReleaseDeletedSessionsAsync(CancellationToken cancellationToken)
	{
		HashSet<string> live = _sessions.Sessions.Select(session => session.Id).ToHashSet(StringComparer.Ordinal);
		foreach(string key in _runtimes.Keys.Where(key => !live.Contains(key)).ToArray())
		{
			if(_runtimes.Remove(key, out Runtime? removed))
			{
				await DisposeRuntimeAsync(removed, cancellationToken);
				_states.Release(key, out _);
			}
		}
	}

	internal async Task RefreshVisibilityAsync(CancellationToken cancellationToken = default)
	{
		if(_currentSessionId is not null && _runtimes.TryGetValue(_currentSessionId, out Runtime? runtime))
		{
			await ActivateVisibleAsync(runtime, cancellationToken);
		}
	}

	internal async Task OpenToolAsync(string toolId, string? groupId = null, CancellationToken cancellationToken = default)
	{
		Runtime runtime = RequireCurrent();
		if(!runtime.ToolSessions.TryGetValue(toolId, out IWorkspaceToolSession? toolSession) || !toolSession.IsAvailable)
		{
			return;
		}
		string instanceId = Guid.NewGuid().ToString("N");
		string targetGroup = groupId ?? runtime.State.FocusedGroupId;
		WorkspaceInstance model = new(instanceId, toolId);
		WorkspaceChangeResult result = _states.Apply(runtime.Session.Id, state => WorkspaceReducer.OpenNewTool(state, targetGroup, model));
		if(result.Status != WorkspaceChangeStatus.Applied)
		{
			return;
		}
		runtime.State = result.State;
		await CreateControllerAsync(runtime, model, null, cancellationToken);
		Publish(runtime);
		await ActivateVisibleAsync(runtime, cancellationToken);
		ScheduleSave(runtime);
	}

	internal void Activate(string groupId, string instanceId)
	{
		Runtime runtime = RequireCurrent();
		WorkspaceChangeResult result = _states.Apply(runtime.Session.Id, state => WorkspaceReducer.ActivateInstance(state, groupId, instanceId));
		if(result.Status == WorkspaceChangeStatus.Applied)
		{
			runtime.State = result.State;
			Publish(runtime);
			ScheduleSave(runtime);
			_ = ActivateVisibleAsync(runtime, CancellationToken.None);
		}
	}

	internal void Move(string instanceId, string groupId, int index)
	{
		Runtime runtime = RequireCurrent();
		WorkspaceChangeResult result = _states.Apply(runtime.Session.Id, state => WorkspaceReducer.MoveInstance(state, instanceId, groupId, index));
		if(result.Status == WorkspaceChangeStatus.Applied)
		{
			runtime.State = result.State;
			Publish(runtime);
			ScheduleSave(runtime);
		}
	}

	internal void MoveToGroupEnd(string instanceId, string groupId)
	{
		Runtime runtime = RequireCurrent();
		if(!runtime.State.Nodes.TryGetValue(groupId, out WorkspaceNode? node) || node is not WorkspaceGroupNode group)
		{
			return;
		}
		int index = group.OrderedInstanceIds.Count - (group.OrderedInstanceIds.Contains(instanceId, StringComparer.Ordinal) ? 1 : 0);
		Move(instanceId, groupId, index);
	}

	internal void Split(string instanceId, string targetGroupId, WorkspaceSplitAxis axis, bool first)
	{
		Runtime runtime = RequireCurrent();
		WorkspaceChangeResult result = _states.Apply(runtime.Session.Id, state => WorkspaceReducer.SplitGroupWithExistingInstance(
			state, instanceId, targetGroupId, Guid.NewGuid().ToString("N"), Guid.NewGuid().ToString("N"), axis, first));
		if(result.Status == WorkspaceChangeStatus.Applied)
		{
			runtime.State = result.State;
			Publish(runtime);
			ScheduleSave(runtime);
		}
	}

	internal void SplitAtRoot(string instanceId, WorkspaceSplitAxis axis, bool first)
	{
		Runtime runtime = RequireCurrent();
		WorkspaceChangeResult result = _states.Apply(runtime.Session.Id, state => WorkspaceReducer.SplitRootWithExistingInstance(
			state, instanceId, Guid.NewGuid().ToString("N"), Guid.NewGuid().ToString("N"), axis, first));
		if(result.Status == WorkspaceChangeStatus.Applied)
		{
			runtime.State = result.State;
			Publish(runtime);
			ScheduleSave(runtime);
		}
	}

	internal void ToggleMaximise(string groupId)
	{
		Runtime runtime = RequireCurrent();
		WorkspaceChangeResult result = _states.Apply(runtime.Session.Id, state => state.MaximisedGroupId == groupId
			? WorkspaceReducer.RestoreGroups(state)
			: WorkspaceReducer.MaximiseGroup(state, groupId));
		if(result.Status == WorkspaceChangeStatus.Applied)
		{
			runtime.State = result.State;
			Publish(runtime);
			ScheduleSave(runtime);
		}
	}

	internal void SetSplitRatio(string splitId, double ratio)
	{
		Runtime runtime = RequireCurrent();
		WorkspaceChangeResult result = _states.Apply(runtime.Session.Id, state => WorkspaceReducer.SetSplitRatio(state, splitId, Math.Clamp(ratio, 0.1, 0.9)));
		if(result.Status == WorkspaceChangeStatus.Applied)
		{
			runtime.State = result.State;
			Publish(runtime);
			ScheduleSave(runtime);
		}
	}

	internal async ValueTask<ToolCloseDecision> GetCloseDecisionAsync(string instanceId, CancellationToken cancellationToken = default)
	{
		Runtime runtime = RequireCurrent();
		return runtime.Controllers.TryGetValue(instanceId, out Controller? controller)
			? await controller.Instance.CanCloseAsync(ToolCloseReason.UserClosedTab, cancellationToken)
			: ToolCloseDecision.Allow();
	}

	internal async Task CloseAsync(string instanceId, bool confirmed, CancellationToken cancellationToken = default)
	{
		Runtime runtime = RequireCurrent();
		if(runtime.Controllers.TryGetValue(instanceId, out Controller? controller) && !confirmed)
		{
			ToolCloseDecision decision = await controller.Instance.CanCloseAsync(ToolCloseReason.UserClosedTab, cancellationToken);
			if(decision.Kind != ToolCloseDecisionKind.Allow)
			{
				return;
			}
		}
		WorkspaceChangeResult result = _states.Apply(runtime.Session.Id, state => WorkspaceReducer.CloseInstance(state, instanceId));
		if(result.Status != WorkspaceChangeStatus.Applied)
		{
			return;
		}
		runtime.State = result.State;
		if(runtime.Controllers.Remove(instanceId, out controller))
		{
			controller.Instance.StateChanged -= controller.StateChangedHandler;
			await controller.Instance.DisposeAsync();
			controller.Lifetime.Cancel();
			controller.Lifetime.Dispose();
		}
		Publish(runtime);
		ScheduleSave(runtime);
	}

	async Task<Runtime> CreateRuntimeAsync(SessionModel session, CancellationToken cancellationToken)
	{
		WorkspaceSessionInfo info = ToSessionInfo(session);
		WorkspaceStorageLocations locations = WorkspaceStorageLocator.ForSession(FileSystem.AppDataDirectory, session.Id, session.Context.WorkspacePath);
		WorkspaceLoadResult loaded = await _store.LoadAsync(locations.Primary, cancellationToken);
		string loadRoot = locations.Primary;
		if(loaded.Status == WorkspaceLoadStatus.NotFound && locations.Fallback is not null)
		{
			loaded = await _store.LoadAsync(locations.Fallback, cancellationToken);
			loadRoot = locations.Fallback;
		}

		WorkspaceState state;
		if(loaded.Manifest is not null)
		{
			state = loaded.Manifest.ToState(session.Id);
		}
		else
		{
			WorkspaceLoadResult defaults = await _store.LoadAsync(WorkspaceStorageLocator.ForDefaults(FileSystem.AppDataDirectory), cancellationToken);
			state = defaults.Manifest is not null
				? defaults.Manifest.ToState(session.Id) with { SessionStorageId = session.Id, IdentityEpoch = 0, PersistedRevision = -1 }
				: WorkspaceState.Empty(session.Id, session.Id, Guid.NewGuid().ToString("N"));
			if(defaults.Manifest is not null)
			{
				loadRoot = WorkspaceStorageLocator.ForDefaults(FileSystem.AppDataDirectory);
			}
		}
		_states.AttachRestored(state);
		Runtime runtime = new(session, info, state, locations.Primary, loadRoot);

		foreach(RegisteredWorkspaceTool registered in _registry.Tools)
		{
			try
			{
				IWorkspaceToolSession toolSession = await registered.Definition.CreateSessionAsync(new(info, new Dictionary<string, object?>()), cancellationToken);
				await toolSession.RefreshAvailabilityAsync(info, cancellationToken);
				runtime.ToolSessions[registered.Descriptor.ToolId] = toolSession;
				runtime.State = WorkspaceReducer.SetToolAvailability(runtime.State, registered.Descriptor.ToolId,
					toolSession.IsAvailable ? WorkspaceAvailabilityState.available : new(WorkspaceToolAvailability.Unavailable, toolSession.UnavailabilityReason));
			}
			catch(Exception exception)
			{
				runtime.State = WorkspaceReducer.SetToolAvailability(runtime.State, registered.Descriptor.ToolId,
					new(WorkspaceToolAvailability.EvaluationFailed, exception.Message));
			}
		}
		_states.AttachRestored(runtime.State);

		if(runtime.State.Instances.Count == 0)
		{
			foreach(string toolId in new[] { "cockpit.session-context", "cockpit.git-changes" })
			{
				if(runtime.ToolSessions.TryGetValue(toolId, out IWorkspaceToolSession? toolSession) && toolSession.IsAvailable)
				{
					WorkspaceInstance model = new(Guid.NewGuid().ToString("N"), toolId);
					WorkspaceChangeResult opened = WorkspaceReducer.OpenNewTool(runtime.State, runtime.State.FocusedGroupId, model);
					runtime.State = opened.State;
				}
			}
			_states.AttachRestored(runtime.State);
		}

		foreach(WorkspaceInstance instance in runtime.State.Instances.Values)
		{
			ToolStateReadContext? saved = instance.Checkpoint is null ? null : await ReadCheckpointAsync(runtime, instance, cancellationToken);
			await CreateControllerAsync(runtime, instance, saved, cancellationToken);
		}
		if(loaded.Manifest is null)
		{
			ScheduleSave(runtime);
		}
		return runtime;
	}

	async Task CreateControllerAsync(Runtime runtime, WorkspaceInstance model, ToolStateReadContext? saved, CancellationToken cancellationToken)
	{
		if(!runtime.ToolSessions.TryGetValue(model.ToolId, out IWorkspaceToolSession? toolSession) || !toolSession.IsAvailable)
		{
			return;
		}
		Host host = new(this, runtime.Session.Id, model.Id);
		CancellationTokenSource lifetime = new();
		WorkspaceToolInstanceContext instanceContext = new(new(model.Id), runtime.SessionInfo, host, lifetime.Token);
		IWorkspaceToolInstance instance = await toolSession.CreateInstanceAsync(instanceContext, cancellationToken);
		await instance.RestoreAsync(saved, cancellationToken);
		void handler(ToolStateChanged _) => ScheduleSave(runtime);
		instance.StateChanged += handler;
		runtime.Controllers[model.Id] = new(instance, instanceContext, lifetime, handler);
	}

	async Task<ToolStateReadContext?> ReadCheckpointAsync(Runtime runtime, WorkspaceInstance model, CancellationToken cancellationToken)
	{
		WorkspaceCheckpointReference checkpoint = model.Checkpoint!;
		try
		{
			await using Stream stream = await _store.OpenObjectReadAsync(runtime.LoadRoot, checkpoint.ContentId, cancellationToken);
			using JsonDocument document = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken);
			return new ToolStateReadContext(checkpoint.SchemaVersion, checkpoint.StateVersion, new(model.Id), document.RootElement.Clone(),
				[.. (checkpoint.AssetContentIds ?? []).Select(id => new ToolAssetReference(id))],
				(asset, ct) => new(_store.OpenObjectReadAsync(runtime.LoadRoot, asset.Id, ct)));
		}
		catch
		{
			return null;
		}
	}

	async Task ActivateVisibleAsync(Runtime runtime, CancellationToken cancellationToken)
	{
		HashSet<string> active = _uiState.RightSidebarCollapsed ? [] : runtime.State.Nodes.Values.OfType<WorkspaceGroupNode>()
			.Where(group => runtime.State.MaximisedGroupId is null || runtime.State.MaximisedGroupId == group.Id)
			.Select(group => group.ActiveInstanceId).OfType<string>().ToHashSet(StringComparer.Ordinal);
		foreach((string id, Controller controller) in runtime.Controllers.ToArray())
		{
			if(active.Contains(id))
			{
				await controller.Instance.OnActivatedAsync(new(new(runtime.State.LayoutRevision), 0, 0, true, cancellationToken), cancellationToken);
			}
			else
			{
				await controller.Instance.OnDeactivatedAsync(cancellationToken);
			}
		}
	}

	void ScheduleSave(Runtime runtime)
	{
		runtime.SaveCancellation?.Cancel();
		runtime.SaveCancellation?.Dispose();
		runtime.SaveCancellation = new();
		_ = SaveAfterDelayAsync(runtime, runtime.SaveCancellation.Token);
	}

	async Task SaveAfterDelayAsync(Runtime runtime, CancellationToken cancellationToken)
	{
		try
		{
			await Task.Delay(500, cancellationToken);
			await SaveAsync(runtime, cancellationToken);
		}
		catch(OperationCanceledException) when(cancellationToken.IsCancellationRequested) { }
		catch(Exception exception)
		{
			_logger.LogWarning(exception, "Unable to persist the sidebar workspace for session {SessionId}", runtime.Session.Id);
		}
	}

	async Task SaveAsync(Runtime runtime, CancellationToken cancellationToken, string? destinationRoot = null, bool exportTemplate = false)
	{
		await runtime.SaveGate.WaitAsync(cancellationToken);
		try
		{
			Dictionary<string, byte[]> objects = new(StringComparer.Ordinal);
			WorkspaceState state = runtime.State;
			foreach((string id, Controller controller) in runtime.Controllers.ToArray())
			{
				ToolStateWriteContext destination = new(new(id), (name, mediaType, ct) => ValueTask.FromResult(CreateAssetWriter(objects, mediaType)));
				ToolCheckpoint captured = await controller.Instance.CaptureAsync(destination, cancellationToken);
				ToolCheckpoint checkpoint = captured;
				if(exportTemplate && state.Instances.TryGetValue(id, out WorkspaceInstance? model) &&
					_registry.TryGet(model.ToolId, out RegisteredWorkspaceTool? registered) && registered is not null)
				{
					ToolStateReadContext source = new(captured.SchemaVersion, captured.MutationVersion, new(id), captured.Metadata,
						captured.Assets, (asset, ct) => ValueTask.FromResult<Stream>(new MemoryStream(objects[asset.Id], writable: false)));
					ToolTemplateExportResult exported = await registered.Definition.ExportTemplateAsync(
						source, new(new(id), destination, new Dictionary<string, object?>()), cancellationToken);
					if(exported.Status is (ToolTemplateExportStatus.Prepared or ToolTemplateExportStatus.Partial) && exported.Seed is not null)
					{
						checkpoint = exported.Seed;
					}
				}
				byte[] metadata = Encoding.UTF8.GetBytes(checkpoint.Metadata.GetRawText());
				string contentId = await AddObjectAsync(objects, metadata, cancellationToken);
				state = WorkspaceReducer.UpdateCheckpoint(state, id, new(contentId, checkpoint.SchemaVersion, checkpoint.MutationVersion, [.. checkpoint.Assets.Select(asset => asset.Id)]));
			}
			runtime.State = state;
			_states.AttachRestored(state);
			IWorkspaceWriteLease? lease = await _store.TryAcquireWriteLeaseAsync(destinationRoot ?? runtime.StorageRoot, cancellationToken);
			if(lease is null)
			{
				return;
			}
			await using(lease)
			{
				IReadOnlyCollection<WorkspaceObjectWrite> writes = [.. objects.Select(pair => new WorkspaceObjectWrite(pair.Key, _ => ValueTask.FromResult<Stream>(new MemoryStream(pair.Value, writable: false))))];
				await _store.CommitAsync(lease, new($"{DateTimeOffset.UtcNow:yyyyMMddHHmmssfffffff}-{Guid.NewGuid():N}", WorkspaceManifest.FromState(state, runtime.Session.Id), writes), cancellationToken);
			}
		}
		finally
		{
			runtime.SaveGate.Release();
		}
	}

	static ToolAssetWriter CreateAssetWriter(Dictionary<string, byte[]> objects, string? mediaType)
	{
		MemoryStream stream = new();
		return new ToolAssetWriter(stream, async cancellationToken =>
		{
			byte[] bytes = stream.ToArray();
			string id = await AddObjectAsync(objects, bytes, cancellationToken);
			return new ToolAssetReference(id, mediaType, bytes.LongLength);
		});
	}

	static async Task<string> AddObjectAsync(Dictionary<string, byte[]> objects, byte[] bytes, CancellationToken cancellationToken)
	{
		await using MemoryStream hashStream = new(bytes, writable: false);
		string id = await WorkspaceContentId.ComputeAsync(hashStream, cancellationToken);
		objects[id] = bytes;
		return id;
	}

	void Publish(Runtime runtime)
	{
		if(_currentSessionId != runtime.Session.Id)
		{
			return;
		}
		Dictionary<string, WorkspaceRenderedInstance> instances = new(StringComparer.Ordinal);
		foreach(WorkspaceInstance model in runtime.State.Instances.Values)
		{
			_registry.TryGet(model.ToolId, out RegisteredWorkspaceTool? registered);
			runtime.Controllers.TryGetValue(model.Id, out Controller? controller);
			instances[model.Id] = new(model, registered?.Descriptor, controller?.Instance,
				controller is null ? null : new(controller.Instance, controller.Context),
				registered is null ? "This tool is not installed." : controller is null ? "This tool is unavailable for this session." : null);
		}
		Current = new(runtime.State, WorkspaceVisibleProjection.Create(runtime.State), instances);
		Changed?.Invoke();
	}

	Runtime RequireCurrent() => _currentSessionId is not null && _runtimes.TryGetValue(_currentSessionId, out Runtime? runtime)
		? runtime : throw new InvalidOperationException("No session workspace is active.");

	static WorkspaceSessionInfo ToSessionInfo(SessionModel session) => new(
		new(session.Id), new(session.Id), 0,
		DeviceInfo.Current.Platform.ToString(), session.Context.CurrentWorkingDirectory,
		session.Context.GitRoot, session.Lifecycle.SdkState.ToString(), new HashSet<string>(StringComparer.Ordinal));

	void HandleSessionsChanged() => _ = EnsureCurrentAsync();
	void HandleWorkspaceChanged(string key, WorkspaceState state)
	{
		if(_runtimes.TryGetValue(key, out Runtime? runtime))
		{
			runtime.State = state;
			Publish(runtime);
		}
	}

	public async ValueTask DisposeAsync()
	{
		_sessions.OnStateChanged -= HandleSessionsChanged;
		_states.StateChanged -= HandleWorkspaceChanged;
		foreach(Runtime runtime in _runtimes.Values)
		{
			await DisposeRuntimeAsync(runtime, CancellationToken.None);
		}
		_gate.Dispose();
	}

	static async Task DisposeRuntimeAsync(Runtime runtime, CancellationToken cancellationToken)
	{
		runtime.SaveCancellation?.Cancel();
		foreach(Controller controller in runtime.Controllers.Values)
		{
			await controller.Instance.DisposeAsync();
			controller.Lifetime.Cancel();
			controller.Lifetime.Dispose();
		}
		foreach(IWorkspaceToolSession session in runtime.ToolSessions.Values)
		{
			await session.DisposeAsync();
		}
		runtime.SaveGate.Dispose();
	}

	sealed class Runtime(SessionModel session, WorkspaceSessionInfo sessionInfo, WorkspaceState state, string storageRoot, string loadRoot)
	{
		internal SessionModel Session { get; } = session;
		internal WorkspaceSessionInfo SessionInfo { get; } = sessionInfo;
		internal WorkspaceState State { get; set; } = state;
		internal string StorageRoot { get; } = storageRoot;
		internal string LoadRoot { get; } = loadRoot;
		internal Dictionary<string, IWorkspaceToolSession> ToolSessions { get; } = new(StringComparer.Ordinal);
		internal Dictionary<string, Controller> Controllers { get; } = new(StringComparer.Ordinal);
		internal SemaphoreSlim SaveGate { get; } = new(1, 1);
		internal CancellationTokenSource? SaveCancellation { get; set; }
	}

	sealed record Controller(IWorkspaceToolInstance Instance, WorkspaceToolInstanceContext Context, CancellationTokenSource Lifetime, Action<ToolStateChanged> StateChangedHandler);

	sealed class Host(WorkspaceFeature owner, string sessionId, string instanceId) : IWorkspaceToolHost
	{
		public ValueTask<WorkspaceHostCommandResult> ActivateSelfAsync(CancellationToken cancellationToken)
		{
			Runtime runtime = owner.RequireCurrent();
			WorkspaceGroupNode? group = runtime.State.Nodes.Values.OfType<WorkspaceGroupNode>().FirstOrDefault(item => item.OrderedInstanceIds.Contains(instanceId));
			if(runtime.Session.Id != sessionId || group is null)
			{
				return ValueTask.FromResult(new WorkspaceHostCommandResult(WorkspaceHostCommandStatus.SessionNotCurrent));
			}

			owner.Activate(group.Id, instanceId);
			return ValueTask.FromResult(WorkspaceHostCommandResult.Applied());
		}
		public async ValueTask<WorkspaceHostCommandResult> CloseSelfAsync(CancellationToken cancellationToken)
		{
			if(owner._currentSessionId != sessionId)
			{
				return new(WorkspaceHostCommandStatus.SessionNotCurrent);
			}

			ToolCloseDecision decision = await owner.GetCloseDecisionAsync(instanceId, cancellationToken);
			if(decision.Kind != ToolCloseDecisionKind.Allow)
			{
				return new(WorkspaceHostCommandStatus.Rejected, decision.Message);
			}

			await owner.CloseAsync(instanceId, false, cancellationToken);
			return WorkspaceHostCommandResult.Applied();
		}
		public async ValueTask<WorkspaceHostCommandResult> OpenNewToolAsync(string toolId, CancellationToken cancellationToken)
		{
			if(owner._currentSessionId != sessionId)
			{
				return new(WorkspaceHostCommandStatus.SessionNotCurrent);
			}

			await owner.OpenToolAsync(toolId, cancellationToken: cancellationToken);
			return WorkspaceHostCommandResult.Applied();
		}
		public ValueTask<WorkspaceHostCommandResult> SetTitleAsync(string? title, CancellationToken cancellationToken) => ValueTask.FromResult(new WorkspaceHostCommandResult(WorkspaceHostCommandStatus.NoChange));
		public ValueTask<WorkspaceHostCommandResult> RequestLayoutNotificationAsync(CancellationToken cancellationToken) => ValueTask.FromResult(WorkspaceHostCommandResult.Applied());
		public ValueTask<WorkspaceHostCommandResult> AppendToComposerAsync(string content, CancellationToken cancellationToken)
		{
			if(owner._currentSessionId != sessionId)
			{
				return ValueTask.FromResult(new WorkspaceHostCommandResult(WorkspaceHostCommandStatus.SessionNotCurrent));
			}

			owner._uiState.AppendChatInput(content);
			return ValueTask.FromResult(WorkspaceHostCommandResult.Applied());
		}
		public ValueTask<WorkspaceHostCommandResult> RevealFileAsync(string path, CancellationToken cancellationToken)
		{
			if(!File.Exists(path))
			{
				return ValueTask.FromResult(new WorkspaceHostCommandResult(WorkspaceHostCommandStatus.InvalidTarget));
			}

			Utilities.FileUtil.RevealFile(path);
			return ValueTask.FromResult(WorkspaceHostCommandResult.Applied());
		}
	}
}
