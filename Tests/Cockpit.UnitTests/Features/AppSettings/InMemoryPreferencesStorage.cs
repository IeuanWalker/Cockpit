using Cockpit.Features.AppSettings;

namespace Cockpit.UnitTests.Features.AppSettings;

/// <summary>
/// In-memory implementation of <see cref="IPreferencesStorage"/> for unit tests.
/// Thread-safe enough for sequential test usage.
/// </summary>
sealed class InMemoryPreferencesStorage : IPreferencesStorage
{
	readonly Dictionary<string, object?> _store = [];

	public T Get<T>(string key, T defaultValue)
		=> _store.TryGetValue(key, out object? value) ? (T)value! : defaultValue;

	public void Set<T>(string key, T value) => _store[key] = value;
}
