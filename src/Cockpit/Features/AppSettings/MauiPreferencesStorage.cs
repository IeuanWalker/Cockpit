namespace Cockpit.Features.AppSettings;

/// <summary>
/// Production implementation of <see cref="IPreferencesStorage"/> that delegates to MAUI's <see cref="Preferences.Default"/>.
/// </summary>
sealed class MauiPreferencesStorage : IPreferencesStorage
{
	public T Get<T>(string key, T defaultValue) => Preferences.Default.Get(key, defaultValue);
	public void Set<T>(string key, T value) => Preferences.Default.Set(key, value);
}
