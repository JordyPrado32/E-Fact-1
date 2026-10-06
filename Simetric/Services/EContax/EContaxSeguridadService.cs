using System.Data;
using System.Net.Mail;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Simetric.Components.Helpers;
using Simetric.Data;
using Simetric.Models;
using Simetric.Models.EContax;

namespace Simetric.Services.EContax;

public sealed class EContaxSeguridadService(IDbContextFactory<AppDbContext> factory, EContaxTenantService tenant)
{
    public static HashSet<int> LeerMenus(string? json) => json is null
        ? new() : JsonSerializer.Deserialize<HashSet<int>>(json) ?? new();

    private static string Serializar(IEnumerable<int> menus) => JsonSerializer.Serialize(menus.Distinct().Order());

    public static Dictionary<int, EContaxAccion> LeerAcciones(string? json) => EContaxPermisos.LeerAcciones(json);

    public static EContaxAccion AccionesDe(string? json, int menuId) => EContaxPermisos.AccionesDe(json, menuId);

    public async Task<List<EContaxMenu>> GetMenusAsync(int userId)
    {
        await using var db = await factory.CreateDbContextAsync();
        var miembro = await db.EContaxUsuariosContexto.AsNoTracking().FirstOrDefaultAsync(x => x.IdUsuario == userId);
        if (miembro is null || !miembro.Estado || miembro.Suspendido ||
            !await db.Usuarios.AnyAsync(x => x.IdUsuario == userId && x.Estado == true)) return new();
        var empresa = await db.EContaxEmpresas.AsNoTracking().SingleAsync(x => x.IdEmpresa == miembro.IdEmpresa);
        if (!empresa.Estado) return new();
        var menus = await db.EContaxMenus.AsNoTracking().Where(x => x.EstadoMenu == 1).ToListAsync();
        var permitidos = menus.Select(x => x.IdMenu).ToHashSet();
        if (empresa.MenusJson is not null) permitidos.IntersectWith(LeerMenus(empresa.MenusJson));
        if (empresa.IdTitular != userId)
        {
            var sucursal = await db.EContaxSucursales.AsNoTracking()
                .FirstOrDefaultAsync(x => x.IdEmpresa == miembro.IdEmpresa && x.IdSucursal == miembro.IdSucursal && x.Estado);
            if (sucursal is null) return new();
            if (sucursal.MenusJson is not null) permitidos.IntersectWith(LeerMenus(sucursal.MenusJson));
            if (!miembro.EsAdminSucursal)
            {
                var perfil = await db.EContaxPerfiles.AsNoTracking().FirstOrDefaultAsync(x =>
                    x.IdPerfil == miembro.IdPerfil && x.IdEmpresa == miembro.IdEmpresa && x.Estado &&
                    (x.IdSucursal == null || x.IdSucursal == miembro.IdSucursal));
                if (perfil is null) return new();
                permitidos.IntersectWith(LeerMenus(miembro.MenusJson ?? perfil.MenusJson));
                var accionesJson = miembro.MenusJson is null ? perfil.AccionesJson : miembro.AccionesJson;
                if (accionesJson is not null)
                {
                    var acciones = LeerAcciones(accionesJson);
                    permitidos.RemoveWhere(id => EContaxPermisos.Normalizar(acciones.GetValueOrDefault(id)) == 0);
                }
            }
        }
        // Los padres se incluyen para navegar, sin habilitar sus rutas por herencia.
        var visibles = menus.Where(x => permitidos.Contains(x.IdMenu)).ToList();
        var pendientes = new Queue<int>(visibles.Where(x => x.IdPadre is > 0).Select(x => x.IdPadre!.Value));
        var visitados = visibles.Select(x => x.IdMenu).ToHashSet();
        while (pendientes.TryDequeue(out var id))
        {
            if (!visitados.Add(id)) continue;
            var padre = menus.FirstOrDefault(x => x.IdMenu == id);
            if (padre is null) continue;
            if (!permitidos.Contains(id)) padre.UrlMenu = null;
            visibles.Add(padre);
            if (padre.IdPadre is > 0) pendientes.Enqueue(padre.IdPadre.Value);
        }
        return visibles;
    }

