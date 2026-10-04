using System.Reflection;

namespace Cockpit.Tooling.Discovery;

sealed record WorkspaceToolScanResult(
	IReadOnlyList<Type> DefinitionTypes,
	IReadOnlyList<Type> ModuleTypes);

static class WorkspaceToolTypeScanner
{
	public static WorkspaceToolScanResult Scan(
		IWorkspaceToolAssemblyCatalog catalog,
		WorkspaceToolDiscoveryDiagnostics diagnostics)
	{
		List<Type> definitions = [];
		List<Type> modules = [];

		foreach(string? assemblyName in catalog.AssemblyNames
					 .Distinct(StringComparer.Ordinal)
					 .Order(StringComparer.Ordinal))
		{
			Assembly assembly;
			try
			{
				assembly = Assembly.Load(new AssemblyName(assemblyName));
			}
			catch(Exception exception) when(exception is FileNotFoundException or FileLoadException or BadImageFormatException)
			{
				diagnostics.Add(new(
					WorkspaceToolDiscoveryDiagnosticKind.AssemblyLoadFailed,
					assemblyName,
					exception.Message,
					exception));
				continue;
			}

			IReadOnlyList<Type> types = GetLoadableTypes(assembly, diagnostics);
			definitions.AddRange(types.Where(IsEligibleDefinition));
			modules.AddRange(types.Where(IsEligibleModule));
		}

		return new WorkspaceToolScanResult(
			definitions.OrderBy(type => type.AssemblyQualifiedName, StringComparer.Ordinal).ToArray(),
			modules.OrderBy(type => type.AssemblyQualifiedName, StringComparer.Ordinal).ToArray());
	}

	static IReadOnlyList<Type> GetLoadableTypes(
		Assembly assembly,
		WorkspaceToolDiscoveryDiagnostics diagnostics)
	{
		try
		{
			return assembly.GetTypes();
		}
		catch(ReflectionTypeLoadException exception)
		{
			foreach(Exception? loaderException in exception.LoaderExceptions.Where(item => item is not null))
			{
				diagnostics.Add(new(
					WorkspaceToolDiscoveryDiagnosticKind.TypeLoadFailed,
					assembly.GetName().Name ?? assembly.FullName ?? "unknown",
					loaderException!.Message,
					loaderException));
			}
			return exception.Types.OfType<Type>().ToArray();
		}
	}

	static bool IsEligibleDefinition(Type type) =>
		type.IsVisible &&
		type.IsClass &&
		!type.IsAbstract &&
		!type.ContainsGenericParameters &&
		typeof(IWorkspaceTool).IsAssignableFrom(type) &&
		type.GetCustomAttribute<WorkspaceToolIgnoreAttribute>() is null;

	static bool IsEligibleModule(Type type) =>
		type.IsVisible &&
		type.IsClass &&
		!type.IsAbstract &&
		!type.ContainsGenericParameters &&
		typeof(IWorkspaceToolModule).IsAssignableFrom(type);
}
