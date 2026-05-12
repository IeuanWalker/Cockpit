namespace Cockpit.Features.AppSettings;

/// <summary>
/// Thin abstraction over platform key-value preference storage.
/// Allows unit tests to substitute an in-memory implementation without requiring MAUI.
/// </summary>
public interface IPreferencesStorage
{
	T Get<T>(string key, T defaultValue);
	void Set<T>(string key, T value);
}