    public async Task<bool> PuedeAdministrarAsync(int userId)
    {
        await using var db = await factory.CreateDbContextAsync();
        return await db.EContaxUsuariosContexto.AnyAsync(x => x.IdUsuario == userId && x.Estado && !x.Suspendido &&
            x.Usuario != null && x.Usuario.Estado == true && x.Empresa != null && x.Empresa.Estado &&
            (x.Empresa.IdTitular == userId || (x.EsAdminSucursal && x.Sucursal != null && x.Sucursal.Estado)));
    }

    public async Task<bool> PuedeAccederRutaAsync(int userId, string path, EContaxAccion accion = EContaxAccion.Ver)
    {
        if (accion == 0 || (accion & ~EContaxAccion.Todas) != 0) return false;
        await using var db = await factory.CreateDbContextAsync();
        if (!await db.EContaxUsuariosContexto.AnyAsync(x => x.IdUsuario == userId && x.Estado && !x.Suspendido &&
            x.Usuario != null && x.Usuario.Estado == true && x.Empresa != null && x.Empresa.Estado &&
            (x.Empresa.IdTitular == userId || (x.Sucursal != null && x.Sucursal.Estado)))) return false;
        path = path.Split('?', '#')[0].TrimEnd('/');
        if (path is EContaxRoutes.Root or EContaxRoutes.DashboardAlias or EContaxRoutes.Profile or EContaxRoutes.Soporte)
            return accion == EContaxAccion.Ver;
        if (path is "/e-contax/administracion/roles" or "/e-contax/administracion/usuarios")
            return accion == EContaxAccion.Ver || await PuedeAdministrarAsync(userId);
        var rutas = await db.EContaxMenus.AsNoTracking().Where(x => x.EstadoMenu == 1 && x.UrlMenu != null)
            .Select(x => x.UrlMenu!).ToListAsync();
        var coincidente = rutas.Select(x => x.Split('?', '#')[0].TrimEnd('/'))
            .Where(x => path.Equals(x, StringComparison.OrdinalIgnoreCase) ||
                (x != EContaxRoutes.Root && path.StartsWith(x + "/", StringComparison.OrdinalIgnoreCase)))
            .OrderByDescending(x => x.Length).FirstOrDefault();
        if (coincidente is null) return false;
        var menu = (await GetMenusAsync(userId)).FirstOrDefault(x => x.UrlMenu != null &&
            x.UrlMenu.Split('?', '#')[0].TrimEnd('/').Equals(coincidente, StringComparison.OrdinalIgnoreCase));
        return menu is not null && (await GetAccionesUsuarioAsync(userId)).GetValueOrDefault(menu.IdMenu).HasFlag(accion);
    }

    public async Task<Dictionary<int, EContaxAccion>> GetAccionesUsuarioAsync(int userId)
    {
        var menus = await GetMenusAsync(userId);
        await using var db = await factory.CreateDbContextAsync();
        var miembro = await db.EContaxUsuariosContexto.AsNoTracking().Include(x => x.Empresa)
            .FirstOrDefaultAsync(x => x.IdUsuario == userId);
        if (miembro is null) return new();
        string? json = null;
        if (miembro.Empresa?.IdTitular != userId && !miembro.EsAdminSucursal)
            json = miembro.MenusJson is not null ? miembro.AccionesJson :
                await db.EContaxPerfiles.Where(x => x.IdPerfil == miembro.IdPerfil).Select(x => x.AccionesJson).FirstOrDefaultAsync();
        var acciones = LeerAcciones(json);
        return menus.Where(x => x.UrlMenu is not null).ToDictionary(x => x.IdMenu,
            x => json is null ? EContaxAccion.Todas : EContaxPermisos.Normalizar(acciones.GetValueOrDefault(x.IdMenu)));
    }

    public async Task ExigirAccionAsync(int userId, string ruta, EContaxAccion accion)
    {
        if (!await PuedeAccederRutaAsync(userId, ruta, accion))
            throw new InvalidOperationException($"No tienes permiso para {accion.ToString().ToLowerInvariant()} en esta función de E-Contax.");
    }

