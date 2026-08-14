using Microsoft.JSInterop;

namespace Simetric.Services;

public sealed class SelectedAppServiceStateService
{
    private const string StorageKey = "numerica:current-service";
    private readonly IJSRuntime _jsRuntime;

    public SelectedAppServiceStateService(IJSRuntime jsRuntime)
    {
        _jsRuntime = jsRuntime;
    }

    public async Task<string?> GetCurrentServiceKeyAsync(int? userId = null)
    {
        try
        {
            var value = await _jsRuntime.InvokeAsync<string?>("localStorage.getItem", GetStorageKey(userId));
            return Normalize(value);
        }
        catch (JSDisconnectedException)
        {
            return null;
        }
        catch (TaskCanceledException)
        {
            return null;
        }
        catch (InvalidOperationException)
        {
            return null;
        }
    }

    public async Task SetCurrentServiceKeyAsync(string? serviceKey, int? userId = null)
    {
        var normalized = Normalize(serviceKey);

        try
        {
            if (string.IsNullOrWhiteSpace(normalized))
            {
                await _jsRuntime.InvokeVoidAsync("localStorage.removeItem", GetStorageKey(userId));
                if (userId is > 0)
                    await _jsRuntime.InvokeVoidAsync("localStorage.removeItem", StorageKey);
                return;
            }

            await _jsRuntime.InvokeVoidAsync("localStorage.setItem", GetStorageKey(userId), normalized);
            if (userId is > 0)
                await _jsRuntime.InvokeVoidAsync("localStorage.setItem", StorageKey, normalized);
        }
        catch (JSDisconnectedException)
        {
            // El circuito puede cerrarse en navegacion forzada; no interrumpimos el flujo.
        }
        catch (TaskCanceledException)
        {
            // En publicacion puede cancelarse el acceso a JS durante un recambio de circuito.
        }
        catch (InvalidOperationException)
        {
            // Evita romper la navegacion cuando JS todavia no esta disponible.
        }
    }

    public Task ClearAsync(int? userId = null) => SetCurrentServiceKeyAsync(null, userId);

    private static string GetStorageKey(int? userId) =>
        userId is > 0 ? $"{StorageKey}:{userId.Value}" : StorageKey;

    private static string? Normalize(string? value) =>
        string.IsNullOrWhiteSpace(value)
            ? null
            : value.Trim().ToLowerInvariant();
}
