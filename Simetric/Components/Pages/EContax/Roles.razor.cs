using System.Security.Claims;
using System.Text.Json;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Authorization;
using Simetric.DTOs.EContax;
using Simetric.Models.EContax;
using Simetric.Services;
using Simetric.Services.EContax;

namespace Simetric.Components.Pages.EContax;

public partial class Roles
{
    [Inject] private AuthenticationStateProvider Auth { get; set; } = null!;
    [Inject] private EContaxSeguridadService Seguridad { get; set; } = null!;
    [Inject] private EContaxOrganizacionService Organizacion { get; set; } = null!;
    [Inject] private NavigationManager Nav { get; set; } = null!;
    [Inject] private IEmailService Correo { get; set; } = null!;
    [Inject] private MenuStateService MenuState { get; set; } = null!;
    [Parameter, SupplyParameterFromQuery(Name = "codigo")] public string? Codigo { get; set; }
    private EContaxSeguridadResumen? resumen;
    private HashSet<int> seleccion = new();
    private int userId, sucursalId, perfilId, perfilAsignado, usuarioEditando, nuevoTitular;
    private string tab = "Mi cuenta", nombrePerfil = "", nombreSucursal = "", nombres = "", apellidos = "", email = "", clave = "";
    private string? error, mensaje, codigoProcesado;
    private bool cargando = true, ocupado, adminSucursal, permisosUnicos, confirmarAbandono, confirmarTransferencia;
    private IEnumerable<string> Tabs => resumen?.EsAdmin == true
        ? new[] { "Sucursales", "Roles", "Usuarios", "Invitaciones", "Mi cuenta" } : new[] { "Mi cuenta" };

    protected override async Task OnParametersSetAsync()
    {
        var user = (await Auth.GetAuthenticationStateAsync()).User;
        if (user.Identity?.IsAuthenticated != true)
        {
            var vuelta = "/invitaciones/e-contax?codigo=" + Uri.EscapeDataString(Codigo ?? "");
            Nav.NavigateTo("/login?ReturnUrl=" + Uri.EscapeDataString(Codigo is null ? "/e-contax/administracion/roles" : vuelta), true);
            return;
        }
        userId = int.TryParse(user.FindFirst("IdUsuario")?.Value ?? user.FindFirstValue(ClaimTypes.NameIdentifier), out var id) ? id : 0;
        if (Codigo is not null && Codigo != codigoProcesado)
        {
            cargando = false;
            return;
        }
        else await CargarAsync();
    }

    private Task AceptarInvitacionAsync() => EjecutarAsync(async () =>
    {
        await Seguridad.AceptarAsync(userId, Codigo!);
        codigoProcesado = Codigo;
    }, "Invitación aceptada. Ya estás vinculado a la empresa.");

    private async Task CargarAsync()
    {
        cargando = true;
        try { resumen = await Seguridad.GetResumenAsync(userId); }
        catch (InvalidOperationException ex) { resumen = null; error = ex.Message; }
        finally { cargando = false; }
    }

    private async Task EjecutarAsync(Func<Task> accion, string texto)
    {
        if (ocupado) return;
        ocupado = true; error = null; mensaje = null;
        try { await accion(); mensaje = texto; MenuState.NotifyMenuChanged(); await CargarAsync(); }
        catch (Exception ex) when (ex is InvalidOperationException or ArgumentException or FormatException)
        { error = ex.Message; }
        finally { ocupado = false; cargando = false; }
    }