    public async Task<EContaxAccion> GetAccionesRutaAsync(int userId, string ruta)
    {
        var menu = (await GetMenusAsync(userId)).FirstOrDefault(x => string.Equals(x.UrlMenu?.TrimEnd('/'), ruta.TrimEnd('/'), StringComparison.OrdinalIgnoreCase));
        return menu is null ? 0 : (await GetAccionesUsuarioAsync(userId)).GetValueOrDefault(menu.IdMenu);
    }

    public async Task<EContaxSeguridadResumen> GetResumenAsync(int userId)
    {
        await using var db = await factory.CreateDbContextAsync();
        var actor = await tenant.GetContextAsync(db, userId);
        var miembro = await db.EContaxUsuariosContexto.AsNoTracking().SingleAsync(x => x.IdUsuario == userId && x.Estado);
        var esAdmin = actor.EsJefeEmpresa || miembro.EsAdminSucursal;
        var perfiles = await db.EContaxPerfiles.AsNoTracking().Where(x => x.IdEmpresa == actor.IdEmpresa && x.Estado &&
            (actor.EsJefeEmpresa || x.IdSucursal == null || x.IdSucursal == actor.IdSucursal)).OrderBy(x => x.Nombre).ToListAsync();
        return new()
        {
            Contexto = actor, Miembro = miembro, EsAdmin = esAdmin,
            Empresa = await db.EContaxEmpresas.AsNoTracking().SingleAsync(x => x.IdEmpresa == actor.IdEmpresa),
            Sucursales = await db.EContaxSucursales.AsNoTracking().Where(x => x.IdEmpresa == actor.IdEmpresa && x.Estado &&
                (actor.EsJefeEmpresa || x.IdSucursal == actor.IdSucursal)).OrderBy(x => x.Nombre).ToListAsync(),
            Perfiles = esAdmin ? perfiles : new(),
            Usuarios = await db.EContaxUsuariosContexto.AsNoTracking().Include(x => x.Usuario)
                .Where(x => x.IdEmpresa == actor.IdEmpresa && x.Estado &&
                    (actor.EsJefeEmpresa || (esAdmin && x.IdSucursal == actor.IdSucursal) || x.IdUsuario == userId)).ToListAsync(),
            Invitaciones = esAdmin ? await db.EContaxInvitaciones.AsNoTracking().Where(x => x.IdEmpresa == actor.IdEmpresa &&
                (actor.EsJefeEmpresa || x.IdSucursal == actor.IdSucursal) && !x.Cancelada && x.Aceptada == null && x.Vence > DateTime.UtcNow)
                .OrderByDescending(x => x.IdInvitacion).ToListAsync() : new(),
            Menus = esAdmin ? await GetMenusAsync(userId) : new(),
            CatalogoMenus = esAdmin ? await db.EContaxMenus.AsNoTracking().Where(x => x.EstadoMenu == 1)
                .OrderBy(x => x.OrdenMenu).ThenBy(x => x.NombreMenu).ToListAsync() : new()
        };
    }

    public async Task<List<Auditoria>> GetHistorialAsync(int actorId)
    {
        await using var db = await factory.CreateDbContextAsync();
        var actor = await tenant.GetContextAsync(db, actorId);
        await ValidarAdminAsync(db, actorId, actor.IdSucursal);
        var prefijo = $"E-Contax/seguridad/{actor.IdEmpresa}/";
        if (!actor.EsJefeEmpresa) prefijo += $"{actor.IdSucursal}/";
        return await db.Auditorias.AsNoTracking().Include(x => x.Usuario)
            .Where(x => x.Accion != null && x.Accion.StartsWith(prefijo))
            .OrderByDescending(x => x.IdAuditoria).Take(100).ToListAsync();
    }

    private static void RegistrarCambio(AppDbContext db, int actorId, int empresaId, int? sucursalId,
        string accion, object? antes, object? despues)
    {
        db.Auditorias.Add(new Auditoria { IdUsuario = actorId, Fecha = DateTime.UtcNow,
            Accion = $"E-Contax/seguridad/{empresaId}/{sucursalId ?? 0}/{accion}",
            ValoresPrevios = antes is null ? null : JsonSerializer.Serialize(antes),
            ValorNuevo = despues is null ? null : JsonSerializer.Serialize(despues), Detalles = accion });
    }

