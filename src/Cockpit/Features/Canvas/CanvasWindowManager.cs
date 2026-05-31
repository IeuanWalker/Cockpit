using System.Collections.Concurrent;
using System.Text.Json;
using Cockpit.Features.Theme;
using GitHub.Copilot;
using GitHub.Copilot.Rpc;
using Microsoft.Extensions.Logging;

namespace Cockpit.Features.Canvas;

/// <summary>
/// Manages canvas window lifecycle for all sessions.
/// Receives open/close/action requests from <see cref="SessionCanvasHandler"/> instances
/// and opens or closes native MAUI windows accordingly.
/// </summary>
public sealed class CanvasWindowManager
{
	readonly ThemeStateFeature _themeStateFeature;
	readonly ILogger<CanvasWindowManager> _logger;

	readonly ConcurrentDictionary<string, CanvasInstanceModel> _instances = new();

	/// <summary>
	/// FIFO queue of instance IDs for in-flight window opens.
	/// Enqueued before <see cref="Application.OpenWindow"/> is called; dequeued by the
	/// newly-created <see cref="CanvasRoot"/> component during its initialization.
	/// </summary>
	readonly ConcurrentQueue<string> _pendingInstanceIds = new();

	public event Action<string>? OnInstanceChanged;

	public CanvasWindowManager(ThemeStateFeature themeStateFeature, ILogger<CanvasWindowManager> logger)
	{
		_themeStateFeature = themeStateFeature;
		_logger = logger;
	}

	/// <summary>
	/// Opens a new canvas window for the given request and returns the open result.
	/// Called by <see cref="SessionCanvasHandler.OnOpenAsync"/>.
	/// </summary>
	public Task<CanvasProviderOpenResult> OpenAsync(CanvasProviderOpenRequest request, CancellationToken cancellationToken)
	{
		CanvasInstanceModel instance = new()
		{
			InstanceId = request.InstanceId,
			CanvasId = request.CanvasId,
			SessionId = request.SessionId,
			Title = TryExtractString(request.Input, "title") ?? request.CanvasId,
			Input = request.Input
		};

		_instances[instance.InstanceId] = instance;
		_pendingInstanceIds.Enqueue(instance.InstanceId);
		_logger.LogInformation(
			"Opening canvas window for instance {InstanceId} (canvasId={CanvasId}, session={SessionId})",
			instance.InstanceId, instance.CanvasId, instance.SessionId);

		MainThread.BeginInvokeOnMainThread(() =>
		{
			try
			{
				Window window = BuildWindow(instance, _themeStateFeature.IsLightTheme);
				instance.Window = window;
				Application.Current?.OpenWindow(window);
			}
			catch(Exception ex)
			{
				_logger.LogError(ex, "Failed to open canvas window for instance {InstanceId}", instance.InstanceId);
				_instances.TryRemove(instance.InstanceId, out _);
				// Also drain the stale ID from the pending queue so it doesn't block the next valid open.
				_pendingInstanceIds.TryDequeue(out _);
			}
		});

		CanvasProviderOpenResult result = new()
		{
			Title = instance.Title
		};
		return Task.FromResult(result);
	}

	/// <summary>
	/// Closes the canvas window for the given instance. Called by <see cref="SessionCanvasHandler.OnCloseAsync"/>.
	/// </summary>
	public Task CloseAsync(string instanceId, CancellationToken cancellationToken)
	{
		if(!_instances.TryRemove(instanceId, out CanvasInstanceModel? instance))
		{
			return Task.CompletedTask;
		}

		_logger.LogInformation("Closing canvas window for instance {InstanceId}", instanceId);

		if(instance.Window is not null)
		{
			MainThread.BeginInvokeOnMainThread(() =>
			{
				try
				{
					Application.Current?.CloseWindow(instance.Window);
				}
				catch(Exception ex)
				{
					_logger.LogWarning(ex, "Failed to close canvas window for instance {InstanceId}", instanceId);
				}
			});
		}

		return Task.CompletedTask;
	}

