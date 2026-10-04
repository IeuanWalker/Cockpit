using System.Text.Json;
using Cockpit.Tooling;
using Cockpit.Tools.SessionContext.Sources;
using Cockpit.Tools.SessionContext.State;

namespace Cockpit.Tools.SessionContext;

public sealed class SessionContextWorkspaceTool : IWorkspaceTool
{
	readonly ISessionContextToolSource _source;

	public SessionContextWorkspaceTool(ISessionContextToolSource source) => _source = source;

	public WorkspaceToolDescriptor Descriptor { get; } = new()
	{
		ToolId = "cockpit.session-context",
		ApiVersion = new Version(1, 0),
		DisplayName = "Session context",
		Description = "Working directory, repository and session capabilities.",
		Icon = "context",
		StateSchemaVersion = SessionContextState.CurrentSchemaVersion,
		Content = new WorkspaceToolContentDescriptor.Razor(typeof(SessionContextTool)),
		TemplateEditor = new WorkspaceToolTemplateEditorDescriptor(typeof(SessionContextTemplateEditor))
	};

	public ValueTask<IWorkspaceToolSession> CreateSessionAsync(
		WorkspaceToolSessionContext context,
		CancellationToken cancellationToken)
	{
		cancellationToken.ThrowIfCancellationRequested();
		IWorkspaceToolSession session = new SessionContextToolSession(context.Session, _source);
		return ValueTask.FromResult(session);
	}

	public ValueTask<ToolTemplateExportResult> ExportTemplateAsync(
		ToolStateReadContext source,
		ToolTemplateExportContext target,
		CancellationToken cancellationToken)
	{
		cancellationToken.ThrowIfCancellationRequested();
		if(!TryRead(source, out SessionContextState? state, out string? error))
		{
			return ValueTask.FromResult(new ToolTemplateExportResult(ToolTemplateExportStatus.Failed, null, Message: error));
		}

		bool includeCached = target.Options.TryGetValue("includeCachedContext", out object? option) && option is true;
		SessionContextState template = state! with
		{
			ScrollAnchor = null,
			CachedContext = includeCached ? state.CachedContext : null
		};
		ToolCheckpoint checkpoint = CreateCheckpoint(template, source.MutationVersion);
		return ValueTask.FromResult(new ToolTemplateExportResult(
			ToolTemplateExportStatus.Prepared,
			checkpoint,
			Included: ["section presentation", "filter", "selection"],
			Excluded: includeCached ? [] : ["live context", "scroll position"]));
	}

	public ValueTask<ToolStateCloneResult> CloneStateAsync(
		ToolStateReadContext source,
		ToolStateCloneContext target,
		CancellationToken cancellationToken)
	{
		cancellationToken.ThrowIfCancellationRequested();
		if(!TryRead(source, out SessionContextState? state, out string? error))
		{
			return ValueTask.FromResult(new ToolStateCloneResult(ToolStateCloneStatus.NeedsRecovery, null, error));
		}

		// Cached facts are retained as labelled display content; the destination source
		// remains authoritative and replaces them after its first successful refresh.
		return ValueTask.FromResult(new ToolStateCloneResult(
			ToolStateCloneStatus.Prepared,
			CreateCheckpoint(state!, 0)));
	}

	static bool TryRead(ToolStateReadContext source, out SessionContextState? state, out string? error)
	{
		state = null;
		error = null;
		if(source.SchemaVersion is not (1 or SessionContextState.CurrentSchemaVersion))
		{
			error = $"Unsupported session-context state version {source.SchemaVersion}.";
			return false;
		}

		try
		{
			state = source.Metadata.Deserialize(SessionContextJsonContext.Default.SessionContextState);
			if(state is null)
			{
				error = "Session-context state was empty.";
				return false;
			}
			if(source.SchemaVersion == 1)
			{
				state = state with
				{
					SchemaVersion = SessionContextState.CurrentSchemaVersion,
					CachedContext = null
				};
			}
			return true;
		}
		catch(JsonException ex)
		{
			error = ex.Message;
			return false;
		}
	}

	static ToolCheckpoint CreateCheckpoint(SessionContextState state, long mutationVersion) => new(
		SessionContextState.CurrentSchemaVersion,
		mutationVersion,
		JsonSerializer.SerializeToElement(state, SessionContextJsonContext.Default.SessionContextState),
		[]);
}