    private async Task<EContaxUserContext> ValidarAdminAsync(AppDbContext db, int actorId, int? sucursalId = null)
    {
        var actor = await tenant.GetContextAsync(db, actorId);
        if (!actor.EsJefeEmpresa && (!await db.EContaxUsuariosContexto.AnyAsync(x => x.IdUsuario == actorId && x.Estado && x.EsAdminSucursal) ||
            sucursalId != actor.IdSucursal))
            throw new InvalidOperationException("Solo puedes administrar empleados y roles de tu sucursal.");
        if (sucursalId is > 0 && !await db.EContaxSucursales.AnyAsync(x => x.IdEmpresa == actor.IdEmpresa && x.IdSucursal == sucursalId && x.Estado))
            throw new InvalidOperationException("La sucursal no pertenece a tu empresa.");
        return actor;
    }

    private async Task ValidarPerfilAsync(AppDbContext db, int empresaId, int sucursalId, int perfilId)
    {
        if (!await db.EContaxPerfiles.AnyAsync(x => x.IdPerfil == perfilId && x.IdEmpresa == empresaId && x.Estado &&
            (x.IdSucursal == null || x.IdSucursal == sucursalId)))
            throw new InvalidOperationException("Selecciona un rol válido para esta sucursal.");
    }

    private async Task<string> ValidarMenusAsync(int actorId, IEnumerable<int> ids)
    {
        var solicitados = ids.ToHashSet();
        var menus = await GetMenusAsync(actorId);
        var disponibles = menus.Select(x => x.IdMenu).ToHashSet();
        if (!solicitados.IsSubsetOf(disponibles))
            throw new InvalidOperationException("No puedes conceder permisos que no tienes habilitados.");
        solicitados.ExceptWith(menus.Where(x => x.UrlMenu == null).Select(x => x.IdMenu));
        return Serializar(solicitados);
    }

    private async Task ValidarAccionesAsync(int actorId, string menusJson, string? accionesJson)
    {
        var menus = LeerMenus(menusJson);
        var acciones = LeerAcciones(accionesJson);
        if (acciones.Keys.Any(id => !menus.Contains(id)) ||
            (accionesJson is not null && menus.Any(id => !acciones.GetValueOrDefault(id).HasFlag(EContaxAccion.Ver))) ||
            acciones.Values.Any(x => (x & ~EContaxAccion.Todas) != 0))
            throw new InvalidOperationException("Los permisos por acción deben pertenecer a un menú habilitado y permitir consultar.");
        var disponibles = await GetAccionesUsuarioAsync(actorId);
        if (menus.Any(id => (AccionesDe(accionesJson, id) & ~disponibles.GetValueOrDefault(id)) != 0))
            throw new InvalidOperationException("No puedes conceder acciones que no tienes habilitadas.");
    }

    public async Task CambiarSuspensionAsync(int actorId, int userId, bool suspendido)
    {
        await MutarAsync(async db =>
        {
            var miembro = await db.EContaxUsuariosContexto.SingleAsync(x => x.IdUsuario == userId && x.Estado);
            var actor = await ValidarAdminAsync(db, actorId, miembro.IdSucursal);
            if (miembro.IdEmpresa != actor.IdEmpresa || userId == actor.IdUsuarioTitular || userId == actorId ||
                (!actor.EsJefeEmpresa && miembro.EsAdminSucursal))
                throw new InvalidOperationException("No puedes suspender al titular, tu propia cuenta ni administradores de otra jerarquía.");
            if (!suspendido)
                await ValidarPerfilAsync(db, actor.IdEmpresa, miembro.IdSucursal ?? 0, miembro.IdPerfil ?? 0);
            RegistrarCambio(db, actorId, actor.IdEmpresa, miembro.IdSucursal, suspendido ? "Suspender usuario" : "Reactivar usuario",
                new { miembro.IdUsuario, miembro.Suspendido }, new { IdUsuario = userId, Suspendido = suspendido });
            miembro.Suspendido = suspendido; miembro.FechaActualizacion = DateTime.UtcNow;
            await db.SaveChangesAsync();
        });
    }

