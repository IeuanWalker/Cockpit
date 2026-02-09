namespace Cockpit.Services;

public class LocalStorageService
{
    public Task<string?> GetItemAsync(string key)
    {
        try
        {
            return Task.FromResult(Preferences.Default.Get(key, (string?)null));
        }
        catch
        {
            return Task.FromResult<string?>(null);
        }
    }

    public Task SetItemAsync(string key, string value)
    {
        try
        {
            Preferences.Default.Set(key, value);
        }
        catch
        {
            // Handle error silently
        }
        return Task.CompletedTask;
    }

    public Task RemoveItemAsync(string key)
    {
        try
        {
            Preferences.Default.Remove(key);
        }
        catch
        {
            // Handle error silently
        }
        return Task.CompletedTask;
    }
}
