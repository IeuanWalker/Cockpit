using System.Reflection;
using Microsoft.Extensions.DependencyInjection;

namespace Cockpit.Tooling.Discovery;

public static class WorkspaceToolServiceCollectionExtensions
{
	sealed class WorkspaceToolRegistrationMarker;

	public static IServiceCollection AddCockpitWorkspace(
		this IServiceCollection services,
		Assembly? hostAssembly = null) =>
		services.AddCockpitWorkspace(
			WorkspaceToolAssemblyCatalog.FromEmbeddedResource(hostAssembly ?? Assembly.GetEntryAssembly()
				?? throw new InvalidOperationException("No entry assembly is available.")));

	public static IServiceCollection AddCockpitWorkspace(
		this IServiceCollection services,
		IWorkspaceToolAssemblyCatalog catalog)
	{
		ArgumentNullException.ThrowIfNull(services);
		ArgumentNullException.ThrowIfNull(catalog);
		if(services.Any(descriptor => descriptor.ServiceType == typeof(WorkspaceToolRegistrationMarker)))
		{
			return services;
		}

		services.AddSingleton<WorkspaceToolRegistrationMarker>();
		WorkspaceToolDiscoveryDiagnostics diagnostics = new();
		WorkspaceToolScanResult scan = WorkspaceToolTypeScanner.Scan(catalog, diagnostics);
		HashSet<Assembly> disabledAssemblies = [];

		foreach(IGrouping<Assembly, Type> moduleGroup in scan.ModuleTypes.GroupBy(type => type.Assembly))
		{
			if(moduleGroup.Count() != 1)
			{
				diagnostics.Add(new(
					WorkspaceToolDiscoveryDiagnosticKind.ModuleInvalid,
					moduleGroup.Key.GetName().Name ?? "unknown",
					"A tool assembly must declare at most one public IWorkspaceToolModule."));
				disabledAssemblies.Add(moduleGroup.Key);
				continue;
			}

			Type moduleType = moduleGroup.Single();
			if(moduleType.GetConstructor(Type.EmptyTypes) is null)
			{
				diagnostics.Add(new(
					WorkspaceToolDiscoveryDiagnosticKind.ModuleInvalid,
					moduleType.FullName ?? moduleType.Name,
					"The module must have a public parameterless constructor."));
				disabledAssemblies.Add(moduleGroup.Key);
				continue;
			}

			ServiceCollection staged = new();
			try
			{
				IWorkspaceToolModule module = (IWorkspaceToolModule)Activator.CreateInstance(moduleType)!;
				module.ConfigureServices(staged);
				if(staged.Any(IsProtectedHostContract))
				{
					throw new InvalidOperationException("A tool module cannot register or replace tooling host contracts.");
				}

				foreach(ServiceDescriptor descriptor in staged)
				{
					services.Add(descriptor);
				}
			}
			catch(Exception exception)
			{
				disabledAssemblies.Add(moduleGroup.Key);
				diagnostics.Add(new(
					WorkspaceToolDiscoveryDiagnosticKind.ModuleFailed,
					moduleType.FullName ?? moduleType.Name,
					exception.Message,
					exception));
			}
		}

		foreach(Type type in scan.DefinitionTypes)
		{
			if(disabledAssemblies.Contains(type.Assembly))
			{
				continue;
			}

			services.AddSingleton(type, provider => ActivatorUtilities.CreateInstance(provider, type));
		}

		services.AddSingleton(diagnostics);
		services.AddSingleton(provider => new WorkspaceToolRegistry(
			provider,
			scan.DefinitionTypes.Where(type => !disabledAssemblies.Contains(type.Assembly)).ToArray(),
			diagnostics));
		return services;
	}

	static bool IsProtectedHostContract(ServiceDescriptor descriptor) =>
		descriptor.ServiceType == typeof(IWorkspaceTool) ||
		descriptor.ServiceType == typeof(IWorkspaceToolSession) ||
		descriptor.ServiceType == typeof(IWorkspaceToolInstance) ||
		descriptor.ServiceType == typeof(IWorkspaceToolHost) ||
		descriptor.ServiceType == typeof(WorkspaceToolRegistry) ||
		descriptor.ServiceType == typeof(WorkspaceToolDiscoveryDiagnostics);
}