    public async Task GuardarPerfilAsync(int actorId, int perfilId, string nombre, int? sucursalId, IEnumerable<int> menus,
        string? accionesJson = null)
    {
        nombre = nombre.Trim();
        if (nombre.Length is < 1 or > 200) throw new InvalidOperationException("El nombre del rol debe tener entre 1 y 200 caracteres.");
        var json = await ValidarMenusAsync(actorId, menus);
        await ValidarAccionesAsync(actorId, json, accionesJson);
        await MutarAsync(async db =>
        {
            var actor = await ValidarAdminAsync(db, actorId, sucursalId);
            var perfil = perfilId == 0 ? new EContaxPerfil { IdEmpresa = actor.IdEmpresa, IdSucursal = sucursalId }
                : await db.EContaxPerfiles.SingleAsync(x => x.IdPerfil == perfilId && x.IdEmpresa == actor.IdEmpresa);
            if (!actor.EsJefeEmpresa && perfil.IdSucursal != actor.IdSucursal)
                throw new InvalidOperationException("Solo el administrador de empresa puede modificar roles compartidos.");
            if (sucursalId is > 0 && await db.EContaxUsuariosContexto.AnyAsync(x => x.IdPerfil == perfilId && x.Estado &&
                x.IdEmpresa == actor.IdEmpresa && x.IdSucursal != sucursalId))
                throw new InvalidOperationException("El rol está asignado en otras sucursales. Reasigna esos usuarios antes de limitar su alcance.");
            RegistrarCambio(db, actorId, actor.IdEmpresa, perfilId == 0 || perfil.IdSucursal == sucursalId ? sucursalId : null, "Guardar rol",
                perfilId == 0 ? null : new { perfil.IdPerfil, perfil.Nombre, perfil.MenusJson, perfil.AccionesJson, perfil.IdSucursal },
                new { IdPerfil = perfilId, Nombre = nombre, MenusJson = json, AccionesJson = accionesJson, IdSucursal = sucursalId });
            perfil.Nombre = nombre; perfil.MenusJson = json; perfil.IdSucursal = sucursalId; perfil.AccionesJson = accionesJson;
            if (perfilId == 0) db.EContaxPerfiles.Add(perfil);
            await db.SaveChangesAsync();
        });
    }

    public async Task GuardarSucursalMenusAsync(int actorId, int sucursalId, IEnumerable<int> menus)
    {
        var json = await ValidarMenusAsync(actorId, menus);
        await MutarAsync(async db =>
        {
            var actor = await ValidarAdminAsync(db, actorId, sucursalId);
            if (!actor.EsJefeEmpresa) throw new InvalidOperationException("Solo el titular puede habilitar menús por sucursal.");
            var sucursal = await db.EContaxSucursales.SingleAsync(x => x.IdEmpresa == actor.IdEmpresa && x.IdSucursal == sucursalId);
            RegistrarCambio(db, actorId, actor.IdEmpresa, sucursalId, "Permisos de sucursal",
                new { sucursal.MenusJson }, new { MenusJson = json });
            sucursal.MenusJson = json;
            await db.SaveChangesAsync();
        });
    }

