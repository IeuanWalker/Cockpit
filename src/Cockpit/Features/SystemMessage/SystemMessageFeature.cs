using Cockpit.Features.Sdk;
using GitHub.Copilot;
using Microsoft.Extensions.Logging;
using Microsoft.Maui.Storage;

namespace Cockpit.Features.SystemMessage;

sealed class SystemMessageFeature : ISystemMessageFeature
{
static readonly string _internalSessionsDir =
Path.Combine(FileSystem.Current.AppDataDirectory, "InternalSessions");

readonly CopilotClientFeature _clientFeature;
readonly ILogger<SystemMessageFeature> _logger;
readonly CancellationTokenSource _cts = new();

IReadOnlyDictionary<string, string> _defaults = new Dictionary<string, string>();
bool _defaultsCaptured;

public event Action? OnDefaultsLoaded;

public IReadOnlyDictionary<string, string> Defaults => _defaults;
public bool DefaultsLoaded { get; private set; }

public SystemMessageFeature(CopilotClientFeature clientFeature, ILogger<SystemMessageFeature> logger)
{
_clientFeature = clientFeature;
_logger = logger;

_clientFeature.OnConnectionStateChanged += OnConnectionStateChanged;
}

void OnConnectionStateChanged(ConnectionState state)
{
if (state == ConnectionState.Connected && !_defaultsCaptured)
{
_ = WarmDefaultsAsync();
}
}

async Task WarmDefaultsAsync()
{
_defaultsCaptured = true;
try
{
CopilotClient client = await _clientFeature.GetClientAsync(_cts.Token);
Dictionary<string, string> captured = new();

SessionConfig config = new()
{
ConfigDirectory = _internalSessionsDir,
SystemMessage = new SystemMessageConfig
{
Mode = SystemMessageMode.Customize,
Sections = new Dictionary<SystemMessageSection, SectionOverride>
{
[SystemMessageSection.Identity] = new() { Transform = CaptureAndReturn(captured, SystemMessageSection.Identity.Value) },
[SystemMessageSection.Tone] = new() { Transform = CaptureAndReturn(captured, SystemMessageSection.Tone.Value) },
[SystemMessageSection.ToolEfficiency] = new() { Transform = CaptureAndReturn(captured, SystemMessageSection.ToolEfficiency.Value) },
[SystemMessageSection.EnvironmentContext] = new() { Transform = CaptureAndReturn(captured, SystemMessageSection.EnvironmentContext.Value) },
[SystemMessageSection.CodeChangeRules] = new() { Transform = CaptureAndReturn(captured, SystemMessageSection.CodeChangeRules.Value) },
[SystemMessageSection.Guidelines] = new() { Transform = CaptureAndReturn(captured, SystemMessageSection.Guidelines.Value) },
[SystemMessageSection.Safety] = new() { Transform = CaptureAndReturn(captured, SystemMessageSection.Safety.Value) },
[SystemMessageSection.ToolInstructions] = new() { Transform = CaptureAndReturn(captured, SystemMessageSection.ToolInstructions.Value) },
[SystemMessageSection.CustomInstructions] = new() { Transform = CaptureAndReturn(captured, SystemMessageSection.CustomInstructions.Value) },
[SystemMessageSection.RuntimeInstructions] = new() { Transform = CaptureAndReturn(captured, SystemMessageSection.RuntimeInstructions.Value) },
[SystemMessageSection.LastInstructions] = new() { Transform = CaptureAndReturn(captured, SystemMessageSection.LastInstructions.Value) },
}
}
};

CopilotSession session = await client.CreateSessionAsync(config, _cts.Token);
await session.DisposeAsync();

_defaults = captured;
DefaultsLoaded = true;
OnDefaultsLoaded?.Invoke();

_logger.LogInformation("System message defaults captured for {Count} sections", captured.Count);
}
catch (OperationCanceledException)
{
_logger.LogDebug("System message defaults warm-up was cancelled");
_defaultsCaptured = false;
}
catch (Exception ex)
{
_logger.LogWarning(ex, "Failed to warm system message defaults");
_defaultsCaptured = false;
}
}

static Func<string, Task<string>> CaptureAndReturn(Dictionary<string, string> captured, string key)
=> content =>
{
captured[key] = content;
return Task.FromResult(content);
};

/// <summary>
/// Deletes the internal probe sessions directory left from previous runs.
/// Call once at startup before DI is resolved.
/// </summary>
internal static void CleanupInternalSessionsDirectory()
{
try
{
if (Directory.Exists(_internalSessionsDir))
{
Directory.Delete(_internalSessionsDir, recursive: true);
}
}
catch (Exception)
{
// best-effort; failures are non-fatal
}
}
}
