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
    private Dictionary<int, EContaxAccion> acciones = new(), accesoEfectivo = new();
    private List<Simetric.Models.Auditoria> historial = new();
    private static readonly EContaxAccion[] AccionesEditables =
        { EContaxAccion.Ver, EContaxAccion.Crear, EContaxAccion.Editar, EContaxAccion.Eliminar, EContaxAccion.Exportar };
    private int userId, sucursalId, perfilId, perfilAsignado, usuarioEditando, nuevoTitular;
    private string tab = "Mi cuenta", nombrePerfil = "", nombreSucursal = "", nombres = "", apellidos = "", email = "", clave = "";
    private string? error, mensaje, codigoProcesado;
    private bool cargando = true, ocupado, adminSucursal, permisosUnicos, confirmarAbandono, confirmarTransferencia, confirmarImpacto;
    private bool RolCompartido => perfilId > 0 && resumen?.Contexto.EsJefeEmpresa != true &&
        resumen?.Perfiles.FirstOrDefault(x => x.IdPerfil == perfilId)?.IdSucursal is null;
    private int UsuariosAfectados => resumen?.Usuarios.Count(x => x.IdPerfil == perfilId && x.MenusJson is null && !x.EsAdminSucursal &&
        x.IdUsuario != resumen.Empresa.IdTitular) ?? 0;
    private bool UsuarioEditable => usuarioEditando == 0 || (usuarioEditando != resumen?.Empresa.IdTitular &&
        (resumen?.Contexto.EsJefeEmpresa == true || resumen?.Usuarios.FirstOrDefault(x => x.IdUsuario == usuarioEditando)?.EsAdminSucursal != true));
    private IEnumerable<string> Tabs => resumen?.EsAdmin == true
        ? new[] { "Sucursales", "Roles", "Usuarios", "Invitaciones", "Historial", "Mi cuenta" } : new[] { "Mi cuenta" };

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
        try
        {
            resumen = await Seguridad.GetResumenAsync(userId);
            if (tab == "Historial" && resumen.EsAdmin) historial = await Seguridad.GetHistorialAsync(userId);
            if (usuarioEditando > 0) accesoEfectivo = await Seguridad.GetAccionesUsuarioAsync(usuarioEditando);
        }
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

    private async Task CambiarTab(string item)
    {
        tab = item; error = null; mensaje = null; seleccion.Clear();
        sucursalId = resumen?.Contexto.EsJefeEmpresa == true ? 0 : resumen?.Contexto.IdSucursal ?? 0;
        perfilId = perfilAsignado = usuarioEditando = 0; nombrePerfil = email = clave = "";
        adminSucursal = permisosUnicos = confirmarImpacto = false; acciones.Clear(); accesoEfectivo.Clear();
        if (item == "Historial") await EjecutarAsync(async () => historial = await Seguridad.GetHistorialAsync(userId), "Historial actualizado.");
    }
    private void ToggleMenu(int id, bool marcado)
    {
        confirmarImpacto = false;
        if (marcado) { seleccion.Add(id); acciones.TryAdd(id, EContaxAccion.Ver); }
        else { seleccion.Remove(id); acciones.Remove(id); }
    }
    private void ToggleAccion(int id, EContaxAccion accion, bool marcado)
    {
        confirmarImpacto = false;
        if (accion == EContaxAccion.Ver) { ToggleMenu(id, marcado); return; }
        acciones[id] = marcado ? acciones.GetValueOrDefault(id) | accion : acciones.GetValueOrDefault(id) & ~accion;
    }
    private void CargarAcciones(string? json) => acciones = seleccion.ToDictionary(id => id, id => EContaxSeguridadService.AccionesDe(json, id));
    private bool MenuDisponible(int id)
    {
        if (RolCompartido || !resumen!.Menus.Any(x => x.IdMenu == id && x.UrlMenu is not null)) return false;
        if (tab == "Sucursales" || sucursalId == 0) return true;
        var sucursal = resumen.Sucursales.FirstOrDefault(x => x.IdSucursal == sucursalId);
        return sucursal?.MenusJson is null || EContaxSeguridadService.LeerMenus(sucursal.MenusJson).Contains(id);
    }
    private IEnumerable<IGrouping<string, EContaxMenu>> GruposMenus => resumen!.CatalogoMenus
        .Where(x => x.UrlMenu is not null).GroupBy(x => NombreGrupo(x));
    private string NombreGrupo(EContaxMenu menu)
    {
        var visitados = new HashSet<int>();
        while (menu.IdPadre is > 0 && visitados.Add(menu.IdMenu))
        {
            var padre = resumen!.CatalogoMenus.FirstOrDefault(x => x.IdMenu == menu.IdPadre);
            if (padre is null) break;
            menu = padre;
        }
        return menu.NombreMenu ?? "General";
    }
    private void ToggleGrupo(IEnumerable<EContaxMenu> grupo, bool marcado)
    {
        foreach (var menu in grupo.Where(x => MenuDisponible(x.IdMenu))) ToggleMenu(menu.IdMenu, marcado);
    }
    private void SeleccionarSucursal(ChangeEventArgs args)
    {
        sucursalId = int.TryParse(args.Value?.ToString(), out var id) ? id : 0;
        var sucursal = resumen!.Sucursales.FirstOrDefault(x => x.IdSucursal == sucursalId);
        seleccion = sucursal?.MenusJson is null ? resumen.Menus.Select(x => x.IdMenu).ToHashSet() : EContaxSeguridadService.LeerMenus(sucursal.MenusJson);
    }
    private void NuevoPerfil() { perfilId = 0; nombrePerfil = ""; seleccion.Clear(); acciones.Clear(); confirmarImpacto = false;
        sucursalId = resumen!.Contexto.EsJefeEmpresa ? 0 : resumen.Contexto.IdSucursal ?? 0; }
    private void SeleccionarPerfil(ChangeEventArgs args)
    {
        perfilId = int.TryParse(args.Value?.ToString(), out var id) ? id : 0;
        var perfil = resumen!.Perfiles.FirstOrDefault(x => x.IdPerfil == perfilId);
        if (perfil is null) { NuevoPerfil(); return; }
        nombrePerfil = perfil.Nombre; sucursalId = perfil.IdSucursal ?? 0; seleccion = EContaxSeguridadService.LeerMenus(perfil.MenusJson);
        CargarAcciones(perfil.AccionesJson); confirmarImpacto = false;
    }
    private void NuevaCuenta() { usuarioEditando = perfilAsignado = 0; nombres = apellidos = email = clave = "";
        adminSucursal = permisosUnicos = false; seleccion.Clear(); acciones.Clear(); accesoEfectivo.Clear(); }
    private async Task SeleccionarUsuario(ChangeEventArgs args)
    {
        usuarioEditando = int.TryParse(args.Value?.ToString(), out var id) ? id : 0;
        var usuario = resumen!.Usuarios.FirstOrDefault(x => x.IdUsuario == usuarioEditando);
        if (usuario is null) { NuevaCuenta(); return; }
        sucursalId = usuario.IdSucursal ?? 0; perfilAsignado = usuario.IdPerfil ?? 0;
        adminSucursal = usuario.EsAdminSucursal; permisosUnicos = usuario.MenusJson is not null;
        seleccion = EContaxSeguridadService.LeerMenus(usuario.MenusJson ?? resumen.Perfiles.FirstOrDefault(x => x.IdPerfil == usuario.IdPerfil)?.MenusJson);
        CargarAcciones(usuario.MenusJson is not null ? usuario.AccionesJson : resumen.Perfiles.FirstOrDefault(x => x.IdPerfil == usuario.IdPerfil)?.AccionesJson);
        accesoEfectivo = await Seguridad.GetAccionesUsuarioAsync(usuarioEditando);
    }
    private string AccionesJson => JsonSerializer.Serialize(acciones.Where(x => seleccion.Contains(x.Key) &&
        resumen!.Menus.Any(m => m.IdMenu == x.Key && m.UrlMenu is not null)).ToDictionary(x => x.Key, x => x.Value));
    private void CambiarPerfilAsignado()
    {
        if (permisosUnicos) return;
        var perfil = resumen!.Perfiles.FirstOrDefault(x => x.IdPerfil == perfilAsignado);
        seleccion = EContaxSeguridadService.LeerMenus(perfil?.MenusJson);
        CargarAcciones(perfil?.AccionesJson);
    }
    private void CambiarSucursalAsignada()
    {
        if (!resumen!.Perfiles.Any(x => x.IdPerfil == perfilAsignado && (x.IdSucursal is null || x.IdSucursal == sucursalId)))
            perfilAsignado = 0;
        CambiarPerfilAsignado();
    }
    private Task GuardarPerfilAsync() => EjecutarAsync(async () =>
    {
        if (RolCompartido) throw new InvalidOperationException("Solo el titular puede editar un rol compartido.");
        if (perfilId > 0 && UsuariosAfectados > 0 && !confirmarImpacto)
            throw new InvalidOperationException("Confirma el impacto sobre los usuarios del rol antes de guardar.");
        await Seguridad.GuardarPerfilAsync(userId, perfilId, nombrePerfil, sucursalId > 0 ? sucursalId : null, seleccion, AccionesJson);
        confirmarImpacto = false;
    }, "Rol guardado.");
    private Task GuardarSucursalAsync() => EjecutarAsync(() => Seguridad.GuardarSucursalMenusAsync(userId, sucursalId, seleccion), "Permisos de sucursal guardados.");
    private Task CrearSucursalAsync() => EjecutarAsync(async () =>
    { await Organizacion.GuardarSucursalAsync(userId, new EContaxSucursalUpsertDto { Nombre = nombreSucursal }); nombreSucursal = ""; }, "Sucursal creada.");
    private Task GuardarUsuarioAsync() => EjecutarAsync(async () =>
    {
        if (usuarioEditando == 0)
        { await Seguridad.CrearCuentaAsync(userId, nombres, apellidos, email, clave, sucursalId, perfilAsignado, adminSucursal); clave = ""; }
        else await Seguridad.GuardarUsuarioAsync(userId, usuarioEditando, sucursalId, perfilAsignado, adminSucursal,
            permisosUnicos ? JsonSerializer.Serialize(seleccion) : null, permisosUnicos ? AccionesJson : null);
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
    private Task CambiarSuspensionAsync(bool suspendido) => EjecutarAsync(() => Seguridad.CambiarSuspensionAsync(userId, usuarioEditando, suspendido),
        suspendido ? "Acceso a E-Contax suspendido." : "Acceso a E-Contax reactivado.");
    private Task RestablecerRolAsync() => EjecutarAsync(async () =>
    {
        var usuario = resumen!.Usuarios.Single(x => x.IdUsuario == usuarioEditando);
        await Seguridad.GuardarUsuarioAsync(userId, usuario.IdUsuario, usuario.IdSucursal ?? 0, usuario.IdPerfil ?? 0, usuario.EsAdminSucursal, null);
        permisosUnicos = false;
        seleccion = EContaxSeguridadService.LeerMenus(resumen.Perfiles.FirstOrDefault(x => x.IdPerfil == usuario.IdPerfil)?.MenusJson);
        CargarAcciones(resumen.Perfiles.FirstOrDefault(x => x.IdPerfil == usuario.IdPerfil)?.AccionesJson);
    }, "Se restablecieron los permisos del rol.");
    private string MotivoAcceso(EContaxMenu menu)
    {
        var usuario = resumen!.Usuarios.Single(x => x.IdUsuario == usuarioEditando);
        if (usuario.Suspendido) return "Acceso suspendido";
        if (usuario.Usuario?.Estado != true) return "Cuenta desactivada";
        if (resumen.Empresa.MenusJson is not null && !EContaxSeguridadService.LeerMenus(resumen.Empresa.MenusJson).Contains(menu.IdMenu)) return "Limitado por empresa";
        if (usuario.IdUsuario != resumen.Empresa.IdTitular)
        {
            var sucursal = resumen.Sucursales.FirstOrDefault(x => x.IdSucursal == usuario.IdSucursal);
            if (sucursal is null || !sucursal.Estado) return "Sucursal no habilitada";
            if (sucursal.MenusJson is not null && !EContaxSeguridadService.LeerMenus(sucursal.MenusJson).Contains(menu.IdMenu)) return "Limitado por sucursal";
        }
        return usuario.IdUsuario == resumen.Empresa.IdTitular ? "Titular" : usuario.EsAdminSucursal ? "Administrador de sucursal" :
            usuario.MenusJson is not null ? "Permisos individuales" : "Rol asignado";
    }
    private string TextoCambio(string? json)
    {
        if (json is null) return "Sin asignación previa";
        using var documento = JsonDocument.Parse(json);
        return string.Join("; ", documento.RootElement.EnumerateObject().Select(x => x.Name switch
        {
            "Nombre" => $"Rol: {x.Value.GetString()}",
            "IdPerfil" => $"Rol: {resumen!.Perfiles.FirstOrDefault(p => p.IdPerfil == (x.Value.ValueKind == JsonValueKind.Null ? 0 : x.Value.GetInt32()))?.Nombre ?? x.Value.ToString()}",
            "IdUsuario" or "IdTitular" => $"{(x.Name == "IdTitular" ? "Titular" : "Usuario")}: {resumen!.Usuarios.FirstOrDefault(u => u.IdUsuario == (x.Value.ValueKind == JsonValueKind.Null ? 0 : x.Value.GetInt32()))?.Usuario?.NombreCompleto ?? x.Value.ToString()}",
            "IdSucursal" => $"Sucursal: {(x.Value.ValueKind == JsonValueKind.Null ? "Todas" : resumen!.Sucursales.FirstOrDefault(s => s.IdSucursal == x.Value.GetInt32())?.Nombre ?? x.Value.ToString())}",
            "EsAdminSucursal" => $"Administrador: {(x.Value.GetBoolean() ? "Sí" : "No")}",
            "Suspendido" => $"Suspendido: {(x.Value.GetBoolean() ? "Sí" : "No")}",
            "MenusJson" => x.Value.ValueKind == JsonValueKind.Null ? "Permisos heredados del rol" :
                "Menús: " + string.Join(", ", EContaxSeguridadService.LeerMenus(x.Value.GetString()).Select(id => resumen!.CatalogoMenus.FirstOrDefault(m => m.IdMenu == id)?.NombreMenu ?? $"Menú {id}")),
            "AccionesJson" => x.Value.ValueKind == JsonValueKind.Null ? "Acciones: configuración anterior" :
                "Acciones: " + string.Join(" / ", EContaxSeguridadService.LeerAcciones(x.Value.GetString()).Select(a =>
                    $"{resumen!.CatalogoMenus.FirstOrDefault(m => m.IdMenu == a.Key)?.NombreMenu ?? $"Menú {a.Key}"}: {a.Value}")),
            _ => x.Value.ToString()
        }));
    }
    private Task AbandonarAsync() => EjecutarAsync(async () =>
    { await Seguridad.AbandonarAsync(userId); resumen = null; Nav.NavigateTo("/portal-servicios", true); }, "Has abandonado la empresa.");
}
