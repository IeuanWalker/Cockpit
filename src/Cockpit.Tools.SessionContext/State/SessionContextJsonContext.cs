using System.Text.Json.Serialization;

namespace Cockpit.Tools.SessionContext.State;

[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase)]
[JsonSerializable(typeof(SessionContextState))]
public sealed partial class SessionContextJsonContext : JsonSerializerContext;
