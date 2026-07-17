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

    public async Task<string?> GetCurrentServiceKeyAsync()
    {
        try
        {
            var value = await _jsRuntime.InvokeAsync<string?>("localStorage.getItem", StorageKey);
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

    public async Task SetCurrentServiceKeyAsync(string? serviceKey)
    {
        var normalized = Normalize(serviceKey);

        try
        {
            if (string.IsNullOrWhiteSpace(normalized))
            {
                await _jsRuntime.InvokeVoidAsync("localStorage.removeItem", StorageKey);
                return;
            }

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

    public Task ClearAsync() => SetCurrentServiceKeyAsync(null);

    private static string? Normalize(string? value) =>
        string.IsNullOrWhiteSpace(value)
            ? null
            : value.Trim().ToLowerInvariant();
}
