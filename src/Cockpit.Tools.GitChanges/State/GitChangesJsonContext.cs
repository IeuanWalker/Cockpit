using System.Text.Json.Serialization;

namespace Cockpit.Tools.GitChanges.State;

[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase)]
[JsonSerializable(typeof(GitChangesState))]
public sealed partial class GitChangesJsonContext : JsonSerializerContext;
