using System.Security.Cryptography;
using System.Text;
using Simetric.Services;

namespace Simetric.Components;

public partial class AliadoComisionesNotifications
{
    private IReadOnlyList<string> avisos = Array.Empty<string>();
    private IReadOnlyList<AliadoRenovacionDto> renovaciones = Array.Empty<AliadoRenovacionDto>();
    private HashSet<string> ocultas = new(StringComparer.Ordinal);
    private int userId;
    private bool abierto, cargando, cargado;
    private string? error;

    private IEnumerable<string> ComisionesVisibles => avisos.Distinct().Where(x => !ocultas.Contains(GetComisionKey(x)));
    private IEnumerable<AliadoRenovacionDto> RenovacionesVisibles => renovaciones.Where(x => !ocultas.Contains(GetRenovacionKey(x)));
    private int TotalAvisos => ComisionesVisibles.Count() + RenovacionesVisibles.Count();

    protected override async Task OnAfterRenderAsync(bool firstRender)
    {
        if (firstRender)
        {
            await CargarAsync();
            await InvokeAsync(StateHasChanged);
        }
    }

    private async Task MostrarAsync()
    {
        abierto = !abierto;
        if (abierto && !cargado) await CargarAsync();
    }

    private async Task CargarAsync()
    {
        cargando = true;
        error = null;
        try
        {
            var auth = await Auth.GetAuthenticationStateAsync();
            if (int.TryParse(auth.User.FindFirst("IdUsuario")?.Value, out userId))
            {
                var avisosTask = Portal.ObtenerAvisosComisionesAsync(userId);
                var renovacionesTask = Portal.ObtenerRenovacionesAsync(userId);
                var ocultasTask = NotificacionDescartadaService.ObtenerIdsAsync(userId);

                await Task.WhenAll(avisosTask, renovacionesTask, ocultasTask);

                avisos = await avisosTask;
                renovaciones = (await renovacionesTask)
                    .Where(x => x.NivelAlerta is "danger" or "warning")
                    .Take(10)
                    .ToArray();
                ocultas = await ocultasTask;
                cargado = true;
            }
        }
        catch (Exception ex)
        {
            Logger.LogError(ex, "No se pudieron consultar las notificaciones de comisiones.");
            error = "No se pudieron consultar los avisos. Abre nuevamente para reintentar.";
        }
        finally { cargando = false; }
    }

    private async Task BorrarComisionAsync(string aviso)
    {
        ocultas.Add(GetComisionKey(aviso));
        await PersistirAsync();
    }

    private async Task BorrarRenovacionAsync(AliadoRenovacionDto renovacion)
    {
        ocultas.Add(GetRenovacionKey(renovacion));
        await PersistirAsync();
    }

    private async Task BorrarTodoAsync()
    {
        foreach (var aviso in ComisionesVisibles.ToList()) ocultas.Add(GetComisionKey(aviso));
        foreach (var renovacion in RenovacionesVisibles.ToList()) ocultas.Add(GetRenovacionKey(renovacion));
        await PersistirAsync();
    }

    private Task PersistirAsync() => NotificacionDescartadaService.DescartarAsync(userId, ocultas);

    private void IrARenovaciones()
    {
        abierto = false;
        Navigation.NavigateTo("/aliados/renovaciones");
    }

    private static string GetComisionKey(string aviso)
    {
        var hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(aviso)));
        return $"aliado-comision:{hash[..24]}";
    }

    private static string GetRenovacionKey(AliadoRenovacionDto renovacion) =>
        $"aliado-renovacion:{renovacion.IdFactura}:{renovacion.Servicio}:{renovacion.FechaVencimiento:yyyyMMdd}";
}
