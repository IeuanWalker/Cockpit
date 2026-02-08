using Microsoft.JSInterop;

namespace CopilotUIWebAssembly.Services;

public class LocalStorageService
{
    readonly IJSRuntime _jsRuntime;

    public LocalStorageService(IJSRuntime jsRuntime)
    {
        _jsRuntime = jsRuntime;
    }

    public async Task<string?> GetItemAsync(string key)
    {
        try
        {
            return await _jsRuntime.InvokeAsync<string?>("localStorageHelper.getItem", key);
        }
        catch
        {
            return null;
        }
    }

    public async Task SetItemAsync(string key, string value)
    {
        try
        {
            await _jsRuntime.InvokeVoidAsync("localStorageHelper.setItem", key, value);
        }
        catch
        {
            // Handle error silently
        }
    }

    public async Task RemoveItemAsync(string key)
    {
        try
        {
            await _jsRuntime.InvokeVoidAsync("localStorageHelper.removeItem", key);
        }
        catch
        {
            // Handle error silently
        }
    }
}
