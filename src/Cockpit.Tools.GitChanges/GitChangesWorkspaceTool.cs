using System.Text.Json;
using Cockpit.Tooling;
using Cockpit.Tools.GitChanges.Sources;
using Cockpit.Tools.GitChanges.State;

namespace Cockpit.Tools.GitChanges;

public sealed class GitChangesWorkspaceTool : IWorkspaceTool
{
	readonly IGitChangesToolSource _source;
	public GitChangesWorkspaceTool(IGitChangesToolSource source) => _source = source;

	public WorkspaceToolDescriptor Descriptor { get; } = new()
	{
		ToolId = "cockpit.git-changes",
		ApiVersion = new Version(1, 0),
		DisplayName = "Git changes",
		Description = "Browse changed files and inspect diffs.",
		Icon = "git-changes",
		StateSchemaVersion = GitChangesState.CurrentSchemaVersion,
		Content = new WorkspaceToolContentDescriptor.Razor(typeof(GitChangesTool)),
		TemplateEditor = new WorkspaceToolTemplateEditorDescriptor(typeof(GitChangesTemplateEditor))
	};

	public async ValueTask<IWorkspaceToolSession> CreateSessionAsync(WorkspaceToolSessionContext context, CancellationToken cancellationToken)
	{
		WorkspaceSessionInfo session = context.Session;
		GitAvailability availability = await _source.GetAvailabilityAsync(session.RuntimeSessionKey.Value,
			session.IdentityEpoch, session.RepositoryRoot, cancellationToken);
		return new GitChangesToolSession(session, _source, availability);
	}

	public async ValueTask<ToolTemplateExportResult> ExportTemplateAsync(
		ToolStateReadContext source, ToolTemplateExportContext target, CancellationToken cancellationToken)
	{
		GitChangesState? state = Read(source, out string? error);
		if(state is null)
		{
			return new(ToolTemplateExportStatus.Failed, null, Message: error);
		}

		bool includeDisplay = target.Options.TryGetValue("includeCachedDisplay", out object? option) && option is true;
		if(!includeDisplay)
		{
			state = state with { CachedDisplay = null, ScrollAnchor = null, SelectedHunk = null };
			return new(ToolTemplateExportStatus.Prepared, Checkpoint(state, 0, []),
				Included: ["repository binding", "comparison", "view configuration", "selection"],
				Excluded: ["cached diff", "scroll position"]);
		}
		(ToolCheckpoint checkpoint, string? copyError) = await CopyCheckpointAsync(state, source, target.Destination, cancellationToken);
		return new(copyError is null ? ToolTemplateExportStatus.Prepared : ToolTemplateExportStatus.Partial,
			checkpoint, Included: ["configuration", "selected cached diff"], Message: copyError);
	}

	public async ValueTask<ToolStateCloneResult> CloneStateAsync(
		ToolStateReadContext source, ToolStateCloneContext target, CancellationToken cancellationToken)
	{
		GitChangesState? state = Read(source, out string? error);
		if(state is null)
		{
			return new(ToolStateCloneStatus.NeedsRecovery, null, error);
		}

		(ToolCheckpoint checkpoint, string? copyError) = await CopyCheckpointAsync(state, source, target.Destination, cancellationToken);
		return new(copyError is null ? ToolStateCloneStatus.Prepared : ToolStateCloneStatus.NeedsRecovery, checkpoint, copyError);
	}

	static GitChangesState? Read(ToolStateReadContext source, out string? error)
	{
		error = null;
		if(source.SchemaVersion != GitChangesState.CurrentSchemaVersion)
		{
			error = $"Unsupported Git changes state version {source.SchemaVersion}.";
			return null;
		}
		try { return source.Metadata.Deserialize(GitChangesJsonContext.Default.GitChangesState); }
		catch(JsonException ex) { error = ex.Message; return null; }
	}

	static async ValueTask<(ToolCheckpoint Checkpoint, string? Error)> CopyCheckpointAsync(
		GitChangesState state, ToolStateReadContext source, ToolStateWriteContext target, CancellationToken cancellationToken)
	{
		CachedGitDisplay? cachedDisplay = state.CachedDisplay;
		if(cachedDisplay?.DiffAssetId is not string sourceId)
		{
			return (Checkpoint(state, 0, []), null);
		}

		ToolAssetReference? sourceAsset = source.Assets.FirstOrDefault(asset => asset.Id == sourceId);
		if(sourceAsset is null)
		{
			return (Checkpoint(state with { CachedDisplay = cachedDisplay with { DiffAssetId = null } }, 0, []), "Cached diff asset was unavailable.");
		}

		try
		{
			await using Stream input = await source.OpenAssetAsync(sourceAsset, cancellationToken);
			await using ToolAssetWriter writer = await target.CreateAssetAsync("selected.diff", "text/x-diff", cancellationToken);
			await input.CopyToAsync(writer.Stream, cancellationToken);
			ToolAssetReference copied = await writer.CommitAsync(cancellationToken);
			state = state with { CachedDisplay = cachedDisplay with { DiffAssetId = copied.Id } };
			return (Checkpoint(state, 0, [copied]), null);
		}
		catch(IOException ex)
		{
			return (Checkpoint(state with { CachedDisplay = cachedDisplay with { DiffAssetId = null } }, 0, []), ex.Message);
		}
	}

	static ToolCheckpoint Checkpoint(GitChangesState state, long mutationVersion, IReadOnlyList<ToolAssetReference> assets) => new(
		GitChangesState.CurrentSchemaVersion, mutationVersion,
		JsonSerializer.SerializeToElement(state, GitChangesJsonContext.Default.GitChangesState), assets,
		state.CachedDisplay is null ? ToolRestorationFidelity.ConfigurationOnly : ToolRestorationFidelity.Exact);
}
