namespace Cockpit.Tooling.Discovery;

public enum WorkspaceToolDiscoveryDiagnosticKind
{
	AssemblyLoadFailed,
	TypeLoadFailed,
	ModuleInvalid,
	ModuleFailed,
	DefinitionFailed,
	DescriptorInvalid,
	IdentityConflict
}

public sealed record WorkspaceToolDiscoveryDiagnostic(
	WorkspaceToolDiscoveryDiagnosticKind Kind,
	string Source,
	string Message,
	Exception? Exception = null);

public sealed class WorkspaceToolDiscoveryDiagnostics
{
	readonly Lock _sync = new();
	readonly List<WorkspaceToolDiscoveryDiagnostic> _items = [];

	public IReadOnlyList<WorkspaceToolDiscoveryDiagnostic> Items
	{
		get
		{
			lock(_sync)
			{
				return _items.ToArray();
			}
		}
	}

	internal void Add(WorkspaceToolDiscoveryDiagnostic diagnostic)
	{
		lock(_sync)
		{
			_items.Add(diagnostic);
		}
	}
}
