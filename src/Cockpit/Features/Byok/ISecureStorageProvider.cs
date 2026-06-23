namespace Cockpit.Features.Byok;

/// <summary>
/// Thin abstraction over platform secure key-value storage.
/// Allows unit tests to substitute an in-memory implementation without requiring MAUI.
/// </summary>
public interface ISecureStorageProvider
{
	Task<string?> GetAsync(string key);
	Task SetAsync(string key, string value);
	bool Remove(string key);
}