    public async Task GuardarUsuarioAsync(int actorId, int userId, int sucursalId, int perfilId, bool admin, string? menusJson,
        string? accionesJson = null)
    {
        var json = menusJson is null ? null : await ValidarMenusAsync(actorId, LeerMenus(menusJson));
        if (json is not null) await ValidarAccionesAsync(actorId, json, accionesJson);
        await MutarAsync(async db =>
        {
            var actor = await ValidarAdminAsync(db, actorId, sucursalId);
            await ValidarPerfilAsync(db, actor.IdEmpresa, sucursalId, perfilId);
            var miembro = await db.EContaxUsuariosContexto.SingleAsync(x => x.IdUsuario == userId && x.IdEmpresa == actor.IdEmpresa && x.Estado);
            if (userId == actor.IdUsuarioTitular) throw new InvalidOperationException("Transfiere la titularidad para cambiar al propietario.");
            if (!actor.EsJefeEmpresa && (miembro.IdSucursal != actor.IdSucursal || miembro.EsAdminSucursal || admin))
                throw new InvalidOperationException("No puedes modificar administradores ni usuarios de otra sucursal.");
            RegistrarCambio(db, actorId, actor.IdEmpresa, miembro.IdSucursal == sucursalId ? sucursalId : null, "Asignación de usuario",
                new { miembro.IdUsuario, miembro.IdSucursal, miembro.IdPerfil, miembro.EsAdminSucursal, miembro.MenusJson, miembro.AccionesJson },
                new { IdUsuario = userId, IdSucursal = sucursalId, IdPerfil = perfilId, EsAdminSucursal = admin, MenusJson = json, AccionesJson = accionesJson });
            miembro.IdSucursal = sucursalId; miembro.IdPerfil = perfilId; miembro.EsAdminSucursal = admin;
            miembro.MenusJson = json; miembro.AccionesJson = json is null ? null : accionesJson; miembro.FechaActualizacion = DateTime.UtcNow;
            await db.SaveChangesAsync();
        });
    }

    public async Task CrearCuentaAsync(int actorId, string nombres, string apellidos, string email, string clave, int sucursalId, int perfilId, bool admin)
    {
        email = new MailAddress(email.Trim()).Address;
        if (string.IsNullOrWhiteSpace(nombres) || string.IsNullOrWhiteSpace(apellidos) || clave.Length < 8)
            throw new InvalidOperationException("Completa nombres, apellidos y una contraseña de al menos 8 caracteres.");
        await MutarAsync(async db =>
        {
            var actor = await ValidarAdminAsync(db, actorId, sucursalId);
            if (admin && !actor.EsJefeEmpresa) throw new InvalidOperationException("Solo el titular puede asignar administradores.");
            await ValidarPerfilAsync(db, actor.IdEmpresa, sucursalId, perfilId);
            if (await db.Usuarios.AnyAsync(x => x.Email == email)) throw new InvalidOperationException("La cuenta ya existe. Utiliza una invitación.");
            var usuario = new Usuario
            {
                Nombres = nombres.Trim(), Apellidos = apellidos.Trim(), Email = email,
                PasswordHash = SecurityHelper.HashPassword(clave), Estado = true, IdTipoUsuario = 3,
                FechaCreacion = DateTime.UtcNow, ClaveTemporal = true, IntentosFallidos = 0, CuentaBloqueada = false
            };
            db.Usuarios.Add(usuario);
            await db.SaveChangesAsync();
            db.EContaxUsuariosContexto.Add(new() { IdUsuario = usuario.IdUsuario, IdEmpresa = actor.IdEmpresa,
                IdSucursal = sucursalId, IdPerfil = perfilId, EsAdminSucursal = admin });
            RegistrarCambio(db, actorId, actor.IdEmpresa, sucursalId, "Crear cuenta de empleado", null,
                new { usuario.IdUsuario, IdSucursal = sucursalId, IdPerfil = perfilId, EsAdminSucursal = admin });
            await db.SaveChangesAsync();
        });
    }

