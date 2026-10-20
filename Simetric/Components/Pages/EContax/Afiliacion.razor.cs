using System.Security.Claims;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Authorization;
using Simetric.Auth;
using Simetric.Models.EContax;
using Simetric.Services;
using Simetric.Services.EContax;

namespace Simetric.Components.Pages.EContax;

public partial class Afiliacion
{
    [Inject] private AuthenticationStateProvider Auth { get; set; } = null!;
    [Inject] private UserService UserService { get; set; } = null!;
    [Inject] private EContaxSeguridadService Seguridad { get; set; } = null!;
    [Inject] private NavigationManager Nav { get; set; } = null!;

    private EContaxSeguridadResumen? resumen;
    private int userId;
    private int usuarioConfirmarDesvinculacion;
    private bool cargando = true, ocupado, confirmarAbandono;
    private string? error;

    private string SucursalActual => resumen?.Contexto.IdSucursal is int id
        ? resumen.Sucursales.FirstOrDefault(x => x.IdSucursal == id)?.Nombre ?? "Sucursal asignada"
        : "Todas las sucursales";

    private bool PuedeDesvincular(EContaxUsuarioContexto usuario) =>
        usuario.IdUsuario != userId &&
        usuario.IdUsuario != resumen?.Empresa.IdTitular &&
        (resumen?.Contexto.EsJefeEmpresa == true || !usuario.EsAdminSucursal);

    private string RolUsuario(EContaxUsuarioContexto usuario) => usuario.IdUsuario == resumen?.Empresa.IdTitular
        ? "Titular"
        : usuario.EsAdminSucursal ? "Administrador de sucursal" : "Usuario";

    protected override async Task OnInitializedAsync()
    {
        var user = (await Auth.GetAuthenticationStateAsync()).User;
        userId = int.TryParse(user.FindFirst("IdUsuario")?.Value ?? user.FindFirstValue(ClaimTypes.NameIdentifier), out var id)
            ? id
            : 0;

        if (userId <= 0)
        {
            Nav.NavigateTo("/login", forceLoad: true);
            return;
        }

        try
        {
            resumen = await Seguridad.GetResumenAsync(userId);
        }
        catch (InvalidOperationException ex)
        {
            error = ex.Message;
        }
        finally
        {
            cargando = false;
        }
    }

    private async Task AbandonarAsync()
    {
        if (!confirmarAbandono || ocupado)
            return;

        ocupado = true;
        error = null;
        try
        {
            await Seguridad.AbandonarAsync(userId);
            var usuarioActualizado = await UserService.GetUsuarioByIdAsync(userId);
            if (usuarioActualizado is not null && Auth is CustomAuthStateProvider authProvider)
                await authProvider.UpdateAuthenticationState(usuarioActualizado);
            Nav.NavigateTo("/portal-servicios", forceLoad: true);
        }
        catch (InvalidOperationException ex)
        {
            error = ex.Message;
            ocupado = false;
        }
    }

    private void SolicitarDesvinculacion(int idUsuario) => usuarioConfirmarDesvinculacion = idUsuario;

    private void CancelarDesvinculacion() => usuarioConfirmarDesvinculacion = 0;

    private async Task DesvincularUsuarioAsync(int idUsuario)
    {
        if (ocupado || !PuedeDesvincular(resumen!.Usuarios.Single(x => x.IdUsuario == idUsuario)))
            return;

        ocupado = true;
        error = null;
        try
        {
            await Seguridad.DesvincularUsuarioAsync(userId, idUsuario);
            resumen = await Seguridad.GetResumenAsync(userId);
            usuarioConfirmarDesvinculacion = 0;
        }
        catch (InvalidOperationException ex)
        {
            error = ex.Message;
        }
        finally
        {
            ocupado = false;
        }
    }
}
