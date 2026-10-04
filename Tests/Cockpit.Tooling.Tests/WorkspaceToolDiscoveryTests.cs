using Cockpit.Tooling.Discovery;
using Microsoft.Extensions.DependencyInjection;

namespace Cockpit.Tooling.Tests;

public sealed class WorkspaceToolDiscoveryTests
{
	[Fact]
	public void Referenced_catalog_discovers_tools_modules_and_ignores_marked_types()
	{
		ServiceCollection services = new();
		services.AddCockpitWorkspace(new WorkspaceToolAssemblyCatalog([typeof(SampleTool).Assembly.GetName().Name!]));
		using ServiceProvider provider = services.BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = true });

		WorkspaceToolRegistry registry = provider.GetRequiredService<WorkspaceToolRegistry>();

		RegisteredWorkspaceTool tool = Assert.Single(registry.Tools);
		Assert.Equal("example.sample", tool.Descriptor.ToolId);
		Assert.IsType<SampleTool>(tool.Definition);
		Assert.DoesNotContain(registry.Tools, item => item.DefinitionType == typeof(IgnoredTool));
		Assert.True(registry.TryGet("example.old-sample", out RegisteredWorkspaceTool? resolved));
		Assert.Same(tool, resolved);
	}

	[Fact]
	public void Conflicting_ids_and_broken_definitions_are_local_diagnostics()
	{
		ServiceCollection services = new();
		services.AddCockpitWorkspace(new WorkspaceToolAssemblyCatalog([typeof(SampleTool).Assembly.GetName().Name!]));
		using ServiceProvider provider = services.BuildServiceProvider();

		WorkspaceToolRegistry registry = provider.GetRequiredService<WorkspaceToolRegistry>();
		WorkspaceToolDiscoveryDiagnostics diagnostics = provider.GetRequiredService<WorkspaceToolDiscoveryDiagnostics>();

		Assert.Single(registry.Tools);
		Assert.Contains(diagnostics.Items, item => item.Kind == WorkspaceToolDiscoveryDiagnosticKind.IdentityConflict);
		Assert.Contains(diagnostics.Items, item => item.Kind == WorkspaceToolDiscoveryDiagnosticKind.DefinitionFailed);
	}

	[Fact]
	public void Registration_is_idempotent()
	{
		ServiceCollection services = new();
		WorkspaceToolAssemblyCatalog catalog = new([typeof(SampleTool).Assembly.GetName().Name!]);

		services.AddCockpitWorkspace(catalog);
		services.AddCockpitWorkspace(catalog);
		using ServiceProvider provider = services.BuildServiceProvider();

		Assert.Single(provider.GetServices<WorkspaceToolRegistry>());
	}
}

public sealed class TestToolModule : IWorkspaceToolModule
{
	public void ConfigureServices(IServiceCollection services) => services.AddSingleton<SampleDependency>();
}

public sealed class SampleDependency;

public sealed class SampleTool(SampleDependency dependency) : TestWorkspaceTool("example.sample", ["example.old-sample"])
{
	public SampleDependency Dependency { get; } = dependency;
}

[WorkspaceToolIgnore]
public sealed class IgnoredTool() : TestWorkspaceTool("example.ignored");

public sealed class ConflictingToolOne() : TestWorkspaceTool("example.conflict-one", ["example.shared"]);
public sealed class ConflictingToolTwo() : TestWorkspaceTool("example.conflict-two", ["example.shared"]);

public sealed class MissingDependency;
public sealed class BrokenTool(MissingDependency dependency) : TestWorkspaceTool("example.broken")
{
	public MissingDependency Dependency { get; } = dependency;
}

public abstract class TestWorkspaceTool(string id, IReadOnlyList<string>? aliases = null) : IWorkspaceTool
{
	public WorkspaceToolDescriptor Descriptor { get; } = TestTools.Descriptor(id) with
	{
		FormerToolIds = aliases ?? []
	};

	public ValueTask<IWorkspaceToolSession> CreateSessionAsync(WorkspaceToolSessionContext context, CancellationToken cancellationToken) =>
		ValueTask.FromResult<IWorkspaceToolSession>(new TestWorkspaceToolSession(context));

	public ValueTask<ToolTemplateExportResult> ExportTemplateAsync(ToolStateReadContext source, ToolTemplateExportContext target, CancellationToken cancellationToken) =>
		ValueTask.FromResult(new ToolTemplateExportResult(ToolTemplateExportStatus.Unsupported, null));

	public ValueTask<ToolStateCloneResult> CloneStateAsync(ToolStateReadContext source, ToolStateCloneContext target, CancellationToken cancellationToken) =>
		ValueTask.FromResult(new ToolStateCloneResult(ToolStateCloneStatus.Deferred, null));
}

public sealed class TestWorkspaceToolSession(WorkspaceToolSessionContext context) : IWorkspaceToolSession
{
	public bool IsAvailable { get; private set; } = context.Session.Capabilities.Contains("sample");
	public string? UnavailabilityReason => IsAvailable ? null : "Sample capability is unavailable.";
	public event Action? AvailabilityChanged;

	public ValueTask RefreshAvailabilityAsync(WorkspaceSessionInfo session, CancellationToken cancellationToken)
	{
		bool next = session.Capabilities.Contains("sample");
		if(next != IsAvailable)
		{
			IsAvailable = next;
			AvailabilityChanged?.Invoke();
		}
		return ValueTask.CompletedTask;
	}

	public ValueTask<IWorkspaceToolInstance> CreateInstanceAsync(WorkspaceToolInstanceContext context, CancellationToken cancellationToken) =>
		ValueTask.FromResult<IWorkspaceToolInstance>(new NotesInstance(context.InstanceId));

	public ValueTask DisposeAsync() => ValueTask.CompletedTask;
}

public static class TestTools
{
	public static WorkspaceToolDescriptor Descriptor(string id) => new()
	{
		ToolId = id,
		ApiVersion = new(1, 0),
		DisplayName = "Sample",
		StateSchemaVersion = 1,
		Content = new WorkspaceToolContentDescriptor.Razor(typeof(SampleComponent))
	};
}

public sealed class SampleComponent : IWorkspaceToolComponent;