    public async Task<string> InvitarAsync(int actorId, string email, int sucursalId, int perfilId, bool admin)
    {
        email = new MailAddress(email.Trim()).Address;
        var token = Convert.ToHexString(RandomNumberGenerator.GetBytes(32));
        await MutarAsync(async db =>
        {
            var actor = await ValidarAdminAsync(db, actorId, sucursalId);
            if (admin && !actor.EsJefeEmpresa) throw new InvalidOperationException("Solo el titular puede asignar administradores.");
            await ValidarPerfilAsync(db, actor.IdEmpresa, sucursalId, perfilId);
            if (await db.EContaxUsuariosContexto.AnyAsync(x => x.Estado && x.Usuario != null && x.Usuario.Email == email))
                throw new InvalidOperationException("Esta cuenta ya está vinculada. Debe abandonar su empresa antes de recibir una invitación.");
            if (await db.EContaxInvitaciones.AnyAsync(x => x.Email == email && !x.Cancelada && x.Aceptada == null && x.Vence > DateTime.UtcNow))
                throw new InvalidOperationException("Esta cuenta ya tiene una invitación pendiente.");
            db.EContaxInvitaciones.Add(new() { IdEmpresa = actor.IdEmpresa, IdSucursal = sucursalId, IdPerfil = perfilId,
                IdInvitador = actorId, Email = email, EsAdminSucursal = admin, TokenHash = Hash(token), Vence = DateTime.UtcNow.AddDays(2) });
            RegistrarCambio(db, actorId, actor.IdEmpresa, sucursalId, "Invitar empleado", null,
                new { Correo = email, IdSucursal = sucursalId, IdPerfil = perfilId, EsAdminSucursal = admin });
            await db.SaveChangesAsync();
        });
        return token;
    }

    public async Task CancelarInvitacionAsync(int actorId, int invitacionId)
    {
        await MutarAsync(async db =>
        {
            var invitacion = await db.EContaxInvitaciones.SingleAsync(x => x.IdInvitacion == invitacionId);
            var actor = await ValidarAdminAsync(db, actorId, invitacion.IdSucursal);
            if (actor.IdEmpresa != invitacion.IdEmpresa) throw new InvalidOperationException("Invitación de otra empresa.");
            RegistrarCambio(db, actorId, actor.IdEmpresa, invitacion.IdSucursal, "Cancelar invitación",
                new { Correo = invitacion.Email }, null);
            invitacion.Cancelada = true;
            await db.SaveChangesAsync();
        });
    }

