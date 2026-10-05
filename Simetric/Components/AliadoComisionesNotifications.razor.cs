namespace Simetric.Components;

public partial class AliadoComisionesNotifications
{
    private IReadOnlyList<string> avisos = Array.Empty<string>();
    private bool abierto, cargando;
    private string? error;

    protected override async Task OnInitializedAsync() => await CargarAsync();

    private async Task MostrarAsync()
    {
        abierto = !abierto;
        if (abierto) await CargarAsync();
    }

    private async Task CargarAsync()
    {
        cargando = true;
        error = null;
        try
        {
            var auth = await Auth.GetAuthenticationStateAsync();
            if (int.TryParse(auth.User.FindFirst("IdUsuario")?.Value, out var userId))
                avisos = await Portal.ObtenerAvisosComisionesAsync(userId);
        }
        catch (Exception ex)
        {
            Logger.LogError(ex, "No se pudieron consultar las notificaciones de comisiones.");
            error = "No se pudieron consultar los avisos. Abre nuevamente para reintentar.";
        }
        finally { cargando = false; }
    }
}