    private void CambiarTab(string item)
    {
        tab = item; error = null; mensaje = null; seleccion.Clear();
        sucursalId = resumen?.Contexto.EsJefeEmpresa == true ? 0 : resumen?.Contexto.IdSucursal ?? 0;
        perfilId = perfilAsignado = usuarioEditando = 0; nombrePerfil = email = clave = "";
        adminSucursal = permisosUnicos = false;
    }
    private void ToggleMenu(int id, bool marcado) { if (marcado) seleccion.Add(id); else seleccion.Remove(id); }
    private void SeleccionarSucursal(ChangeEventArgs args)
    {
        sucursalId = int.TryParse(args.Value?.ToString(), out var id) ? id : 0;
        var sucursal = resumen!.Sucursales.FirstOrDefault(x => x.IdSucursal == sucursalId);
        seleccion = sucursal?.MenusJson is null ? resumen.Menus.Select(x => x.IdMenu).ToHashSet() : EContaxSeguridadService.LeerMenus(sucursal.MenusJson);
    }
    private void NuevoPerfil() { perfilId = 0; nombrePerfil = ""; seleccion.Clear(); }
    private void SeleccionarPerfil(ChangeEventArgs args)
    {
        perfilId = int.TryParse(args.Value?.ToString(), out var id) ? id : 0;
        var perfil = resumen!.Perfiles.FirstOrDefault(x => x.IdPerfil == perfilId);
        if (perfil is null) { NuevoPerfil(); return; }
        nombrePerfil = perfil.Nombre; sucursalId = perfil.IdSucursal ?? 0; seleccion = EContaxSeguridadService.LeerMenus(perfil.MenusJson);
    }
    private void NuevaCuenta() { usuarioEditando = 0; nombres = apellidos = email = clave = ""; adminSucursal = permisosUnicos = false; seleccion.Clear(); }
    private void SeleccionarUsuario(ChangeEventArgs args)
    {
        usuarioEditando = int.TryParse(args.Value?.ToString(), out var id) ? id : 0;
        var usuario = resumen!.Usuarios.FirstOrDefault(x => x.IdUsuario == usuarioEditando);
        if (usuario is null) { NuevaCuenta(); return; }
        sucursalId = usuario.IdSucursal ?? 0; perfilAsignado = usuario.IdPerfil ?? 0;
        adminSucursal = usuario.EsAdminSucursal; permisosUnicos = usuario.MenusJson is not null;
        seleccion = EContaxSeguridadService.LeerMenus(usuario.MenusJson ?? resumen.Perfiles.FirstOrDefault(x => x.IdPerfil == usuario.IdPerfil)?.MenusJson);
    }
    private Task GuardarPerfilAsync() => EjecutarAsync(() => Seguridad.GuardarPerfilAsync(userId, perfilId, nombrePerfil, sucursalId > 0 ? sucursalId : null, seleccion), "Rol guardado.");
    private Task GuardarSucursalAsync() => EjecutarAsync(() => Seguridad.GuardarSucursalMenusAsync(userId, sucursalId, seleccion), "Permisos de sucursal guardados.");
    private Task CrearSucursalAsync() => EjecutarAsync(async () =>
    { await Organizacion.GuardarSucursalAsync(userId, new EContaxSucursalUpsertDto { Nombre = nombreSucursal }); nombreSucursal = ""; }, "Sucursal creada.");
    private Task GuardarUsuarioAsync() => EjecutarAsync(async () =>
    {
        if (usuarioEditando == 0)
        { await Seguridad.CrearCuentaAsync(userId, nombres, apellidos, email, clave, sucursalId, perfilAsignado, adminSucursal); clave = ""; }
        else await Seguridad.GuardarUsuarioAsync(userId, usuarioEditando, sucursalId, perfilAsignado, adminSucursal,
            permisosUnicos ? JsonSerializer.Serialize(seleccion) : null);
    }, "Cuenta y asignación guardadas.");
    private Task InvitarAsync() => EjecutarAsync(async () =>
    {
        var token = await Seguridad.InvitarAsync(userId, email, sucursalId, perfilAsignado, adminSucursal);
        try { await Correo.EnviarInvitacionEContaxAsync(email, resumen!.Empresa.Nombre, new Uri(new Uri(Nav.BaseUri), "/invitaciones/e-contax?codigo=" + token).AbsoluteUri); }
        catch (Exception ex)
        {
            await CargarAsync();
            var pendiente = resumen?.Invitaciones.FirstOrDefault(x => string.Equals(x.Email, email.Trim(), StringComparison.OrdinalIgnoreCase));
            if (pendiente is not null) await Seguridad.CancelarInvitacionAsync(userId, pendiente.IdInvitacion);
            throw new InvalidOperationException("No se pudo enviar el correo. La invitación se canceló; puedes reintentar.", ex);
        }
    }, "Invitación enviada. Vence en 48 horas.");
    private Task CancelarAsync(int id) => EjecutarAsync(() => Seguridad.CancelarInvitacionAsync(userId, id), "Invitación cancelada.");
    private Task TransferirAsync() => EjecutarAsync(() => Seguridad.TransferirAsync(userId, nuevoTitular), "Titularidad transferida. Puedes abandonar la empresa.");
    private Task AbandonarAsync() => EjecutarAsync(async () =>
    { await Seguridad.AbandonarAsync(userId); resumen = null; Nav.NavigateTo("/portal-servicios", true); }, "Has abandonado la empresa.");
}
