using Microsoft.JSInterop;

namespace Simetric.Services;

public sealed class SweetAlertService
{
    private readonly IJSRuntime _js;

    public SweetAlertService(IJSRuntime js)
    {
        _js = js;
    }

    public ValueTask ShowAsync(
        string title,
        string text,
        string icon = "info",
        string confirmButtonText = "Entendido")
        => InvokeSafeVoidAsync("numericaUi.showAlert", new
        {
            title,
            text,
            icon,
            confirmButtonText
        });

    public ValueTask ShowSuccessAsync(string text, string title = "Proceso completado")
        => ShowAsync(title, text, "success");

    public ValueTask ShowErrorAsync(string text, string title = "Revisa la informacion")
        => ShowAsync(title, text, "error");

    public ValueTask ShowInfoAsync(string text, string title = "Informacion")
        => ShowAsync(title, text, "info");

    public ValueTask ShowWarningAsync(string text, string title = "Atencion")
        => ShowAsync(title, text, "warning");

    public async ValueTask ShowLoadingAsync(string title, string text)
    {
        await InvokeSafeVoidAsync("Swal.fire", new
        {
            title,
            text,
            allowOutsideClick = false,
            allowEscapeKey = false,
            showConfirmButton = false
        });

        await InvokeSafeVoidAsync("Swal.showLoading");
    }

    public ValueTask CloseAsync()
        => InvokeSafeVoidAsync("Swal.close");

    private async ValueTask InvokeSafeVoidAsync(string identifier, object? args = null)
    {
        try
        {
            if (args is null)
                await _js.InvokeVoidAsync(identifier);
            else
                await _js.InvokeVoidAsync(identifier, args);
        }
        catch (TaskCanceledException)
        {
        }
        catch (OperationCanceledException)
        {
        }
        catch (JSDisconnectedException)
        {
        }
        catch (InvalidOperationException ex) when (
            ex.Message.Contains("JavaScript interop calls cannot be issued", StringComparison.OrdinalIgnoreCase) ||
            ex.Message.Contains("disconnected", StringComparison.OrdinalIgnoreCase))
        {
        }
    }
}
