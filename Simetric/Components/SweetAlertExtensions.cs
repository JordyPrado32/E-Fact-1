using Microsoft.JSInterop;

namespace Simetric.Components;

public static class SweetAlertExtensions
{
    public static async Task<bool> ConfirmDeleteAsync(this IJSRuntime js, string title, string text, string confirmButtonText = "Si, eliminar")
    {
        var result = await js.InvokeAsync<System.Text.Json.JsonElement>("Swal.fire", new
        {
            icon = "warning",
            title,
            text = string.IsNullOrWhiteSpace(text) ? null : text,
            showCancelButton = true,
            confirmButtonText,
            cancelButtonText = "Cancelar",
            reverseButtons = true,
            focusCancel = true,
            buttonsStyling = false,
            customClass = new
            {
                popup = "numerica-swal-popup",
                title = "numerica-swal-title",
                htmlContainer = "numerica-swal-body",
                actions = "numerica-swal-actions",
                confirmButton = "numerica-swal-confirm",
                cancelButton = "numerica-swal-cancel"
            }
        });

        return result.TryGetProperty("isConfirmed", out var confirmed) && confirmed.GetBoolean();
    }

    public static ValueTask ShowAlertAsync(this IJSRuntime js, string title, string text, string icon = "success")
    {
        return js.InvokeVoidAsync("Swal.fire", new
        {
            icon,
            title,
            text,
            confirmButtonText = "Aceptar"
        });
    }

    public static ValueTask ShowToastAsync(this IJSRuntime js, string title, string icon = "success")
    {
        return js.InvokeVoidAsync("Swal.fire", new
        {
            icon,
            title,
            timer = 1800,
            showConfirmButton = false
        });
    }
}
