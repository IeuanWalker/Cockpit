using System.Text.Json.Serialization;

namespace Cockpit.Tooling.Discovery;

[JsonSerializable(typeof(WorkspaceToolCatalogDocument))]
sealed partial class WorkspaceToolDiscoveryJsonContext : JsonSerializerContext;