    public async Task AceptarAsync(int userId, string token)
    {
        if (token.Length != 64) throw new InvalidOperationException("Invitación inválida.");
        var hash = Hash(token);
        await MutarAsync(async db =>
        {
            var invitacion = await db.EContaxInvitaciones.SingleOrDefaultAsync(x => x.TokenHash == hash);
            var usuario = await db.Usuarios.SingleAsync(x => x.IdUsuario == userId && x.Estado == true);
            if (invitacion is null || invitacion.Cancelada || invitacion.Aceptada != null || invitacion.Vence <= DateTime.UtcNow)
                throw new InvalidOperationException("La invitación venció, fue cancelada o ya se utilizó.");
            if (!string.Equals(usuario.Email.Trim(), invitacion.Email, StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException("Inicia sesión con el correo que recibió la invitación.");
            var actor = await ValidarAdminAsync(db, invitacion.IdInvitador, invitacion.IdSucursal);
            if (actor.IdEmpresa != invitacion.IdEmpresa || (invitacion.EsAdminSucursal && !actor.EsJefeEmpresa))
                throw new InvalidOperationException("El remitente ya no puede conceder estos permisos.");
            await ValidarPerfilAsync(db, invitacion.IdEmpresa, invitacion.IdSucursal, invitacion.IdPerfil);
            var miembro = await db.EContaxUsuariosContexto.SingleOrDefaultAsync(x => x.IdUsuario == userId);
            if (miembro?.Estado == true) throw new InvalidOperationException("Abandona tu empresa actual antes de aceptar una invitación.");
            RegistrarCambio(db, userId, invitacion.IdEmpresa, invitacion.IdSucursal, "Aceptar invitación", null,
                new { IdUsuario = userId, invitacion.IdSucursal, invitacion.IdPerfil, invitacion.EsAdminSucursal });
            if (miembro is null) { miembro = new() { IdUsuario = userId }; db.EContaxUsuariosContexto.Add(miembro); }
            miembro.IdEmpresa = invitacion.IdEmpresa; miembro.IdSucursal = invitacion.IdSucursal;
            miembro.IdPerfil = invitacion.IdPerfil; miembro.EsAdminSucursal = invitacion.EsAdminSucursal;
            miembro.MenusJson = null; miembro.AccionesJson = null; miembro.Suspendido = false;
            miembro.Estado = true; miembro.FechaActualizacion = DateTime.UtcNow;
            invitacion.Aceptada = DateTime.UtcNow;
            await db.SaveChangesAsync();
        });
    }

    public async Task AbandonarAsync(int userId)
    {
        await MutarAsync(async db =>
        {
            var miembro = await db.EContaxUsuariosContexto.SingleAsync(x => x.IdUsuario == userId && x.Estado);
            var empresa = await db.EContaxEmpresas.SingleAsync(x => x.IdEmpresa == miembro.IdEmpresa);
            if (empresa.IdTitular == userId) throw new InvalidOperationException("Transfiere primero la titularidad a otro miembro de la empresa.");
            RegistrarCambio(db, userId, miembro.IdEmpresa, miembro.IdSucursal, "Abandonar empresa",
                new { miembro.IdUsuario, miembro.IdSucursal, miembro.IdPerfil }, null);
            miembro.Estado = false; miembro.IdSucursal = null; miembro.IdPerfil = null;
            miembro.EsAdminSucursal = false; miembro.MenusJson = null; miembro.AccionesJson = null;
            miembro.FechaActualizacion = DateTime.UtcNow;
            foreach (var invitacion in await db.EContaxInvitaciones.Where(x => x.IdInvitador == userId && x.Aceptada == null).ToListAsync())
                invitacion.Cancelada = true;
            await db.SaveChangesAsync();
        });
    }

    public async Task TransferirAsync(int actorId, int nuevoTitular)
    {
        await MutarAsync(async db =>
        {
            var actor = await ValidarAdminAsync(db, actorId);
            if (!actor.EsJefeEmpresa || actorId == nuevoTitular) throw new InvalidOperationException("Selecciona otro miembro para transferir la titularidad.");
            var nuevo = await db.EContaxUsuariosContexto.SingleOrDefaultAsync(x => x.IdUsuario == nuevoTitular &&
                x.IdEmpresa == actor.IdEmpresa && x.Estado && !x.Suspendido && x.Usuario != null && x.Usuario.Estado == true);
            if (nuevo is null) throw new InvalidOperationException("Selecciona un miembro activo para recibir la titularidad.");
            var empresa = await db.EContaxEmpresas.SingleAsync(x => x.IdEmpresa == actor.IdEmpresa);
            RegistrarCambio(db, actorId, actor.IdEmpresa, null, "Transferir titularidad",
                new { empresa.IdTitular }, new { IdTitular = nuevoTitular });
            empresa.IdTitular = nuevoTitular;
            // El anterior titular conserva administración de su sucursal hasta abandonar.
            var anterior = await db.EContaxUsuariosContexto.SingleAsync(x => x.IdUsuario == actorId);
            if (anterior.IdSucursal is null) anterior.IdSucursal = nuevo.IdSucursal;
            anterior.EsAdminSucursal = true;
            await db.SaveChangesAsync();
        });
    }

    private static string Hash(string token) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(token)));

    private async Task MutarAsync(Func<AppDbContext, Task> accion)
    {
        await using var strategyDb = await factory.CreateDbContextAsync();
        await strategyDb.Database.CreateExecutionStrategy().ExecuteAsync(async () =>
        {
            await using var db = await factory.CreateDbContextAsync();
            await using var transaction = await db.Database.BeginTransactionAsync(IsolationLevel.Serializable);
            await accion(db);
            await transaction.CommitAsync();
        });
    }
}

public sealed class EContaxSeguridadResumen
{
    public EContaxUserContext Contexto { get; set; } = null!;
    public EContaxUsuarioContexto Miembro { get; set; } = null!;
    public EContaxEmpresa Empresa { get; set; } = null!;
    public bool EsAdmin { get; set; }
    public List<EContaxSucursal> Sucursales { get; set; } = new();
    public List<EContaxPerfil> Perfiles { get; set; } = new();
    public List<EContaxUsuarioContexto> Usuarios { get; set; } = new();
    public List<EContaxInvitacion> Invitaciones { get; set; } = new();
    public List<EContaxMenu> Menus { get; set; } = new();
    public List<EContaxMenu> CatalogoMenus { get; set; } = new();
}
