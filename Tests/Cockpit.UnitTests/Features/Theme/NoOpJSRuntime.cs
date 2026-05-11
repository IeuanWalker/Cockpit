using Microsoft.JSInterop;

namespace Cockpit.UnitTests.Features.Theme;

/// <summary>
/// A no-op <see cref="IJSRuntime"/> for unit tests. All JS invocations silently return defaults.
/// </summary>
sealed class NoOpJSRuntime : IJSRuntime
{
	public ValueTask<TValue> InvokeAsync<TValue>(string identifier, object?[]? args)
		=> ValueTask.FromResult(default(TValue)!);

	public ValueTask<TValue> InvokeAsync<TValue>(string identifier, CancellationToken cancellationToken, object?[]? args)
		=> ValueTask.FromResult(default(TValue)!);
}
