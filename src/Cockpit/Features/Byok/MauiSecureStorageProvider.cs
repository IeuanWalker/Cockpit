namespace Cockpit.Features.Byok;

/// <summary>
/// Production implementation of <see cref="ISecureStorageProvider"/> that delegates to MAUI's <see cref="SecureStorage.Default"/>.
/// </summary>
sealed class MauiSecureStorageProvider : ISecureStorageProvider
{
	public Task<string?> GetAsync(string key) => SecureStorage.Default.GetAsync(key);
	public Task SetAsync(string key, string value) => SecureStorage.Default.SetAsync(key, value);
	public bool Remove(string key) => SecureStorage.Default.Remove(key);
}
