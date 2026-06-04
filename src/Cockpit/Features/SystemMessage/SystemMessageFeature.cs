using Cockpit.Features.Models;
using Cockpit.Features.Sdk;
using GitHub.Copilot;
using Microsoft.Extensions.Logging;

namespace Cockpit.Features.SystemMessage;

sealed class SystemMessageFeature : ISystemMessageFeature
{
	static readonly string internalSessionsDir = Path.Combine(FileSystem.Current.AppDataDirectory, "InternalSessions");

	readonly CopilotClientFeature _clientFeature;
	readonly IModelFeature _modelFeature;
	readonly ILogger<SystemMessageFeature> _logger;
	readonly CancellationTokenSource _cts = new();

	IReadOnlyDictionary<string, string> _defaults = new Dictionary<string, string>();
	bool _defaultsCaptured;

	public event Action? OnDefaultsLoaded;

	public IReadOnlyDictionary<string, string> Defaults => _defaults;
	public bool DefaultsLoaded { get; private set; }

	public SystemMessageFeature(CopilotClientFeature clientFeature, IModelFeature modelFeature, ILogger<SystemMessageFeature> logger)
	{
		_clientFeature = clientFeature;
		_modelFeature = modelFeature;
		_logger = logger;

		_clientFeature.OnConnectionStateChanged += OnConnectionStateChanged;

		// Client may already be connected before this singleton is first resolved
		if(_clientFeature.State == ConnectionState.Connected && !_defaultsCaptured)
		{
			_ = WarmDefaultsAsync();
		}
	}

	void OnConnectionStateChanged(ConnectionState state)
	{
		if(state == ConnectionState.Connected && !_defaultsCaptured)
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

			// Use the cheapest available model — the probe session only needs to trigger
			// systemMessage.transform, so model quality is irrelevant.
			IReadOnlyList<ModelInfo> models = await _modelFeature.GetModels(_cts.Token);
			string? cheapestModel = models
				.Where(m => m.Billing?.Multiplier.HasValue == true)
				.OrderBy(m => m.Billing!.Multiplier!.Value)
				.FirstOrDefault()?.Id
				?? models.FirstOrDefault()?.Id;

			Dictionary<string, string> captured = [];

			// The CLI calls systemMessage.transform after CreateSessionAsync returns,
			// as a separate RPC triggered by the first message. Signal this TCS on the
			// first callback so we know all sections have arrived (they come in one batch).
			TaskCompletionSource transformReceived = new(TaskCreationOptions.RunContinuationsAsynchronously);

			SessionConfig config = new()
			{
				ConfigDirectory = internalSessionsDir,
				Model = cheapestModel,
				SystemMessage = new SystemMessageConfig
				{
					Mode = SystemMessageMode.Customize,
					Sections = new Dictionary<SystemMessageSection, SectionOverride>
					{
						[SystemMessageSection.Identity] = new() { Transform = CaptureAndReturn(captured, SystemMessageSection.Identity.Value, transformReceived) },
						[SystemMessageSection.Tone] = new() { Transform = CaptureAndReturn(captured, SystemMessageSection.Tone.Value, transformReceived) },
						[SystemMessageSection.ToolEfficiency] = new() { Transform = CaptureAndReturn(captured, SystemMessageSection.ToolEfficiency.Value, transformReceived) },
						[SystemMessageSection.EnvironmentContext] = new() { Transform = CaptureAndReturn(captured, SystemMessageSection.EnvironmentContext.Value, transformReceived) },
						[SystemMessageSection.CodeChangeRules] = new() { Transform = CaptureAndReturn(captured, SystemMessageSection.CodeChangeRules.Value, transformReceived) },
						[SystemMessageSection.Guidelines] = new() { Transform = CaptureAndReturn(captured, SystemMessageSection.Guidelines.Value, transformReceived) },
						[SystemMessageSection.Safety] = new() { Transform = CaptureAndReturn(captured, SystemMessageSection.Safety.Value, transformReceived) },
						[SystemMessageSection.ToolInstructions] = new() { Transform = CaptureAndReturn(captured, SystemMessageSection.ToolInstructions.Value, transformReceived) },
						[SystemMessageSection.CustomInstructions] = new() { Transform = CaptureAndReturn(captured, SystemMessageSection.CustomInstructions.Value, transformReceived) },
						[SystemMessageSection.RuntimeInstructions] = new() { Transform = CaptureAndReturn(captured, SystemMessageSection.RuntimeInstructions.Value, transformReceived) },
						[SystemMessageSection.LastInstructions] = new() { Transform = CaptureAndReturn(captured, SystemMessageSection.LastInstructions.Value, transformReceived) },
					}
				}
			};

			await using CopilotSession session = await client.CreateSessionAsync(config, _cts.Token);

			// The CLI only calls systemMessage.transform when processing the first message,
			// not at session creation. Send a minimal prompt to trigger the callbacks.
			await session.SendAsync(new MessageOptions { Prompt = "." }, _cts.Token);

			// Wait for the CLI to call systemMessage.transform (all sections arrive in one batch).
			try
			{
				using CancellationTokenSource timeout = new(TimeSpan.FromSeconds(30));
				using CancellationTokenSource timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(_cts.Token, timeout.Token);
				await transformReceived.Task.WaitAsync(timeoutCts.Token);
			}
			catch(OperationCanceledException)
			{
				_logger.LogWarning("Timed out waiting for system message transform callbacks; captured {Count} sections", captured.Count);
			}

			_defaults = captured;
			DefaultsLoaded = true;
			OnDefaultsLoaded?.Invoke();

			_logger.LogInformation("System message defaults captured for {Count} sections", captured.Count);
		}
		catch(OperationCanceledException)
		{
			_logger.LogDebug("System message defaults warm-up was cancelled");
			_defaultsCaptured = false;
		}
		catch(Exception ex)
		{
			_logger.LogWarning(ex, "Failed to warm system message defaults");
			_defaultsCaptured = false;
		}
	}

	static Func<string, Task<string>> CaptureAndReturn(Dictionary<string, string> captured, string key, TaskCompletionSource transformReceived)
		=> content =>
		{
			captured[key] = content;
			transformReceived.TrySetResult();
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
			if(Directory.Exists(internalSessionsDir))
			{
				Directory.Delete(internalSessionsDir, recursive: true);
			}
		}
		catch(Exception)
		{
			// best-effort; failures are non-fatal
		}
	}
}