	/// <summary>
	/// Routes an action invocation to the registered Blazor component callback.
	/// Called by <see cref="SessionCanvasHandler.OnActionAsync"/>.
	/// </summary>
	public async Task<object?> InvokeActionAsync(CanvasProviderInvokeActionRequest request, CancellationToken cancellationToken)
	{
		if(!_instances.TryGetValue(request.InstanceId, out CanvasInstanceModel? instance))
		{
			_logger.LogWarning("Action invoked on unknown canvas instance {InstanceId}", request.InstanceId);
			throw CanvasException.NoHandler();
		}

		if(instance.ActionCallback is null)
		{
			_logger.LogWarning("No action callback registered for canvas instance {InstanceId}", request.InstanceId);
			throw CanvasException.NoHandler();
		}

		return await instance.ActionCallback(request.ActionName, request.Input, cancellationToken);
	}

	/// <summary>
	/// Called by a newly-initialised <see cref="CanvasRoot"/> component to claim the instance
	/// ID that was enqueued immediately before its window was opened.
	/// </summary>
	public string? ClaimPendingInstanceId()
		=> _pendingInstanceIds.TryDequeue(out string? id) ? id : null;

	/// <summary>Looks up a canvas instance by ID, for use by Blazor canvas components.</summary>
	public CanvasInstanceModel? GetInstance(string instanceId)
		=> _instances.TryGetValue(instanceId, out CanvasInstanceModel? instance) ? instance : null;

	/// <summary>
	/// Closes all canvas windows belonging to the given session.
	/// Called during session eviction or deletion.
	/// </summary>
	public async Task CloseAllForSessionAsync(string sessionId, CancellationToken cancellationToken = default)
	{
		IEnumerable<string> instanceIds = _instances.Values
			.Where(i => i.SessionId == sessionId)
			.Select(i => i.InstanceId)
			.ToList();

		foreach(string instanceId in instanceIds)
		{
			await CloseAsync(instanceId, cancellationToken);
		}
	}

	/// <summary>
	/// Notifies Blazor components subscribed to a canvas instance that its state has changed.
	/// </summary>
	public void NotifyInstanceChanged(string instanceId) => OnInstanceChanged?.Invoke(instanceId);

	static string? TryExtractString(JsonElement? element, string propertyName)
	{
		if(element is null || element.Value.ValueKind != JsonValueKind.Object)
		{
			return null;
		}

		if(element.Value.TryGetProperty(propertyName, out JsonElement prop)
			&& prop.ValueKind == JsonValueKind.String)
		{
			return prop.GetString();
		}

		return null;
	}

	Window BuildWindow(CanvasInstanceModel instance, bool isLightTheme){
		Color bg = isLightTheme ? Color.FromArgb("#F8F8F8") : Color.FromArgb("#181818");
		Color fg = isLightTheme ? Color.FromArgb("#3B3B3B") : Color.FromArgb("#CCCCCC");
		string title = instance.Title ?? "Canvas";

		return new Window(new CanvasPage(instance.InstanceId, instance.SplashFeature, this))
		{
			Title = title,
			Width = 900,
			Height = 600,
			TitleBar = new TitleBar
			{
				BackgroundColor = bg,
				ForegroundColor = fg,
				HeightRequest = 48,
				LeadingContent = new HorizontalStackLayout
				{
					VerticalOptions = LayoutOptions.Center,
					Spacing = 8,
					Margin = new Thickness(10, 0),
					Children =
					{
						new Image
						{
							HeightRequest = 26,
							WidthRequest = 19,
							Source = "logo.png",
							VerticalOptions = LayoutOptions.Center,
						},
						new Label
						{
							Text = title,
							TextColor = fg,
							FontSize = 13,
							VerticalOptions = LayoutOptions.Center,
						}
					}
				}
			}
		};
	}
}
