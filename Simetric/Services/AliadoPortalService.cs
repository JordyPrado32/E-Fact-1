using Microsoft.EntityFrameworkCore;
using Simetric.Components.Helpers;
using Simetric.Data;
using Simetric.Models;
using System.Net.Mail;
using System.Security.Claims;

namespace Simetric.Services;

public sealed class AliadoPortalService
{
    public const string RoleName = "Aliado Comercial";
    public const string AdminRoleName = "Administrador Portal de Aliados";
    public const string RootRoute = "/aliados";
    public const string HomeRoute = "/aliados/inicio";
    public const string AdminRoute = "/aliados/admin";
    private const string BackOfficeInvoiceMarker = "[COMPRA_DOCS:";

    private static readonly SemaphoreSlim SchemaLock = new(1, 1);
    private static readonly IReadOnlySet<string> ResultadosGestion = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
    {
        "Pendiente", "Contactado", "No responde", "Interesado", "Pendiente de pago", "No renueva", "Renovado"
    };
    private static bool _schemaEnsured;

    private readonly IDbContextFactory<AppDbContext> _dbFactory;
    private readonly VendedorBackOfficeService _vendedorService;
    private readonly AuditService _auditService;

    public AliadoPortalService(
        IDbContextFactory<AppDbContext> dbFactory,
        VendedorBackOfficeService vendedorService,
        AuditService auditService)
    {
        _dbFactory = dbFactory;
        _vendedorService = vendedorService;
        _auditService = auditService;
    }

    public async Task EnsureSchemaAsync()
    {
        if (_schemaEnsured)
            return;

        await SchemaLock.WaitAsync();
        try
        {
            if (_schemaEnsured)
                return;

            await _vendedorService.EnsureSchemaAsync();
            await using var context = await _dbFactory.CreateDbContextAsync();
            foreach (var statement in BuildEnsureSchemaStatements())
                await context.Database.ExecuteSqlRawAsync(statement);

            _schemaEnsured = true;
        }
        finally
        {
            SchemaLock.Release();
        }
    }

    public async Task<AliadoPortalContext?> ObtenerContextoAsync(int userId)
    {
        await EnsureSchemaAsync();
        if (userId <= 0)
            return null;

        await using var context = await _dbFactory.CreateDbContextAsync();
        var usuario = await context.Usuarios
            .AsNoTracking()
            .Where(x => x.IdUsuario == userId && x.Estado == true)
            .Select(x => new
            {
                x.IdUsuario,
                x.IdTipoUsuario,
                x.IdVendedor,
                x.Nombres,
                x.Apellidos,
                x.Email,
                x.Celular,
                x.AvatarUrl
            })
            .FirstOrDefaultAsync();

        if (usuario?.IdTipoUsuario is not > 0)
            return null;

        var tipo = await context.TipoUsuario
            .AsNoTracking()
            .Where(x => x.IdTipoUsuario == usuario.IdTipoUsuario && x.Estado == true)
            .Select(x => x.NombreTipo)
            .FirstOrDefaultAsync();

        var rolPortal = await context.AliadoPortalUsuariosRoles.AsNoTracking()
            .Where(x => x.IdUsuario == userId)
            .Join(context.AliadoPortalRoles.AsNoTracking(), x => x.IdRol, x => x.IdRol, (_, rol) => rol)
            .FirstOrDefaultAsync();
        if (rolPortal is null || !rolPortal.Activo)
            return null;

        var esAdministrador = string.Equals(tipo, AdminRoleName, StringComparison.OrdinalIgnoreCase) ||
                              usuario.IdTipoUsuario == BackOfficePermissionHelper.SuperAdministradorRoleId;
        var esAliadoPorTipo = string.Equals(tipo, RoleName, StringComparison.OrdinalIgnoreCase);
        if (!esAdministrador && !esAliadoPorTipo &&
            !string.Equals(rolPortal.Nombre, RoleName, StringComparison.OrdinalIgnoreCase) &&
            !string.Equals(rolPortal.Nombre, AdminRoleName, StringComparison.OrdinalIgnoreCase))
            return null;

        esAdministrador = string.Equals(rolPortal.Nombre, AdminRoleName, StringComparison.OrdinalIgnoreCase);
        if (esAdministrador)
        {
            return new AliadoPortalContext
            {
                IdUsuario = usuario.IdUsuario,
                IdTipoUsuario = usuario.IdTipoUsuario.Value,
                IdRolPortal = rolPortal.IdRol,
                Nombre = $"{usuario.Nombres} {usuario.Apellidos}".Trim(),
                Email = usuario.Email,
                Celular = usuario.Celular,
                AvatarUrl = usuario.AvatarUrl,
                NombreAliado = "Administración del Portal",
                EsAdministrador = true
            };
        }

        if (usuario.IdVendedor is not > 0)
            return null;

        var aliado = await context.VendedoresBackOffice
            .AsNoTracking()
            .Where(x => x.IdVendedor == usuario.IdVendedor && x.Activo && !x.EsSistema)
            .Select(x => new { x.IdVendedor, x.Nombre, x.CodigoReferencia, x.PorcentajeBase })
            .FirstOrDefaultAsync();

        return aliado is null
            ? null
            : new AliadoPortalContext
            {
                IdUsuario = usuario.IdUsuario,
                IdTipoUsuario = usuario.IdTipoUsuario.Value,
                IdRolPortal = rolPortal.IdRol,
                IdVendedor = aliado.IdVendedor,
                Nombre = $"{usuario.Nombres} {usuario.Apellidos}".Trim(),
                Email = usuario.Email,
                Celular = usuario.Celular,
                AvatarUrl = usuario.AvatarUrl,
                NombreAliado = aliado.Nombre,
                CodigoReferencia = aliado.CodigoReferencia,
                PorcentajeBase = aliado.PorcentajeBase <= 0 ? 30m : aliado.PorcentajeBase,
                EnlaceRegistro = _vendedorService.ConstruirRutaRegistro(new VendedorBackOffice
                {
                    IdVendedor = aliado.IdVendedor,
                    CodigoReferencia = aliado.CodigoReferencia
                })
            };
    }

    public async Task<bool> TieneAccesoAsync(ClaimsPrincipal user, string ruta)
    {
        if (!int.TryParse(user.FindFirst("IdUsuario")?.Value, out var userId))
            return false;

        var contexto = await ObtenerContextoAsync(userId);
        if (contexto is null)
            return false;

        var relative = "/" + ruta.Split('?', '#')[0].Trim('/');
        var adminRoute = AdminRoute.TrimEnd('/');
        var esRutaAdmin = relative.Equals(adminRoute, StringComparison.OrdinalIgnoreCase) ||
                          relative.StartsWith($"{adminRoute}/", StringComparison.OrdinalIgnoreCase);
        if (!contexto.EsAdministrador &&
            esRutaAdmin)
            return false;

        if (contexto.EsAdministrador &&
            !esRutaAdmin &&
            !relative.Equals(HomeRoute, StringComparison.OrdinalIgnoreCase) &&
            !relative.Equals($"{RootRoute}/perfil", StringComparison.OrdinalIgnoreCase))
            return false;

        return (await ObtenerMenusAsync(contexto.IdRolPortal)).Any(x =>
            relative.Equals(x.Ruta, StringComparison.OrdinalIgnoreCase) ||
            relative.StartsWith($"{x.Ruta.TrimEnd('/')}/", StringComparison.OrdinalIgnoreCase));
    }

    public async Task<(bool Success, string Message)> ActualizarClaveAsync(
        int userId,
        string? claveActual,
        string? claveNueva,
        string? confirmacion)
    {
        var contexto = await ObtenerContextoAsync(userId);
        if (contexto is null)
            return (false, "No tienes acceso para actualizar esta cuenta.");

        if (string.IsNullOrWhiteSpace(claveActual) ||
            string.IsNullOrWhiteSpace(claveNueva) ||
            string.IsNullOrWhiteSpace(confirmacion))
            return (false, "Completa todos los campos de seguridad.");

        if (claveNueva.Length < 8)
            return (false, "La nueva contraseña debe tener al menos 8 caracteres.");

        if (!string.Equals(claveNueva, confirmacion, StringComparison.Ordinal))
            return (false, "La confirmación no coincide con la nueva contraseña.");

        await using var db = await _dbFactory.CreateDbContextAsync();
        var usuario = await db.Usuarios.FirstOrDefaultAsync(x =>
            x.IdUsuario == userId &&
            (contexto.EsAdministrador || x.IdVendedor == contexto.IdVendedor) &&
            x.Estado == true);
        if (usuario is null || !SecurityHelper.VerifyPassword(claveActual.Trim(), usuario.PasswordHash))
            return (false, "La contraseña actual no es correcta.");

        usuario.PasswordHash = SecurityHelper.HashPassword(claveNueva);
        usuario.ClaveTemporal = false;
        usuario.IntentosFallidos = 0;
        usuario.CuentaBloqueada = false;
        usuario.FechaDesbloqueo = null;
        usuario.TokenRecuperacion = null;
        usuario.FechaExpiracionToken = null;
        await db.SaveChangesAsync();
        await _auditService.TryRegistrarAuditoriaAsync(userId, "MODIFICAR", new { IdUsuario = userId }, new { Cambio = "Clave" }, new { Modulo = "PortalAliados", Entidad = "Usuario" });
        return (true, "Contraseña actualizada correctamente.");
    }

    public async Task<IReadOnlyList<AliadoPortalMenu>> ObtenerMenusAsync(int idRol)
    {
        await EnsureSchemaAsync();
        await using var db = await _dbFactory.CreateDbContextAsync();
        return await db.AliadoPortalRolesMenus.AsNoTracking()
            .Where(x => x.IdRol == idRol)
            .Join(db.AliadoPortalMenus.AsNoTracking().Where(x => x.Activo), x => x.IdMenu, x => x.IdMenu, (_, menu) => menu)
            .OrderBy(x => x.Orden).ToListAsync();
    }

    public async Task<IReadOnlyList<AliadoPortalRol>> ObtenerRolesAsync()
    {
        await EnsureSchemaAsync();
        await using var db = await _dbFactory.CreateDbContextAsync();
        return await db.AliadoPortalRoles.AsNoTracking().Where(x => x.Activo).OrderBy(x => x.IdRol).ToListAsync();
    }

    public async Task<IReadOnlyList<AliadoPortalMenu>> ObtenerCatalogoMenusAsync()
    {
        await EnsureSchemaAsync();
        await using var db = await _dbFactory.CreateDbContextAsync();
        return await db.AliadoPortalMenus.AsNoTracking().Where(x => x.Activo).OrderBy(x => x.Orden).ToListAsync();
    }

    public async Task<bool> ActualizarPermisosAsync(int actorId, int idRol, IReadOnlyCollection<int> menuIds)
    {
        await EnsureSchemaAsync();
        await using var db = await _dbFactory.CreateDbContextAsync();
        if (!await EsAdministradorPortalAsync(db, actorId))
            return false;

        var existentes = await db.AliadoPortalRolesMenus.Where(x => x.IdRol == idRol).ToListAsync();
        db.AliadoPortalRolesMenus.RemoveRange(existentes);
        var permitidos = await db.AliadoPortalMenus.Where(x => x.Activo && menuIds.Contains(x.IdMenu)).Select(x => x.IdMenu).ToListAsync();
        var rol = await db.AliadoPortalRoles.AsNoTracking().FirstOrDefaultAsync(x => x.IdRol == idRol && x.Activo);
        var menuAdmin = await db.AliadoPortalMenus.AsNoTracking().Where(x => x.Ruta == AdminRoute).Select(x => (int?)x.IdMenu).FirstOrDefaultAsync();
        if (rol is null || (!string.Equals(rol.Nombre, AdminRoleName, StringComparison.OrdinalIgnoreCase) && menuAdmin.HasValue && permitidos.Contains(menuAdmin.Value)))
            return false;

        db.AliadoPortalRolesMenus.AddRange(permitidos.Select(idMenu => new AliadoPortalRolMenu { IdRol = idRol, IdMenu = idMenu }));
        await db.SaveChangesAsync();
        await _auditService.TryRegistrarAuditoriaAsync(actorId, "MODIFICAR", existentes.Select(x => x.IdMenu).ToArray(), permitidos, new { Modulo = "PortalAliados", Entidad = "Permisos", IdRol = idRol });
        return true;
    }

    public async Task<AliadoDashboardDto?> ObtenerDashboardAsync(int userId)
    {
        var contexto = await ObtenerContextoAsync(userId);
        if (contexto is null)
            return null;

        await SincronizarComisionesAsync(contexto.IdVendedor);
        var facturas = await ObtenerFacturasAsync(contexto.IdVendedor);
        var hoy = DateTime.Today;
        var inicioMes = new DateTime(hoy.Year, hoy.Month, 1);
        var ventasPeriodo = facturas.Where(x => x.Fecha >= inicioMes).ToList();
        var proximasRenovaciones = facturas
            .Where(x => x.FechaVencimiento.HasValue && x.FechaVencimiento.Value.Date >= hoy && x.FechaVencimiento.Value.Date <= hoy.AddDays(30))
            .OrderBy(x => x.FechaVencimiento)
            .ToList();
        var renovaciones = proximasRenovaciones
            .Take(5)
            .Select(x => ToRenovacion(x, null, contexto.PorcentajeBase))
            .ToList();
        var resumenComisiones = await ObtenerResumenComisionesAsync(contexto.IdVendedor);

        return new AliadoDashboardDto
        {
            Contexto = contexto,
            VentasPeriodo = ventasPeriodo.Sum(x => x.Total),
            VentasCantidad = ventasPeriodo.Count,
            ComisionesGeneradas = resumenComisiones.Generadas,
            ComisionesPendientesAprobacion = resumenComisiones.PendientesAprobacion,
            ComisionesAprobadas = resumenComisiones.Aprobadas,
            ComisionesPagadas = resumenComisiones.Pagadas,
            RenovacionesProximas = proximasRenovaciones.Count,
            RenovacionesUrgentes = proximasRenovaciones.Count(x => x.FechaVencimiento!.Value.Date <= hoy.AddDays(7)),
            Renovaciones = renovaciones,
            LinkPersonalizado = contexto.EnlaceRegistro
        };
    }

    public async Task<IReadOnlyList<AliadoClienteDto>> ObtenerClientesAsync(int userId)
    {
        var contexto = await ObtenerContextoAsync(userId);
        if (contexto is null)
            return Array.Empty<AliadoClienteDto>();

        await using var db = await _dbFactory.CreateDbContextAsync();
        var clientes = await db.Clientes
            .AsNoTracking()
            .Where(x => x.Idvendedor == contexto.IdVendedor && x.Estado != false && x.Usuario != userId)
            .Select(x => new AliadoClienteDto
            {
                IdCliente = x.Codcliente,
                Identificacion = x.Numeroidentificacion,
                Nombre = x.Nombrerazonsocial ?? x.Nombrecomercial ?? ((x.Nombres ?? "") + " " + (x.Apellidos ?? "")),
                Email = x.Correo,
                Telefono = x.Celular ?? x.Telefonoconvencional
            })
            .OrderBy(x => x.Nombre)
            .ToListAsync();

        var facturas = await ObtenerFacturasAsync(contexto.IdVendedor);
        var ultimaPorCliente = facturas
            .Where(x => x.IdCliente.HasValue)
            .GroupBy(x => x.IdCliente!.Value)
            .ToDictionary(x => x.Key, x => x.OrderByDescending(y => y.Fecha).First());

        foreach (var cliente in clientes)
        {
            if (!ultimaPorCliente.TryGetValue(cliente.IdCliente, out var factura))
                continue;

            cliente.Producto = factura.Producto;
            cliente.FechaCompra = factura.Fecha;
            cliente.FechaVencimiento = factura.FechaVencimiento;
            cliente.Estado = factura.Estado;
        }

        return clientes;
    }

    public async Task<AliadoClienteDetalleDto?> ObtenerClienteAsync(int userId, int idCliente)
    {
        var contexto = await ObtenerContextoAsync(userId);
        if (contexto is null || idCliente <= 0)
            return null;

        await using var db = await _dbFactory.CreateDbContextAsync();
        var cliente = await db.Clientes
            .AsNoTracking()
            .Where(x => x.Codcliente == idCliente && x.Idvendedor == contexto.IdVendedor && x.Usuario != userId)
            .Select(x => new AliadoClienteDetalleDto
            {
                IdCliente = x.Codcliente,
                Identificacion = x.Numeroidentificacion,
                Nombre = x.Nombrerazonsocial ?? x.Nombrecomercial ?? ((x.Nombres ?? "") + " " + (x.Apellidos ?? "")),
                Email = x.Correo,
                Telefono = x.Celular ?? x.Telefonoconvencional,
                Direccion = x.Direccion
            })
            .FirstOrDefaultAsync();

        if (cliente is null)
            return null;

        var facturas = await ObtenerFacturasAsync(contexto.IdVendedor, idCliente);
        cliente.Productos = facturas
            .OrderByDescending(x => x.Fecha)
            .Select(x => new AliadoProductoDto
            {
                Producto = x.Producto,
                Plan = x.Plan,
                FechaCompra = x.Fecha,
                FechaVencimiento = x.FechaVencimiento,
                Estado = x.Estado,
                Valor = x.Total
            })
            .ToList();
        return cliente;
    }

    public async Task<IReadOnlyList<AliadoRenovacionDto>> ObtenerRenovacionesAsync(int userId, string? filtro = null)
    {
        var contexto = await ObtenerContextoAsync(userId);
        if (contexto is null)
            return Array.Empty<AliadoRenovacionDto>();

        var facturas = await ObtenerFacturasAsync(contexto.IdVendedor);
        var ids = facturas.Where(x => x.FechaVencimiento.HasValue).Select(x => x.IdFactura).ToList();
        await using var db = await _dbFactory.CreateDbContextAsync();
        var gestiones = await db.AliadoRenovacionGestiones
            .AsNoTracking()
            .Where(x => x.IdVendedor == contexto.IdVendedor && ids.Contains(x.IdFactura))
            .GroupBy(x => x.IdFactura)
            .Select(x => x.OrderByDescending(y => y.FechaGestion).First())
            .ToDictionaryAsync(x => x.IdFactura);

        var resultado = facturas
            .Where(x => x.FechaVencimiento.HasValue && x.FechaVencimiento.Value.Date <= DateTime.Today.AddDays(90))
            .OrderBy(x => x.FechaVencimiento)
            .Select(x => ToRenovacion(x, gestiones.TryGetValue(x.IdFactura, out var gestion) ? gestion : null, contexto.PorcentajeBase))
            .ToList();

        if (!string.IsNullOrWhiteSpace(filtro))
            resultado = resultado.Where(x => x.EstadoGestion.Contains(filtro, StringComparison.OrdinalIgnoreCase)).ToList();

        return resultado;
    }

    public async Task<(bool Success, string Message)> RegistrarGestionAsync(int userId, int idFactura, string resultado, string? observacion, DateTime? proximoSeguimiento)
    {
        var contexto = await ObtenerContextoAsync(userId);
        if (contexto is null)
            return (false, "No tienes acceso al portal de aliados.");

        resultado = resultado?.Trim() ?? string.Empty;
        observacion = string.IsNullOrWhiteSpace(observacion) ? null : observacion.Trim();
        if (idFactura <= 0 || !ResultadosGestion.Contains(resultado))
            return (false, "Selecciona un resultado para registrar la gestión.");
        if (observacion?.Length > 500)
            return (false, "La observación no puede superar 500 caracteres.");
        if (proximoSeguimiento?.Date < DateTime.Today)
            return (false, "El próximo seguimiento no puede estar en el pasado.");

        await using var db = await _dbFactory.CreateDbContextAsync();
        var pertenece = await db.Facturas.AsNoTracking().AnyAsync(x =>
            x.Codfactura == idFactura && x.Idvendedor == contexto.IdVendedor);
        if (!pertenece)
            return (false, "El servicio seleccionado no pertenece a tu canal.");

        db.AliadoRenovacionGestiones.Add(new AliadoRenovacionGestion
        {
            IdVendedor = contexto.IdVendedor,
            IdFactura = idFactura,
            FechaGestion = DateTime.Now,
            Resultado = resultado,
            OrigenGestion = "Aliado",
            Observacion = observacion,
            ProximoSeguimiento = proximoSeguimiento?.Date,
            IdUsuario = userId
        });
        await db.SaveChangesAsync();
        await _auditService.TryRegistrarAuditoriaAsync(userId, "REGISTRAR", null, new { IdFactura = idFactura, Resultado = resultado, ProximoSeguimiento = proximoSeguimiento }, new { Modulo = "PortalAliados", Entidad = "GestionRenovacion", IdVendedor = contexto.IdVendedor });
        return (true, "Gestión de renovación registrada correctamente.");
    }

    public async Task<IReadOnlyList<AliadoAdminComisionRow>> ObtenerComisionesAdministracionAsync(int actorId)
    {
        await EnsureSchemaAsync();
        await using var db = await _dbFactory.CreateDbContextAsync();
        if (!await EsAdministradorPortalAsync(db, actorId))
            return Array.Empty<AliadoAdminComisionRow>();

        var vendedorIds = await db.VendedoresBackOffice
            .AsNoTracking()
            .Where(x => !x.EsSistema &&
                (db.Usuarios.Any(u =>
                    u.IdVendedor == x.IdVendedor &&
                    db.AliadoPortalUsuariosRoles.Any(ur => ur.IdUsuario == u.IdUsuario &&
                        db.AliadoPortalRoles.Any(r => r.IdRol == ur.IdRol && r.Nombre == RoleName))) ||
                 db.AliadoComisiones.Any(c => c.IdVendedor == x.IdVendedor)))
            .Select(x => x.IdVendedor)
            .ToListAsync();
        foreach (var idVendedor in vendedorIds)
            await SincronizarComisionesAsync(idVendedor);

        return await db.AliadoComisiones
            .AsNoTracking()
            .Where(x => db.VendedoresBackOffice.Any(v => v.IdVendedor == x.IdVendedor && !v.EsSistema))
            .Join(db.VendedoresBackOffice.AsNoTracking(), x => x.IdVendedor, x => x.IdVendedor, (comision, aliado) => new { comision, aliado })
            .OrderByDescending(x => x.comision.FechaGeneracion)
            .Select(x => new AliadoAdminComisionRow
            {
                IdComision = x.comision.IdComision,
                IdVendedor = x.comision.IdVendedor,
                Aliado = x.aliado.Nombre,
                IdFactura = x.comision.IdFactura,
                Cliente = db.Clientes
                    .Where(c => c.Codcliente == x.comision.IdCliente)
                    .Select(c => c.Nombrerazonsocial ?? c.Nombrecomercial ?? ((c.Nombres ?? "") + " " + (c.Apellidos ?? "")))
                    .FirstOrDefault() ?? "Cliente",
                TipoComision = x.comision.TipoComision,
                BaseComisionable = x.comision.BaseComisionable,
                Porcentaje = x.comision.Porcentaje,
                Valor = x.comision.Valor,
                Estado = x.comision.Estado,
                FechaGeneracion = x.comision.FechaGeneracion,
                Periodo = db.AliadoLiquidaciones
                    .Where(l => l.IdLiquidacion == x.comision.IdLiquidacion)
                    .Select(l => l.Periodo)
                    .FirstOrDefault(),
                ReferenciaPago = db.AliadoLiquidaciones
                    .Where(l => l.IdLiquidacion == x.comision.IdLiquidacion)
                    .Select(l => l.ReferenciaPago)
                    .FirstOrDefault()
            })
            .ToListAsync();
    }

    public async Task<(bool Success, string Message)> AprobarComisionAsync(int actorId, int idComision)
    {
        await EnsureSchemaAsync();
        await using var db = await _dbFactory.CreateDbContextAsync();
        if (!await EsAdministradorPortalAsync(db, actorId))
            return (false, "No tienes permisos para aprobar comisiones.");

        var comision = await db.AliadoComisiones.FirstOrDefaultAsync(x => x.IdComision == idComision);
        if (comision is null)
            return (false, "La comisión no existe.");
        if (comision.Estado != "Generada")
            return (false, "Solo se pueden aprobar comisiones generadas.");

        var estadoAnterior = comision.Estado;
        comision.Estado = "Aprobada";
        comision.FechaAprobacion = DateTime.Now;
        comision.IdUsuarioAprobacion = actorId;
        await db.SaveChangesAsync();
        await _auditService.TryRegistrarAuditoriaAsync(actorId, "APROBAR", new { IdComision = idComision, Estado = estadoAnterior }, comision, new { Modulo = "PortalAliados", Entidad = "Comision" });
        return (true, "Comisión aprobada correctamente.");
    }

    public Task<(bool Success, string Message)> PagarComisionAsync(int actorId, int idComision, string? referenciaPago)
        => LiquidarComisionesAsync(actorId, new[] { idComision }, referenciaPago);

    public async Task<(bool Success, string Message)> LiquidarComisionesAsync(int actorId, IReadOnlyCollection<int> idsComision, string? referenciaPago)
    {
        await EnsureSchemaAsync();
        if (idsComision.Count == 0)
            return (false, "Selecciona al menos una comisión.");
        referenciaPago = string.IsNullOrWhiteSpace(referenciaPago) ? null : referenciaPago.Trim();
        if (referenciaPago?.Length > 100)
            return (false, "La referencia de pago no puede superar 100 caracteres.");

        await using var db = await _dbFactory.CreateDbContextAsync();
        if (!await EsAdministradorPortalAsync(db, actorId))
            return (false, "No tienes permisos para registrar pagos.");
        var ids = idsComision.Distinct().ToArray();
        var comisiones = await db.AliadoComisiones.Where(x => ids.Contains(x.IdComision)).ToListAsync();
        if (comisiones.Count != ids.Length)
            return (false, "Una o más comisiones seleccionadas no existen.");
        if (comisiones.Any(x => x.Estado != "Aprobada"))
            return (false, "Solo se pueden liquidar comisiones aprobadas.");

        var ahora = DateTime.Now;
        var periodo = ahora.ToString("yyyy-MM");
        await using var transaction = await db.Database.BeginTransactionAsync();
        foreach (var grupo in comisiones.GroupBy(x => x.IdVendedor))
        {
            var liquidacion = await db.AliadoLiquidaciones
                .Where(x => x.IdVendedor == grupo.Key && x.Periodo == periodo && x.Estado == "Pendiente")
                .OrderByDescending(x => x.IdLiquidacion)
                .FirstOrDefaultAsync();
            if (liquidacion is null)
            {
                liquidacion = new AliadoLiquidacion { IdVendedor = grupo.Key, Periodo = periodo, Fecha = ahora, Estado = "Pendiente" };
                db.AliadoLiquidaciones.Add(liquidacion);
                await db.SaveChangesAsync();
            }
            liquidacion.Total += grupo.Sum(x => x.Valor);
            liquidacion.Fecha = ahora;
            liquidacion.Estado = "Pagada";
            liquidacion.ReferenciaPago = referenciaPago;
            foreach (var comision in grupo)
            {
                comision.Estado = "Pagada";
                comision.FechaPago = ahora;
                comision.IdLiquidacion = liquidacion.IdLiquidacion;
                comision.IdUsuarioPago = actorId;
            }
        }
        await db.SaveChangesAsync();
        await transaction.CommitAsync();
        await _auditService.TryRegistrarAuditoriaAsync(actorId, "PAGAR", null, new { IdsComision = ids, ReferenciaPago = referenciaPago }, new { Modulo = "PortalAliados", Entidad = "Liquidacion" });
        return (true, $"Se liquidaron {comisiones.Count} comisión(es) correctamente.");
    }

    public async Task<(bool Success, string Message)> RevertirComisionAsync(int actorId, int idComision, string? motivo)
    {
        await EnsureSchemaAsync();
        motivo = motivo?.Trim();
        if (string.IsNullOrWhiteSpace(motivo) || motivo.Length > 500)
            return (false, "Indica un motivo de reversión de hasta 500 caracteres.");
        await using var db = await _dbFactory.CreateDbContextAsync();
        if (!await EsAdministradorPortalAsync(db, actorId))
            return (false, "No tienes permisos para revertir comisiones.");
        var original = await db.AliadoComisiones.FirstOrDefaultAsync(x => x.IdComision == idComision);
        if (original is null)
            return (false, "La comisión no existe.");
        if (original.Estado is "Anulada" or "Revertida")
            return (false, "La comisión ya fue anulada o revertida.");
        if (original.Estado == "Pagada")
        {
            original.Estado = "Revertida";
            var tipo = $"Reversion-{original.TipoComision}";
            db.AliadoComisiones.Add(new AliadoComision
            {
                IdVendedor = original.IdVendedor,
                IdFactura = original.IdFactura,
                IdCliente = original.IdCliente,
                TipoComision = tipo[..Math.Min(40, tipo.Length)],
                BaseComisionable = -original.BaseComisionable,
                Porcentaje = original.Porcentaje,
                Valor = -original.Valor,
                Estado = "Generada",
                FechaGeneracion = DateTime.Now
            });
        }
        else
        {
            original.Estado = "Anulada";
        }
        await db.SaveChangesAsync();
        await _auditService.TryRegistrarAuditoriaAsync(actorId, "REVERTIR", new { IdComision = idComision }, new { Motivo = motivo, Estado = original.Estado }, new { Modulo = "PortalAliados", Entidad = "Comision" });
        return (true, original.Estado == "Revertida" ? "Pago revertido mediante ajuste compensatorio." : "Comisión anulada correctamente.");
    }

    public async Task<AliadoPortalConfiguracion?> ObtenerConfiguracionPortalAsync(int actorId)
    {
        await EnsureSchemaAsync();
        await using var db = await _dbFactory.CreateDbContextAsync();
        return await EsAdministradorPortalAsync(db, actorId)
            ? await db.AliadoPortalConfiguraciones.AsNoTracking().SingleAsync(x => x.IdConfiguracion == 1)
            : null;
    }

    public async Task<(bool Success, string Message)> ActualizarConfiguracionPortalAsync(
        int actorId,
        decimal porcentajeVentaNueva,
        decimal porcentajeRenovacionAliado,
        decimal porcentajeRenovacionNumerica,
        int diasIntervencionNumerica)
    {
        await EnsureSchemaAsync();
        if (!TryNormalizarPorcentaje(porcentajeVentaNueva, out porcentajeVentaNueva) ||
            !TryNormalizarPorcentaje(porcentajeRenovacionAliado, out porcentajeRenovacionAliado) ||
            !TryNormalizarPorcentaje(porcentajeRenovacionNumerica, out porcentajeRenovacionNumerica) ||
            diasIntervencionNumerica is < 0 or > 365)
            return (false, "La configuración de renovación no es válida.");

        await using var db = await _dbFactory.CreateDbContextAsync();
        if (!await EsAdministradorPortalAsync(db, actorId))
            return (false, "No tienes permisos para modificar la configuración.");

        var configuracion = await db.AliadoPortalConfiguraciones.SingleAsync(x => x.IdConfiguracion == 1);
        var anterior = new
        {
            configuracion.PorcentajeVentaNueva,
            configuracion.PorcentajeRenovacionAliado,
            configuracion.PorcentajeRenovacionNumerica,
            configuracion.DiasIntervencionNumerica
        };
        configuracion.PorcentajeVentaNueva = porcentajeVentaNueva;
        configuracion.PorcentajeRenovacionAliado = porcentajeRenovacionAliado;
        configuracion.PorcentajeRenovacionNumerica = porcentajeRenovacionNumerica;
        configuracion.DiasIntervencionNumerica = diasIntervencionNumerica;
        await db.SaveChangesAsync();
        await _auditService.TryRegistrarAuditoriaAsync(actorId, "MODIFICAR", anterior, configuracion, new { Modulo = "PortalAliados", Entidad = "ConfiguracionComisiones" });
        return (true, "Configuración actualizada correctamente.");
    }

    public async Task<(bool Success, string Message)> ActualizarPorcentajeAliadoAsync(int actorId, int idVendedor, decimal porcentajeBase)
    {
        await EnsureSchemaAsync();
        if (!TryNormalizarPorcentaje(porcentajeBase, out porcentajeBase))
            return (false, "El porcentaje debe estar entre 0 y 100.");

        await using var db = await _dbFactory.CreateDbContextAsync();
        if (!await EsAdministradorPortalAsync(db, actorId))
            return (false, "No tienes permisos para modificar porcentajes.");

        var aliado = await db.VendedoresBackOffice.FirstOrDefaultAsync(x => x.IdVendedor == idVendedor && !x.EsSistema);
        if (aliado is null)
            return (false, "El aliado no existe.");

        var anterior = aliado.PorcentajeBase;
        aliado.PorcentajeBase = porcentajeBase;
        await db.SaveChangesAsync();
        await _auditService.TryRegistrarAuditoriaAsync(actorId, "MODIFICAR", new { IdVendedor = idVendedor, PorcentajeBase = anterior }, aliado, new { Modulo = "PortalAliados", Entidad = "Aliado" });
        return (true, "Porcentaje actualizado correctamente.");
    }

    public async Task<AliadoComisionesDto?> ObtenerComisionesAsync(int userId)
    {
        var contexto = await ObtenerContextoAsync(userId);
        if (contexto is null)
            return null;

        await SincronizarComisionesAsync(contexto.IdVendedor);
        await using var db = await _dbFactory.CreateDbContextAsync();
        var movimientos = await db.AliadoComisiones
            .AsNoTracking()
            .Where(x => x.IdVendedor == contexto.IdVendedor)
            .OrderByDescending(x => x.FechaGeneracion)
            .Select(x => new AliadoComisionMovimientoDto
            {
                IdComision = x.IdComision,
                IdFactura = x.IdFactura,
                Cliente = db.Clientes
                    .Where(c => c.Codcliente == x.IdCliente)
                    .Select(c => c.Nombrerazonsocial ?? c.Nombrecomercial ?? ((c.Nombres ?? "") + " " + (c.Apellidos ?? "")))
                    .FirstOrDefault() ?? "Cliente",
                Producto = db.Detallefacturas
                    .Where(d => d.Codfactura == x.IdFactura)
                    .OrderBy(d => d.Codlinea)
                    .Select(d => d.Descripproducto)
                    .FirstOrDefault() ?? "Servicio Numerica",
                Tipo = x.TipoComision,
                BaseComisionable = x.BaseComisionable,
                Porcentaje = x.Porcentaje,
                ValorComision = x.Valor,
                FechaGeneracion = x.FechaGeneracion,
                Estado = x.Estado,
                IdLiquidacion = x.IdLiquidacion,
                PeriodoLiquidacion = db.AliadoLiquidaciones
                    .Where(l => l.IdLiquidacion == x.IdLiquidacion)
                    .Select(l => l.Periodo)
                    .FirstOrDefault(),
                ReferenciaPago = db.AliadoLiquidaciones
                    .Where(l => l.IdLiquidacion == x.IdLiquidacion)
                    .Select(l => l.ReferenciaPago)
                    .FirstOrDefault()
            })
            .ToListAsync();

        return new AliadoComisionesDto
        {
            PorcentajeBase = contexto.PorcentajeBase,
            Movimientos = movimientos,
            Generadas = movimientos.Where(x => x.Estado is "Generada" or "Aprobada" or "Pagada").Sum(x => x.ValorComision),
            PendientesAprobacion = movimientos.Where(x => x.Estado == "Generada").Sum(x => x.ValorComision),
            Aprobadas = movimientos.Where(x => x.Estado == "Aprobada").Sum(x => x.ValorComision),
            Pagadas = movimientos.Where(x => x.Estado == "Pagada").Sum(x => x.ValorComision),
            Pendientes = movimientos.Count(x => x.Estado is "Generada" or "Aprobada")
        };
    }

    public async Task<IReadOnlyList<AliadoAdminRow>> ListarAliadosAsync()
    {
        await EnsureSchemaAsync();
        await using var db = await _dbFactory.CreateDbContextAsync();
        var idRolAliado = await db.AliadoPortalRoles
            .Where(x => x.Nombre == RoleName && x.Activo)
            .Select(x => (int?)x.IdRol)
            .FirstOrDefaultAsync();
        if (!idRolAliado.HasValue)
            return Array.Empty<AliadoAdminRow>();

        return await db.VendedoresBackOffice
            .AsNoTracking()
            .Where(x => !x.EsSistema && db.Usuarios.Any(u =>
                u.IdVendedor == x.IdVendedor &&
                db.AliadoPortalUsuariosRoles.Any(ur => ur.IdUsuario == u.IdUsuario && ur.IdRol == idRolAliado.Value)))
            .OrderBy(x => x.Nombre)
            .Select(x => new AliadoAdminRow
            {
                IdVendedor = x.IdVendedor,
                Nombre = x.Nombre,
                CodigoReferencia = x.CodigoReferencia,
                Activo = x.Activo,
                PorcentajeBase = x.PorcentajeBase,
                IdUsuario = db.Usuarios.Where(u => u.IdVendedor == x.IdVendedor && db.AliadoPortalUsuariosRoles.Any(ur => ur.IdUsuario == u.IdUsuario && ur.IdRol == idRolAliado.Value)).Select(u => (int?)u.IdUsuario).FirstOrDefault(),
                Usuario = db.Usuarios.Where(u => u.IdVendedor == x.IdVendedor && db.AliadoPortalUsuariosRoles.Any(ur => ur.IdUsuario == u.IdUsuario && ur.IdRol == idRolAliado.Value)).Select(u => u.Email).FirstOrDefault(),
                UsuarioActivo = db.Usuarios.Where(u => u.IdVendedor == x.IdVendedor && db.AliadoPortalUsuariosRoles.Any(ur => ur.IdUsuario == u.IdUsuario && ur.IdRol == idRolAliado.Value)).Select(u => u.Estado ?? false).FirstOrDefault()
            })
            .ToListAsync();
    }

    public async Task<IReadOnlyList<AliadoUsuarioAdminRow>> ListarUsuariosAliadosAsync(int actorId)
    {
        await EnsureSchemaAsync();
        await using var db = await _dbFactory.CreateDbContextAsync();
        if (!await EsAdministradorInternoAsync(db, actorId))
            return Array.Empty<AliadoUsuarioAdminRow>();

        return await db.Usuarios.AsNoTracking()
            .Where(u => db.AliadoPortalUsuariosRoles.Any(ur => ur.IdUsuario == u.IdUsuario))
            .OrderBy(u => u.Nombres).ThenBy(u => u.Apellidos)
            .Select(u => new AliadoUsuarioAdminRow
            {
                IdUsuario = u.IdUsuario,
                Nombre = ((u.Nombres ?? string.Empty) + " " + (u.Apellidos ?? string.Empty)).Trim(),
                Email = u.Email,
                AvatarUrl = u.AvatarUrl,
                Activo = u.Estado ?? false,
                Bloqueado = u.CuentaBloqueada ?? false,
                FechaCreacion = u.FechaCreacion,
                Rol = db.AliadoPortalRoles.Where(r => db.AliadoPortalUsuariosRoles.Any(ur => ur.IdUsuario == u.IdUsuario && ur.IdRol == r.IdRol)).Select(r => r.Nombre).FirstOrDefault() ?? "Sin rol",
                Aliado = db.VendedoresBackOffice.Where(v => v.IdVendedor == u.IdVendedor).Select(v => v.Nombre).FirstOrDefault(),
                CodigoReferencia = db.VendedoresBackOffice.Where(v => v.IdVendedor == u.IdVendedor).Select(v => v.CodigoReferencia).FirstOrDefault()
            })
            .ToListAsync();
    }

    public async Task<(bool Success, string Message)> ActualizarEstadoUsuarioAliadoAsync(int actorId, int idUsuario, bool activo)
    {
        await EnsureSchemaAsync();
        await using var db = await _dbFactory.CreateDbContextAsync();
        if (!await EsAdministradorInternoAsync(db, actorId)) return (false, "No tienes permisos para modificar esta cuenta.");
        if (actorId == idUsuario && !activo) return (false, "No puedes desactivar tu propia cuenta.");
        var usuario = await db.Usuarios.FirstOrDefaultAsync(u => u.IdUsuario == idUsuario && db.AliadoPortalUsuariosRoles.Any(ur => ur.IdUsuario == u.IdUsuario));
        if (usuario is null) return (false, "La cuenta de aliado no existe.");
        usuario.Estado = activo;
        if (activo) usuario.CuentaBloqueada = false;
        await db.SaveChangesAsync();
        await _auditService.TryRegistrarAuditoriaAsync(actorId, "MODIFICAR", new { idUsuario }, new { Activo = activo }, new { Modulo = "PortalAliados", Entidad = "Usuario" });
        return (true, activo ? "Usuario activado correctamente." : "Usuario desactivado correctamente.");
    }

    public async Task<(bool Success, string Message)> ActualizarUsuarioAliadoAsync(int actorId, int idUsuario, string nombre, string email)
    {
        await EnsureSchemaAsync();
        nombre = nombre.Trim(); email = email.Trim();
        if (string.IsNullOrWhiteSpace(nombre) || !MailAddress.TryCreate(email, out _)) return (false, "Ingresa un nombre y correo válidos.");
        await using var db = await _dbFactory.CreateDbContextAsync();
        if (!await EsAdministradorInternoAsync(db, actorId)) return (false, "No tienes permisos para modificar esta cuenta.");
        var usuario = await db.Usuarios.FirstOrDefaultAsync(u => u.IdUsuario == idUsuario && db.AliadoPortalUsuariosRoles.Any(ur => ur.IdUsuario == u.IdUsuario));
        if (usuario is null) return (false, "La cuenta de aliado no existe.");
        if (await db.Usuarios.AnyAsync(u => u.IdUsuario != idUsuario && u.Email.ToLower() == email.ToLower())) return (false, "Ya existe una cuenta con ese correo.");
        usuario.Nombres = nombre; usuario.Apellidos = string.Empty; usuario.Email = email;
        var aliado = usuario.IdVendedor.HasValue ? await db.VendedoresBackOffice.FirstOrDefaultAsync(v => v.IdVendedor == usuario.IdVendedor) : null;
        if (aliado is not null) aliado.Nombre = nombre;
        await db.SaveChangesAsync();
        await _auditService.TryRegistrarAuditoriaAsync(actorId, "MODIFICAR", new { idUsuario }, new { Nombre = nombre, Email = email }, new { Modulo = "PortalAliados", Entidad = "Usuario" });
        return (true, "Usuario actualizado correctamente.");
    }

    public async Task<IReadOnlyList<AliadoVendedorDisponible>> ListarVendedoresDisponiblesComoAliadosAsync()
    {
        await EnsureSchemaAsync();
        await using var db = await _dbFactory.CreateDbContextAsync();
        var idRolAliado = await db.AliadoPortalRoles
            .Where(x => x.Nombre == RoleName && x.Activo)
            .Select(x => (int?)x.IdRol)
            .FirstOrDefaultAsync();
        if (!idRolAliado.HasValue)
            return Array.Empty<AliadoVendedorDisponible>();

        var vendedores = await db.VendedoresBackOffice
            .AsNoTracking()
            .Where(x => !x.EsSistema && x.Activo)
            .ToListAsync();
        var usuarios = await db.Usuarios
            .AsNoTracking()
            .Where(x => x.Estado == true && x.IdTipoUsuario == BackOfficePermissionHelper.BackOfficeRoleId &&
                (x.TipoCliente == 1 || x.TipoCliente == 2 || x.TipoCliente == 3) &&
                !db.AliadoPortalUsuariosRoles.Any(ur => ur.IdUsuario == x.IdUsuario && ur.IdRol == idRolAliado.Value))
            .Select(x => new { x.IdUsuario, x.IdVendedor, x.Nombres, x.Apellidos, x.Email })
            .ToListAsync();

        return usuarios
            .Select(usuario =>
            {
                var vendedor = vendedores.FirstOrDefault(x => x.IdVendedor == usuario.IdVendedor) ??
                               vendedores.FirstOrDefault(x => x.CodigoReferencia == $"usr_{usuario.IdUsuario}");
                return vendedor is null
                    ? null
                    : new AliadoVendedorDisponible
                    {
                        IdVendedor = vendedor.IdVendedor,
                        IdUsuario = usuario.IdUsuario,
                        Nombre = vendedor.Nombre,
                        Email = usuario.Email
                    };
            })
            .Where(x => x is not null)
            .Cast<AliadoVendedorDisponible>()
            .OrderBy(x => x.Nombre)
            .ToList();
    }

    public async Task<(bool Success, string Message)> AsociarVendedorComoAliadoAsync(int actorId, int idVendedor)
    {
        await EnsureSchemaAsync();
        await using var db = await _dbFactory.CreateDbContextAsync();
        if (!await EsAdministradorInternoAsync(db, actorId))
            return (false, "No tienes permisos para asociar vendedores como aliados.");

        var vendedor = await db.VendedoresBackOffice.FirstOrDefaultAsync(x => x.IdVendedor == idVendedor && !x.EsSistema && x.Activo);
        if (vendedor is null)
            return (false, "El vendedor no existe o está inactivo.");

        var usuario = await db.Usuarios.FirstOrDefaultAsync(x =>
            x.Estado == true &&
            x.IdTipoUsuario == BackOfficePermissionHelper.BackOfficeRoleId &&
            (x.TipoCliente == 1 || x.TipoCliente == 2 || x.TipoCliente == 3) &&
            x.IdVendedor == idVendedor);

        if (usuario is null &&
            vendedor.CodigoReferencia.StartsWith("usr_", StringComparison.OrdinalIgnoreCase) &&
            int.TryParse(vendedor.CodigoReferencia[4..], out var idUsuarioPorCodigo))
        {
            usuario = await db.Usuarios.FirstOrDefaultAsync(x =>
                x.Estado == true &&
                x.IdTipoUsuario == BackOfficePermissionHelper.BackOfficeRoleId &&
                (x.TipoCliente == 1 || x.TipoCliente == 2 || x.TipoCliente == 3) &&
                x.IdUsuario == idUsuarioPorCodigo);
        }

        if (usuario is null)
            return (false, "El vendedor no tiene una cuenta activa asociable.");

        var idRolAliado = await db.AliadoPortalRoles
            .Where(x => x.Nombre == RoleName && x.Activo)
            .Select(x => (int?)x.IdRol)
            .FirstOrDefaultAsync();
        if (!idRolAliado.HasValue)
            return (false, "No se encontró el rol del Portal de Aliados.");

        if (await db.AliadoPortalUsuariosRoles.AnyAsync(x => x.IdUsuario == usuario.IdUsuario))
            return (false, "El vendedor ya tiene una asociación en el Portal de Aliados.");

        usuario.IdVendedor = vendedor.IdVendedor;
        db.AliadoPortalUsuariosRoles.Add(new AliadoPortalUsuarioRol
        {
            IdUsuario = usuario.IdUsuario,
            IdRol = idRolAliado.Value
        });
        await db.SaveChangesAsync();
        await _auditService.TryRegistrarAuditoriaAsync(actorId, "ASOCIAR", null,
            new { usuario.IdUsuario, vendedor.IdVendedor },
            new { Modulo = "PortalAliados", Entidad = "AsociacionVendedorAliado" });
        return (true, $"{vendedor.Nombre} fue añadido como aliado sin cambiar su tipo de vendedor.");
    }

    public async Task<(bool Success, string Message)> ActualizarEstadoAliadoAsync(int actorId, int idVendedor, bool activo)
    {
        await EnsureSchemaAsync();
        await using var db = await _dbFactory.CreateDbContextAsync();
        if (!await EsAdministradorPortalAsync(db, actorId))
            return (false, "No tienes permisos para cambiar el estado del aliado.");

        var aliado = await db.VendedoresBackOffice.FirstOrDefaultAsync(x => x.IdVendedor == idVendedor && !x.EsSistema);
        var idRolAliado = await db.AliadoPortalRoles
            .Where(x => x.Nombre == RoleName && x.Activo)
            .Select(x => (int?)x.IdRol)
            .FirstOrDefaultAsync();
        var idTipoAliado = await db.TipoUsuario
            .Where(x => x.NombreTipo == RoleName && x.Estado == true)
            .Select(x => (int?)x.IdTipoUsuario)
            .FirstOrDefaultAsync();
        var usuarios = await db.Usuarios
            .Where(x => x.IdVendedor == idVendedor &&
                (x.IdTipoUsuario == idTipoAliado ||
                 x.IdTipoUsuario == BackOfficePermissionHelper.BackOfficeRoleId ||
                 (idRolAliado.HasValue && db.AliadoPortalUsuariosRoles.Any(ur => ur.IdUsuario == x.IdUsuario && ur.IdRol == idRolAliado.Value))))
            .ToListAsync();
        if (aliado is null || usuarios.Count == 0)
            return (false, "El aliado no existe o no tiene una cuenta comercial.");

        aliado.Activo = activo;
        foreach (var usuario in usuarios)
        {
            if (usuario.IdTipoUsuario == idTipoAliado)
            {
                usuario.Estado = activo;
                continue;
            }

            if (activo && idRolAliado.HasValue && !await db.AliadoPortalUsuariosRoles
                    .AnyAsync(x => x.IdUsuario == usuario.IdUsuario && x.IdRol == idRolAliado.Value))
            {
                db.AliadoPortalUsuariosRoles.Add(new AliadoPortalUsuarioRol { IdUsuario = usuario.IdUsuario, IdRol = idRolAliado.Value });
            }
        }

        await db.SaveChangesAsync();
        await _auditService.TryRegistrarAuditoriaAsync(actorId, "MODIFICAR", new { IdVendedor = idVendedor }, new { Activo = activo }, new { Modulo = "PortalAliados", Entidad = "EstadoAliado" });
        return (true, activo ? "Aliado activado correctamente." : "Aliado desactivado correctamente.");
    }

    public async Task<(bool Success, string Message)> CrearCuentaAliadoAsync(int actorId, string nombre, string email, string password, decimal porcentajeBase = 30m, bool esAdministradorPortal = false)
    {
        await EnsureSchemaAsync();
        nombre = nombre.Trim();
        email = email.Trim();
        password = password.Trim();
        if (string.IsNullOrWhiteSpace(nombre) || string.IsNullOrWhiteSpace(email) || password.Length < 8)
            return (false, "Nombre, correo y una clave de al menos 8 caracteres son obligatorios.");
        if (nombre.Length > 120 || email.Length > 254 || !MailAddress.TryCreate(email, out _))
            return (false, "El nombre o el correo no tienen un formato válido.");
        if (!TryNormalizarPorcentaje(porcentajeBase, out porcentajeBase))
            return (false, "El porcentaje debe estar entre 0 y 100.");

        await using var db = await _dbFactory.CreateDbContextAsync();
        var actor = await db.Usuarios
            .AsNoTracking()
            .Where(x => x.IdUsuario == actorId && x.Estado == true)
            .Select(x => new { x.IdTipoUsuario, Tipo = x.IdTipoUsuarioNavigation!.NombreTipo })
            .FirstOrDefaultAsync();
        var actorEsSuperAdministrador = actor?.IdTipoUsuario == BackOfficePermissionHelper.SuperAdministradorRoleId;
        var actorEsAdministradorPortal = await EsAdministradorInternoAsync(db, actorId);
        if (!actorEsAdministradorPortal ||
            (esAdministradorPortal && !actorEsSuperAdministrador))
            return (false, "No tienes permisos para crear este tipo de cuenta.");

        if (await db.Usuarios.AnyAsync(x => x.Email.ToLower() == email.ToLower()))
            return (false, "Ya existe una cuenta con ese correo.");

        var roleName = esAdministradorPortal ? AdminRoleName : RoleName;
        var roleId = await db.TipoUsuario
            .Where(x => x.NombreTipo == roleName && x.Estado == true)
            .Select(x => (int?)x.IdTipoUsuario)
            .FirstOrDefaultAsync();
        if (!roleId.HasValue)
            return (false, $"No se encontró el rol {roleName}.");

        await using var transaction = await db.Database.BeginTransactionAsync();
        VendedorBackOffice? aliado = null;
        if (!esAdministradorPortal)
        {
            var codigo = await GenerarCodigoAsync(db, nombre);
            aliado = new VendedorBackOffice
            {
                Nombre = nombre,
                CodigoReferencia = codigo,
                Activo = true,
                EsSistema = false,
                PorcentajeBase = porcentajeBase,
                IdUsuarioCreacion = actorId > 0 ? actorId : null,
                FechaCreacion = DateTime.Now
            };
            db.VendedoresBackOffice.Add(aliado);
            await db.SaveChangesAsync();
        }

        var nuevoUsuario = new Usuario
        {
            Nombres = nombre,
            Apellidos = string.Empty,
            Email = email,
            PasswordHash = SecurityHelper.HashPassword(password),
            IdTipoUsuario = roleId,
            IdVendedor = aliado?.IdVendedor,
            Estado = true,
            ClaveTemporal = true,
            CuentaBloqueada = false,
            FechaCreacion = DateTime.Now,
            estadoAsociado = true
        };
        db.Usuarios.Add(nuevoUsuario);
        await db.SaveChangesAsync();
        var idRolPortal = await db.AliadoPortalRoles
            .Where(x => x.Nombre == roleName && x.Activo)
            .Select(x => x.IdRol)
            .SingleAsync();
        db.AliadoPortalUsuariosRoles.Add(new AliadoPortalUsuarioRol { IdUsuario = nuevoUsuario.IdUsuario, IdRol = idRolPortal });
        await db.SaveChangesAsync();
        await transaction.CommitAsync();
        await _auditService.TryRegistrarAuditoriaAsync(actorId, "CREAR", null, new { nuevoUsuario.IdUsuario, aliado?.IdVendedor, TipoCuenta = roleName }, new { Modulo = "PortalAliados", Entidad = "Cuenta" });
        return (true, esAdministradorPortal
            ? "Cuenta creada correctamente con el rol Administrador Portal de Aliados."
            : "Cuenta de aliado creada correctamente con el rol Aliado Comercial.");
    }

    private async Task<List<FacturaPortalRow>> ObtenerFacturasAsync(int idVendedor, int? idCliente = null)
    {
        await using var db = await _dbFactory.CreateDbContextAsync();
        return await db.Facturas
            .AsNoTracking()
            .Where(x => x.Idvendedor == idVendedor && (!idCliente.HasValue || x.Codclientes == idCliente) && (x.Estado == true || x.Estado == null))
            .OrderByDescending(x => x.Fchautorizacion ?? x.Fechaentrega)
            .Select(x => new FacturaPortalRow
            {
                IdFactura = x.Codfactura,
                IdCliente = x.Codclientes,
                Cliente = x.CodclientesNavigation == null ? "Cliente" : (x.CodclientesNavigation.Nombrerazonsocial ?? x.CodclientesNavigation.Nombrecomercial ?? ((x.CodclientesNavigation.Nombres ?? "") + " " + (x.CodclientesNavigation.Apellidos ?? ""))),
                Producto = x.Detallefacturas.OrderBy(d => d.Codlinea).Select(d => d.Descripproducto).FirstOrDefault() ?? "Servicio Numerica",
                Plan = x.Detallefacturas.OrderBy(d => d.Codlinea).Select(d => d.Descripproducto).FirstOrDefault() ?? "Servicio",
                Fecha = x.Fchautorizacion ?? x.Fechaentrega ?? DateTime.MinValue,
                FechaVencimiento = x.Fechavence,
                Total = x.Valortotal ?? x.Subtotal ?? 0m,
                Subtotal = x.Subtotal,
                Subtotal0 = x.Subtotal0,
                Subtotal12 = x.Subtotal12,
                Comision = x.Comision,
                Autorizado = x.Autorizado == true,
                EstadoPago = x.Estadopago
            })
            .ToListAsync();
    }

    private static AliadoRenovacionDto ToRenovacion(FacturaPortalRow factura, AliadoRenovacionGestion? gestion, decimal porcentajeBase) => new()
    {
        IdFactura = factura.IdFactura,
        IdCliente = factura.IdCliente ?? 0,
        Cliente = factura.Cliente,
        Producto = factura.Producto,
        FechaVencimiento = factura.FechaVencimiento!.Value,
        DiasRestantes = (factura.FechaVencimiento.Value.Date - DateTime.Today).Days,
        Valor = factura.Total,
        ComisionPotencial = decimal.Round(ObtenerBaseNeta(factura.Subtotal, factura.Subtotal0, factura.Subtotal12) * porcentajeBase / 100m, 2, MidpointRounding.AwayFromZero),
        EstadoGestion = gestion?.Resultado ?? "Pendiente",
        UltimaGestion = gestion?.FechaGestion,
        Observacion = gestion?.Observacion
    };

    private static AliadoComisionMovimientoDto CalcularComision(FacturaPortalRow factura, decimal porcentajeBase)
    {
        var baseComisionable = ObtenerBaseNeta(factura.Subtotal, factura.Subtotal0, factura.Subtotal12);
        var valor = factura.Comision is > 0
            ? factura.Comision.Value
            : decimal.Round(baseComisionable * porcentajeBase / 100m, 2, MidpointRounding.AwayFromZero);
        return new AliadoComisionMovimientoDto
        {
            IdFactura = factura.IdFactura,
            Cliente = factura.Cliente,
            Producto = factura.Producto,
            Tipo = factura.FechaVencimiento.HasValue ? "Renovación" : "Venta nueva",
            BaseComisionable = baseComisionable,
            Porcentaje = porcentajeBase,
            ValorComision = valor,
            FechaGeneracion = factura.Fecha,
            Estado = "Generada"
        };
    }

    public async Task<IReadOnlyList<AliadoLiquidacionDto>> ObtenerLiquidacionesAsync(int userId)
    {
        var contexto = await ObtenerContextoAsync(userId);
        if (contexto is null || contexto.EsAdministrador)
            return Array.Empty<AliadoLiquidacionDto>();

        await using var db = await _dbFactory.CreateDbContextAsync();
        return await db.AliadoLiquidaciones.AsNoTracking()
            .Where(x => x.IdVendedor == contexto.IdVendedor)
            .OrderByDescending(x => x.Fecha)
            .Select(x => new AliadoLiquidacionDto
            {
                IdLiquidacion = x.IdLiquidacion,
                Periodo = x.Periodo,
                Total = x.Total,
                Fecha = x.Fecha,
                Estado = x.Estado,
                ReferenciaPago = x.ReferenciaPago
            })
            .ToListAsync();
    }

    public async Task SincronizarComisionesPortalAsync()
    {
        await EnsureSchemaAsync();
        await using var db = await _dbFactory.CreateDbContextAsync();
        var vendedorIds = await db.VendedoresBackOffice.AsNoTracking()
            .Where(x => !x.EsSistema && x.Activo && db.Usuarios.Any(u =>
                u.IdVendedor == x.IdVendedor && u.Estado == true &&
                db.AliadoPortalUsuariosRoles.Any(ur => ur.IdUsuario == u.IdUsuario &&
                    db.AliadoPortalRoles.Any(r => r.IdRol == ur.IdRol && r.Nombre == RoleName))))
            .Select(x => x.IdVendedor)
            .ToListAsync();

        foreach (var idVendedor in vendedorIds)
            await SincronizarComisionesAsync(idVendedor);
    }

    public async Task<IReadOnlyList<AliadoRenovacionNotificacionDto>> ObtenerNotificacionesRenovacionAsync()
    {
        await EnsureSchemaAsync();
        await using var db = await _dbFactory.CreateDbContextAsync();
        var aliados = await db.Usuarios.AsNoTracking()
            .Where(x => x.Estado == true && x.IdVendedor.HasValue &&
                db.AliadoPortalUsuariosRoles.Any(ur => ur.IdUsuario == x.IdUsuario &&
                    db.AliadoPortalRoles.Any(r => r.IdRol == ur.IdRol && r.Nombre == RoleName)))
            .Select(x => new { IdVendedor = x.IdVendedor!.Value, x.Email, Nombre = (x.Nombres ?? "") + " " + (x.Apellidos ?? "") })
            .ToListAsync();
        var hoy = DateTime.Today;
        var limite = hoy.AddDays(7);
        var resultado = new List<AliadoRenovacionNotificacionDto>();

        foreach (var aliado in aliados)
        {
            var yaNotificadas = await db.AliadoRenovacionNotificaciones.AsNoTracking()
                .Where(x => x.IdVendedor == aliado.IdVendedor)
                .Select(x => new { x.IdFactura, x.Tipo })
                .ToHashSetAsync();
            var facturas = await ObtenerFacturasAsync(aliado.IdVendedor);
            foreach (var factura in facturas.Where(x => x.FechaVencimiento.HasValue && x.FechaVencimiento.Value.Date >= hoy && x.FechaVencimiento.Value.Date <= limite && x.Autorizado))
            {
                var dias = (factura.FechaVencimiento!.Value.Date - hoy).Days;
                var tipo = dias <= 1 ? "Urgente" : "Proxima";
                if (yaNotificadas.Contains(new { factura.IdFactura, Tipo = tipo }))
                    continue;
                resultado.Add(new AliadoRenovacionNotificacionDto
                {
                    IdVendedor = aliado.IdVendedor,
                    IdFactura = factura.IdFactura,
                    Email = aliado.Email,
                    NombreAliado = aliado.Nombre.Trim(),
                    Cliente = factura.Cliente,
                    Producto = factura.Producto,
                    FechaVencimiento = factura.FechaVencimiento.Value,
                    DiasRestantes = dias,
                    Tipo = tipo
                });
            }
        }

        return resultado;
    }

    public async Task RegistrarNotificacionRenovacionAsync(int idVendedor, int idFactura, string tipo)
    {
        await EnsureSchemaAsync();
        await using var db = await _dbFactory.CreateDbContextAsync();
        if (await db.AliadoRenovacionNotificaciones.AnyAsync(x => x.IdVendedor == idVendedor && x.IdFactura == idFactura && x.Tipo == tipo))
            return;
        db.AliadoRenovacionNotificaciones.Add(new AliadoRenovacionNotificacion
        {
            IdVendedor = idVendedor,
            IdFactura = idFactura,
            Tipo = tipo,
            FechaEnvio = DateTime.Now
        });
        await db.SaveChangesAsync();
    }

    private async Task SincronizarComisionesAsync(int idVendedor)
    {
        if (idVendedor <= 0)
            return;

        await EnsureSchemaAsync();
        await using var db = await _dbFactory.CreateDbContextAsync();
        var aliado = await db.VendedoresBackOffice.AsNoTracking()
            .Where(x => x.IdVendedor == idVendedor && !x.EsSistema && x.Activo)
            .Select(x => new { x.IdVendedor, x.PorcentajeBase })
            .FirstOrDefaultAsync();
        if (aliado is null)
            return;

        var configuracion = await db.AliadoPortalConfiguraciones.AsNoTracking()
            .SingleOrDefaultAsync(x => x.IdConfiguracion == 1)
            ?? new AliadoPortalConfiguracion();
        var facturas = await db.Facturas.AsNoTracking()
            .Where(x => x.Idvendedor == idVendedor &&
                        (x.Estado == true || x.Estado == null) &&
                        x.Notas != null && x.Notas.Contains(BackOfficeInvoiceMarker))
            .Select(x => new FacturaComisionRow
            {
                IdFactura = x.Codfactura,
                IdCliente = x.Codclientes,
                FechaVencimiento = x.Fechavence,
                Subtotal = x.Subtotal,
                Subtotal0 = x.Subtotal0,
                Subtotal12 = x.Subtotal12,
                Autorizado = x.Autorizado == true,
                EstadoPago = x.Estadopago
            })
            .ToListAsync();
        var existentes = await db.AliadoComisiones.AsNoTracking()
            .Where(x => x.IdVendedor == idVendedor)
            .Select(x => new { x.IdFactura, x.TipoComision })
            .ToHashSetAsync();
        var gestiones = await db.AliadoRenovacionGestiones.AsNoTracking()
            .Where(x => x.IdVendedor == idVendedor)
            .GroupBy(x => x.IdFactura)
            .Select(x => x.OrderByDescending(y => y.FechaGestion).First())
            .ToDictionaryAsync(x => x.IdFactura);
        var nuevas = new List<AliadoComision>();

        foreach (var factura in facturas)
        {
            if (!factura.Autorizado || !EsPagoConfirmado(factura.EstadoPago))
                continue;

            var baseComisionable = ObtenerBaseNeta(factura.Subtotal, factura.Subtotal0, factura.Subtotal12);
            if (baseComisionable <= 0)
                continue;

            gestiones.TryGetValue(factura.IdFactura, out var gestion);
            var esRenovacion = factura.FechaVencimiento.HasValue;
            var gestionAliadoEfectiva = gestion?.OrigenGestion != "Numerica" &&
                                         gestion?.Resultado is not null &&
                                         gestion.Resultado is not "Pendiente" and not "No responde";
            var intervieneNumerica = esRenovacion &&
                                     factura.FechaVencimiento!.Value.Date <= DateTime.Today.AddDays(configuracion.DiasIntervencionNumerica) &&
                                     !gestionAliadoEfectiva;
            var tipo = !esRenovacion
                ? "VentaNueva"
                : intervieneNumerica || string.Equals(gestion?.OrigenGestion, "Numerica", StringComparison.OrdinalIgnoreCase)
                    ? "RenovacionNumerica"
                    : "RenovacionAliado";
            if (existentes.Contains(new { factura.IdFactura, TipoComision = tipo }))
                continue;

            var porcentaje = tipo switch
            {
                "RenovacionNumerica" => configuracion.PorcentajeRenovacionNumerica,
                "RenovacionAliado" => aliado.PorcentajeBase > 0m
                    ? aliado.PorcentajeBase
                    : configuracion.PorcentajeRenovacionAliado,
                _ => aliado.PorcentajeBase > 0m
                    ? aliado.PorcentajeBase
                    : configuracion.PorcentajeVentaNueva
            };
            nuevas.Add(new AliadoComision
            {
                IdVendedor = idVendedor,
                IdFactura = factura.IdFactura,
                IdCliente = factura.IdCliente,
                TipoComision = tipo,
                BaseComisionable = baseComisionable,
                Porcentaje = porcentaje,
                Valor = decimal.Round(baseComisionable * porcentaje / 100m, 2, MidpointRounding.AwayFromZero),
                Estado = "Generada",
                FechaGeneracion = DateTime.Now
            });
        }

        if (nuevas.Count > 0)
        {
            db.AliadoComisiones.AddRange(nuevas);
            await db.SaveChangesAsync();
        }
    }

    private async Task<AliadoComisionResumen> ObtenerResumenComisionesAsync(int idVendedor)
    {
        await using var db = await _dbFactory.CreateDbContextAsync();
        var movimientos = await db.AliadoComisiones.AsNoTracking()
            .Where(x => x.IdVendedor == idVendedor)
            .Select(x => new { x.Estado, x.Valor })
            .ToListAsync();
        return new AliadoComisionResumen
        {
            Generadas = movimientos.Where(x => x.Estado is "Generada" or "Aprobada" or "Pagada").Sum(x => x.Valor),
            PendientesAprobacion = movimientos.Where(x => x.Estado == "Generada").Sum(x => x.Valor),
            Aprobadas = movimientos.Where(x => x.Estado == "Aprobada").Sum(x => x.Valor),
            Pagadas = movimientos.Where(x => x.Estado == "Pagada").Sum(x => x.Valor)
        };
    }

    private static async Task<bool> EsAdministradorInternoAsync(AppDbContext db, int actorId)
    {
        var actor = await db.Usuarios.AsNoTracking()
            .Where(x => x.IdUsuario == actorId && x.Estado == true)
            .Select(x => new { x.IdTipoUsuario, Tipo = x.IdTipoUsuarioNavigation!.NombreTipo })
            .FirstOrDefaultAsync();
        return actor?.IdTipoUsuario is BackOfficePermissionHelper.SuperAdministradorRoleId or BackOfficePermissionHelper.BackOfficeRoleId ||
               await db.AliadoPortalUsuariosRoles.AnyAsync(ur =>
            ur.IdUsuario == actorId &&
            db.AliadoPortalRoles.Any(r =>
                r.IdRol == ur.IdRol && r.Activo && r.Nombre == AdminRoleName));
    }

    private static async Task<bool> EsAdministradorPortalAsync(AppDbContext db, int actorId)
    {
        var actor = await db.Usuarios.AsNoTracking()
            .Where(x => x.IdUsuario == actorId && x.Estado == true)
            .Select(x => new { x.IdTipoUsuario })
            .FirstOrDefaultAsync();
        if (actor?.IdTipoUsuario == BackOfficePermissionHelper.SuperAdministradorRoleId)
            return true;

        return await db.AliadoPortalUsuariosRoles.AnyAsync(ur =>
            ur.IdUsuario == actorId &&
            db.AliadoPortalRoles.Any(r =>
                r.IdRol == ur.IdRol && r.Activo && r.Nombre == AdminRoleName));
    }

    private static bool EsPagoConfirmado(FacturaPortalRow factura)
        => EsPagoConfirmado(factura.EstadoPago);

    private static bool TryNormalizarPorcentaje(decimal valor, out decimal normalizado)
    {
        if (valor is < 0 or > 100)
        {
            normalizado = valor;
            return false;
        }

        normalizado = decimal.Round(valor, 2, MidpointRounding.AwayFromZero);
        return true;
    }

    private static bool EsPagoConfirmado(string? estadoPago)
        => estadoPago?.Trim().ToUpperInvariant() is "PAGADA" or "PAGADO" or "CANCELADA" or "CANCELADO" or "COBRADA" or "COBRADO";

    private static decimal ObtenerBaseNeta(decimal? subtotal, decimal? subtotal0, decimal? subtotal12)
        => subtotal ?? ((subtotal0 ?? 0m) + (subtotal12 ?? 0m));

    private static async Task<string> GenerarCodigoAsync(AppDbContext db, string nombre)
    {
        var baseCodigo = new string(nombre.ToUpperInvariant().Where(char.IsLetterOrDigit).ToArray());
        baseCodigo = string.IsNullOrWhiteSpace(baseCodigo) ? "ALIADO" : baseCodigo[..Math.Min(baseCodigo.Length, 18)];
        var codigo = baseCodigo;
        var suffix = 1;
        while (await db.VendedoresBackOffice.AnyAsync(x => x.CodigoReferencia == codigo))
            codigo = $"{baseCodigo}{++suffix}";
        return codigo;
    }

    private static IEnumerable<string> BuildEnsureSchemaStatements()
    {
        yield return $"""
IF OBJECT_ID(N'dbo.ALIADO_PORTAL_ROL', N'U') IS NULL CREATE TABLE dbo.ALIADO_PORTAL_ROL (IdRol INT IDENTITY(1,1) NOT NULL PRIMARY KEY, Nombre NVARCHAR(80) NOT NULL UNIQUE, Descripcion NVARCHAR(250) NULL, Activo BIT NOT NULL DEFAULT(1));
IF OBJECT_ID(N'dbo.ALIADO_PORTAL_MENU', N'U') IS NULL CREATE TABLE dbo.ALIADO_PORTAL_MENU (IdMenu INT IDENTITY(1,1) NOT NULL PRIMARY KEY, Nombre NVARCHAR(100) NOT NULL, Ruta NVARCHAR(200) NOT NULL UNIQUE, Icono NVARCHAR(50) NULL, Orden INT NOT NULL, Activo BIT NOT NULL DEFAULT(1));
IF OBJECT_ID(N'dbo.ALIADO_PORTAL_ROL_MENU', N'U') IS NULL CREATE TABLE dbo.ALIADO_PORTAL_ROL_MENU (IdRolMenu INT IDENTITY(1,1) NOT NULL PRIMARY KEY, IdRol INT NOT NULL, IdMenu INT NOT NULL, CONSTRAINT UX_ALIADO_PORTAL_ROL_MENU UNIQUE(IdRol, IdMenu));
IF OBJECT_ID(N'dbo.ALIADO_PORTAL_USUARIO_ROL', N'U') IS NULL CREATE TABLE dbo.ALIADO_PORTAL_USUARIO_ROL (IdUsuarioRol INT IDENTITY(1,1) NOT NULL PRIMARY KEY, IdUsuario INT NOT NULL UNIQUE, IdRol INT NOT NULL);
MERGE dbo.ALIADO_PORTAL_ROL AS t USING (VALUES (N'{RoleName}', N'Acceso comercial externo.'), (N'{AdminRoleName}', N'Administración interna del portal.')) s(Nombre,Descripcion) ON t.Nombre=s.Nombre WHEN NOT MATCHED THEN INSERT(Nombre,Descripcion,Activo) VALUES(s.Nombre,s.Descripcion,1);
MERGE dbo.ALIADO_PORTAL_MENU AS t USING (VALUES (N'Inicio',N'{RootRoute}',N'ri-dashboard-3-line',1),(N'Mis clientes',N'{RootRoute}/clientes',N'ri-user-3-line',2),(N'Renovaciones',N'{RootRoute}/renovaciones',N'ri-refresh-line',3),(N'Comisiones',N'{RootRoute}/comisiones',N'ri-hand-coin-line',4),(N'Liquidaciones',N'{RootRoute}/liquidaciones',N'ri-bank-card-line',5),(N'Mi perfil',N'{RootRoute}/perfil',N'ri-user-settings-line',6),(N'Administración de aliados',N'{AdminRoute}',N'ri-admin-line',10),(N'Usuarios',N'{AdminRoute}/usuarios',N'ri-group-line',11)) s(Nombre,Ruta,Icono,Orden) ON t.Ruta=s.Ruta WHEN NOT MATCHED THEN INSERT(Nombre,Ruta,Icono,Orden,Activo) VALUES(s.Nombre,s.Ruta,s.Icono,s.Orden,1);
INSERT dbo.ALIADO_PORTAL_ROL_MENU(IdRol,IdMenu) SELECT r.IdRol,m.IdMenu FROM dbo.ALIADO_PORTAL_ROL r CROSS JOIN dbo.ALIADO_PORTAL_MENU m WHERE ((r.Nombre=N'{RoleName}' AND m.Ruta NOT IN(N'{AdminRoute}',N'{AdminRoute}/usuarios')) OR (r.Nombre=N'{AdminRoleName}' AND m.Ruta IN(N'{AdminRoute}',N'{AdminRoute}/usuarios'))) AND NOT EXISTS(SELECT 1 FROM dbo.ALIADO_PORTAL_ROL_MENU x WHERE x.IdRol=r.IdRol AND x.IdMenu=m.IdMenu);
INSERT dbo.ALIADO_PORTAL_USUARIO_ROL(IdUsuario,IdRol) SELECT u.IdUsuario,r.IdRol FROM dbo.Usuarios u INNER JOIN dbo.TIPOUSUARIO tu ON tu.IdTipoUsuario=u.IdTipoUsuario INNER JOIN dbo.ALIADO_PORTAL_ROL r ON r.Nombre=tu.NombreTipo WHERE tu.NombreTipo IN(N'{RoleName}',N'{AdminRoleName}') AND NOT EXISTS(SELECT 1 FROM dbo.ALIADO_PORTAL_USUARIO_ROL x WHERE x.IdUsuario=u.IdUsuario);
INSERT dbo.ALIADO_PORTAL_USUARIO_ROL(IdUsuario,IdRol) SELECT u.IdUsuario,r.IdRol FROM dbo.Usuarios u CROSS JOIN dbo.ALIADO_PORTAL_ROL r WHERE u.IdTipoUsuario={BackOfficePermissionHelper.SuperAdministradorRoleId} AND r.Nombre=N'{AdminRoleName}' AND NOT EXISTS(SELECT 1 FROM dbo.ALIADO_PORTAL_USUARIO_ROL x WHERE x.IdUsuario=u.IdUsuario);
""";
        yield return $"""
IF NOT EXISTS (SELECT 1 FROM dbo.TIPOUSUARIO WHERE NOMBRETIPO = N'{RoleName}')
BEGIN
    INSERT INTO dbo.TIPOUSUARIO (NOMBRETIPO, DESCRIPCION, ESTADO)
    VALUES (N'{RoleName}', N'Acceso externo al Portal de Aliados Numerica.', 1);
END
IF NOT EXISTS (SELECT 1 FROM dbo.TIPOUSUARIO WHERE NOMBRETIPO = N'{AdminRoleName}')
BEGIN
    INSERT INTO dbo.TIPOUSUARIO (NOMBRETIPO, DESCRIPCION, ESTADO)
    VALUES (N'{AdminRoleName}', N'Administración interna del Portal de Aliados Numerica.', 1);
END
DECLARE @idTipoAliado int = (SELECT TOP 1 IdTipoUsuario FROM dbo.TIPOUSUARIO WHERE NOMBRETIPO = N'{RoleName}' AND ESTADO = 1 ORDER BY IdTipoUsuario);
DECLARE @idTipoAdmin int = (SELECT TOP 1 IdTipoUsuario FROM dbo.TIPOUSUARIO WHERE NOMBRETIPO = N'{AdminRoleName}' AND ESTADO = 1 ORDER BY IdTipoUsuario);
IF NOT EXISTS (SELECT 1 FROM dbo.ROLES WHERE IDTIPOUSUARIO = @idTipoAliado AND ESTADOROL = 1)
    INSERT INTO dbo.ROLES (DESCRIPCIONROL, IDTIPOUSUARIO, ESTADOROL) VALUES (N'{RoleName}', @idTipoAliado, 1);
IF NOT EXISTS (SELECT 1 FROM dbo.ROLES WHERE IDTIPOUSUARIO = @idTipoAdmin AND ESTADOROL = 1)
    INSERT INTO dbo.ROLES (DESCRIPCIONROL, IDTIPOUSUARIO, ESTADOROL) VALUES (N'{AdminRoleName}', @idTipoAdmin, 1);
DECLARE @padre int = (SELECT TOP 1 IDMENU FROM dbo.MENUS WHERE RUTAMENU = N'{RootRoute}' AND ESTADOMENU = 1);
IF @padre IS NULL
BEGIN
    INSERT INTO dbo.MENUS (NOMBREMENU, RUTAMENU, ICONOMENU, IDMENUPADRE, ESTADOMENU, MOSTRAR_EFACT, MOSTRAR_EDECLARA, orden_menu)
    VALUES (N'Portal de Aliados', N'{RootRoute}', N'ri-team-line', 0, 1, 1, 0, 90);
    SET @padre = SCOPE_IDENTITY();
END
""";

        yield return $"""
DECLARE @idRolAliado int = (SELECT TOP 1 r.IDROL FROM dbo.ROLES r INNER JOIN dbo.TIPOUSUARIO t ON t.IdTipoUsuario = r.IDTIPOUSUARIO WHERE t.NOMBRETIPO = N'{RoleName}' AND r.ESTADOROL = 1 ORDER BY r.IDROL);
DECLARE @idRolAdmin int = (SELECT TOP 1 r.IDROL FROM dbo.ROLES r INNER JOIN dbo.TIPOUSUARIO t ON t.IdTipoUsuario = r.IDTIPOUSUARIO WHERE t.NOMBRETIPO = N'{AdminRoleName}' AND r.ESTADOROL = 1 ORDER BY r.IDROL);
DECLARE @padre int = (SELECT TOP 1 IDMENU FROM dbo.MENUS WHERE RUTAMENU = N'{RootRoute}' AND ESTADOMENU = 1);
DECLARE @menus TABLE (Nombre nvarchar(100), Ruta nvarchar(200), Icono nvarchar(50), Orden int);
INSERT INTO @menus VALUES
    (N'Inicio', N'{RootRoute}', N'ri-dashboard-3-line', 1),
    (N'Mis clientes', N'{RootRoute}/clientes', N'ri-user-3-line', 2),
    (N'Renovaciones', N'{RootRoute}/renovaciones', N'ri-refresh-line', 3),
    (N'Comisiones', N'{RootRoute}/comisiones', N'ri-hand-coin-line', 4),
    (N'Liquidaciones', N'{RootRoute}/liquidaciones', N'ri-bank-card-line', 5),
    (N'Mi perfil', N'{RootRoute}/perfil', N'ri-user-settings-line', 6),
    (N'Administración de aliados', N'{AdminRoute}', N'ri-admin-line', 10);
INSERT INTO dbo.MENUS (NOMBREMENU, RUTAMENU, ICONOMENU, IDMENUPADRE, ESTADOMENU, MOSTRAR_EFACT, MOSTRAR_EDECLARA, orden_menu)
SELECT m.Nombre, m.Ruta, m.Icono, CASE WHEN m.Ruta = N'{RootRoute}' THEN 0 ELSE @padre END, 1, 1, 0, m.Orden
FROM @menus m
WHERE NOT EXISTS (SELECT 1 FROM dbo.MENUS existing WHERE existing.RUTAMENU = m.Ruta);
UPDATE dbo.MENUS
SET ESTADOMENU = 1, MOSTRAR_EFACT = 1, MOSTRAR_EDECLARA = 0
WHERE RUTAMENU IN (N'{RootRoute}', N'{RootRoute}/clientes', N'{RootRoute}/renovaciones', N'{RootRoute}/comisiones', N'{RootRoute}/liquidaciones', N'{RootRoute}/perfil', N'{AdminRoute}');
INSERT INTO dbo.ROL_MENU (IDROL, IDMENU)
 SELECT @idRolAliado, m.IDMENU FROM dbo.MENUS m
WHERE m.RUTAMENU IN (N'{RootRoute}', N'{RootRoute}/clientes', N'{RootRoute}/renovaciones', N'{RootRoute}/comisiones', N'{RootRoute}/liquidaciones', N'{RootRoute}/perfil')
   AND NOT EXISTS (SELECT 1 FROM dbo.ROL_MENU rm WHERE rm.IDROL = @idRolAliado AND rm.IDMENU = m.IDMENU);
INSERT INTO dbo.ROL_MENU (IDROL, IDMENU)
SELECT @idRolAdmin, m.IDMENU FROM dbo.MENUS m
WHERE m.RUTAMENU = N'{AdminRoute}'
  AND NOT EXISTS (SELECT 1 FROM dbo.ROL_MENU rm WHERE rm.IDROL = @idRolAdmin AND rm.IDMENU = m.IDMENU);
""";

        yield return """
IF OBJECT_ID(N'dbo.ALIADO_RENOVACION_GESTION', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.ALIADO_RENOVACION_GESTION
    (
        IdGestion INT IDENTITY(1,1) NOT NULL PRIMARY KEY,
        IdVendedor INT NOT NULL,
        IdFactura INT NOT NULL,
        FechaGestion DATETIME2 NOT NULL CONSTRAINT DF_ALIADO_GESTION_FECHA DEFAULT(SYSUTCDATETIME()),
        Resultado NVARCHAR(50) NOT NULL,
        OrigenGestion NVARCHAR(20) NOT NULL CONSTRAINT DF_ALIADO_GESTION_ORIGEN DEFAULT(N'Aliado'),
        Observacion NVARCHAR(500) NULL,
        ProximoSeguimiento DATE NULL,
        IdUsuario INT NOT NULL
    );
END
IF COL_LENGTH('dbo.ALIADO_RENOVACION_GESTION', 'OrigenGestion') IS NULL
    ALTER TABLE dbo.ALIADO_RENOVACION_GESTION ADD OrigenGestion NVARCHAR(20) NOT NULL CONSTRAINT DF_ALIADO_GESTION_ORIGEN DEFAULT(N'Aliado');
""";

        yield return """
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_ALIADO_GESTION_CANAL_FACTURA' AND object_id = OBJECT_ID(N'dbo.ALIADO_RENOVACION_GESTION'))
    CREATE INDEX IX_ALIADO_GESTION_CANAL_FACTURA ON dbo.ALIADO_RENOVACION_GESTION (IdVendedor, IdFactura, FechaGestion DESC);
IF OBJECT_ID(N'dbo.ALIADO_LIQUIDACION', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.ALIADO_LIQUIDACION
    (
        IdLiquidacion INT IDENTITY(1,1) NOT NULL PRIMARY KEY,
        IdVendedor INT NOT NULL,
        Periodo NVARCHAR(20) NOT NULL,
        Total DECIMAL(18,2) NOT NULL,
        Fecha DATETIME2 NOT NULL,
        Estado NVARCHAR(30) NOT NULL,
        ReferenciaPago NVARCHAR(100) NULL
    );
END
""";

        yield return """
IF OBJECT_ID(N'dbo.ALIADO_PORTAL_CONFIG', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.ALIADO_PORTAL_CONFIG
    (
        IdConfiguracion INT NOT NULL PRIMARY KEY,
        PorcentajeVentaNueva DECIMAL(9,4) NOT NULL CONSTRAINT DF_ALIADO_CONFIG_VENTA DEFAULT(30),
        PorcentajeRenovacionAliado DECIMAL(9,4) NOT NULL CONSTRAINT DF_ALIADO_CONFIG_RENOVACION_ALIADO DEFAULT(30),
        PorcentajeRenovacionNumerica DECIMAL(9,4) NOT NULL CONSTRAINT DF_ALIADO_CONFIG_RENOVACION_NUMERICA DEFAULT(15),
        PorcentajeVentaDirecta DECIMAL(9,4) NOT NULL CONSTRAINT DF_ALIADO_CONFIG_VENTA_DIRECTA DEFAULT(0),
        DiasIntervencionNumerica INT NOT NULL CONSTRAINT DF_ALIADO_CONFIG_DIAS DEFAULT(15)
    );
END
IF NOT EXISTS (SELECT 1 FROM dbo.ALIADO_PORTAL_CONFIG WHERE IdConfiguracion = 1)
    INSERT INTO dbo.ALIADO_PORTAL_CONFIG (IdConfiguracion) VALUES (1);
IF OBJECT_ID(N'dbo.ALIADO_COMISION', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.ALIADO_COMISION
    (
        IdComision INT IDENTITY(1,1) NOT NULL PRIMARY KEY,
        IdVendedor INT NOT NULL,
        IdFactura INT NOT NULL,
        IdCliente INT NULL,
        TipoComision NVARCHAR(40) NOT NULL,
        BaseComisionable DECIMAL(18,2) NOT NULL,
        Porcentaje DECIMAL(9,4) NOT NULL,
        Valor DECIMAL(18,2) NOT NULL,
        Estado NVARCHAR(30) NOT NULL CONSTRAINT DF_ALIADO_COMISION_ESTADO DEFAULT(N'Generada'),
        FechaGeneracion DATETIME2 NOT NULL,
        FechaAprobacion DATETIME2 NULL,
        FechaPago DATETIME2 NULL,
        IdLiquidacion INT NULL,
        IdUsuarioAprobacion INT NULL,
        IdUsuarioPago INT NULL
    );
END
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'UX_ALIADO_COMISION_VENDEDOR_FACTURA_TIPO' AND object_id = OBJECT_ID(N'dbo.ALIADO_COMISION'))
    CREATE UNIQUE INDEX UX_ALIADO_COMISION_VENDEDOR_FACTURA_TIPO ON dbo.ALIADO_COMISION (IdVendedor, IdFactura, TipoComision);
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_ALIADO_COMISION_VENDEDOR_ESTADO' AND object_id = OBJECT_ID(N'dbo.ALIADO_COMISION'))
    CREATE INDEX IX_ALIADO_COMISION_VENDEDOR_ESTADO ON dbo.ALIADO_COMISION (IdVendedor, Estado, FechaGeneracion DESC);
IF OBJECT_ID(N'dbo.ALIADO_RENOVACION_NOTIFICACION', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.ALIADO_RENOVACION_NOTIFICACION
    (
        IdNotificacion INT IDENTITY(1,1) NOT NULL PRIMARY KEY,
        IdVendedor INT NOT NULL,
        IdFactura INT NOT NULL,
        Tipo NVARCHAR(40) NOT NULL,
        FechaEnvio DATETIME2 NOT NULL
    );
END
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'UX_ALIADO_RENOVACION_NOTIFICACION' AND object_id = OBJECT_ID(N'dbo.ALIADO_RENOVACION_NOTIFICACION'))
    CREATE UNIQUE INDEX UX_ALIADO_RENOVACION_NOTIFICACION ON dbo.ALIADO_RENOVACION_NOTIFICACION (IdVendedor, IdFactura, Tipo);
""";
    }
}

public sealed class AliadoPortalContext
{
    public int IdUsuario { get; init; }
    public int IdTipoUsuario { get; init; }
    public int IdRolPortal { get; init; }
    public int IdVendedor { get; init; }
    public string Nombre { get; init; } = string.Empty;
    public string Email { get; init; } = string.Empty;
    public string? Celular { get; init; }
    public string? AvatarUrl { get; init; }
    public string NombreAliado { get; init; } = string.Empty;
    public string CodigoReferencia { get; init; } = string.Empty;
    public decimal PorcentajeBase { get; init; }
    public string EnlaceRegistro { get; init; } = string.Empty;
    public bool EsAdministrador { get; init; }
}

public sealed class AliadoDashboardDto
{
    public AliadoPortalContext Contexto { get; init; } = new();
    public decimal VentasPeriodo { get; init; }
    public int VentasCantidad { get; init; }
    public decimal ComisionesGeneradas { get; init; }
    public decimal ComisionesPendientesAprobacion { get; init; }
    public decimal ComisionesAprobadas { get; init; }
    public decimal ComisionesPagadas { get; init; }
    public int RenovacionesProximas { get; init; }
    public int RenovacionesUrgentes { get; init; }
    public IReadOnlyList<AliadoRenovacionDto> Renovaciones { get; init; } = Array.Empty<AliadoRenovacionDto>();
    public string LinkPersonalizado { get; init; } = string.Empty;
}

public class AliadoClienteDto
{
    public int IdCliente { get; init; }
    public string? Identificacion { get; init; }
    public string Nombre { get; init; } = string.Empty;
    public string? Email { get; init; }
    public string? Telefono { get; init; }
    public string? Producto { get; set; }
    public DateTime? FechaCompra { get; set; }
    public DateTime? FechaVencimiento { get; set; }
    public string? Estado { get; set; }
}

public sealed class AliadoClienteDetalleDto : AliadoClienteDto
{
    public string? Direccion { get; init; }
    public List<AliadoProductoDto> Productos { get; set; } = new();
}

public sealed class AliadoProductoDto
{
    public string Producto { get; init; } = string.Empty;
    public string Plan { get; init; } = string.Empty;
    public DateTime FechaCompra { get; init; }
    public DateTime? FechaVencimiento { get; init; }
    public string? Estado { get; init; }
    public decimal Valor { get; init; }
}

public sealed class AliadoRenovacionDto
{
    public int IdFactura { get; init; }
    public int IdCliente { get; init; }
    public string Cliente { get; init; } = string.Empty;
    public string Producto { get; init; } = string.Empty;
    public DateTime FechaVencimiento { get; init; }
    public int DiasRestantes { get; init; }
    public decimal Valor { get; init; }
    public decimal ComisionPotencial { get; init; }
    public string EstadoGestion { get; init; } = "Pendiente";
    public DateTime? UltimaGestion { get; init; }
    public string? Observacion { get; init; }
}

public sealed class AliadoComisionesDto
{
    public decimal PorcentajeBase { get; init; }
    public decimal Generadas { get; init; }
    public decimal PendientesAprobacion { get; init; }
    public decimal Aprobadas { get; init; }
    public decimal Pagadas { get; init; }
    public int Pendientes { get; init; }
    public IReadOnlyList<AliadoComisionMovimientoDto> Movimientos { get; init; } = Array.Empty<AliadoComisionMovimientoDto>();
}

public sealed class AliadoComisionMovimientoDto
{
    public int IdComision { get; init; }
    public int IdFactura { get; init; }
    public string Cliente { get; init; } = string.Empty;
    public string Producto { get; init; } = string.Empty;
    public string Tipo { get; init; } = string.Empty;
    public decimal BaseComisionable { get; init; }
    public decimal Porcentaje { get; init; }
    public decimal ValorComision { get; init; }
    public DateTime FechaGeneracion { get; init; }
    public string Estado { get; init; } = string.Empty;
    public int? IdLiquidacion { get; init; }
    public string? PeriodoLiquidacion { get; init; }
    public string? ReferenciaPago { get; init; }
}

public sealed class AliadoAdminComisionRow
{
    public int IdComision { get; init; }
    public int IdVendedor { get; init; }
    public string Aliado { get; init; } = string.Empty;
    public int IdFactura { get; init; }
    public string Cliente { get; init; } = string.Empty;
    public string TipoComision { get; init; } = string.Empty;
    public decimal BaseComisionable { get; init; }
    public decimal Porcentaje { get; init; }
    public decimal Valor { get; init; }
    public string Estado { get; init; } = string.Empty;
    public DateTime FechaGeneracion { get; init; }
    public string? Periodo { get; init; }
    public string? ReferenciaPago { get; init; }
}

internal sealed class AliadoComisionResumen
{
    public decimal Generadas { get; init; }
    public decimal PendientesAprobacion { get; init; }
    public decimal Aprobadas { get; init; }
    public decimal Pagadas { get; init; }
}

public sealed class AliadoLiquidacionDto
{
    public int IdLiquidacion { get; init; }
    public string Periodo { get; init; } = string.Empty;
    public decimal Total { get; init; }
    public DateTime Fecha { get; init; }
    public string Estado { get; init; } = string.Empty;
    public string? ReferenciaPago { get; init; }
}

public sealed class AliadoRenovacionNotificacionDto
{
    public int IdVendedor { get; init; }
    public int IdFactura { get; init; }
    public string Email { get; init; } = string.Empty;
    public string NombreAliado { get; init; } = string.Empty;
    public string Cliente { get; init; } = string.Empty;
    public string Producto { get; init; } = string.Empty;
    public DateTime FechaVencimiento { get; init; }
    public int DiasRestantes { get; init; }
    public string Tipo { get; init; } = string.Empty;
}

public sealed class AliadoAdminRow
{
    public int IdVendedor { get; init; }
    public int? IdUsuario { get; init; }
    public string Nombre { get; init; } = string.Empty;
    public string CodigoReferencia { get; init; } = string.Empty;
    public bool Activo { get; init; }
    public bool UsuarioActivo { get; init; }
    public decimal PorcentajeBase { get; init; }
    public string? Usuario { get; init; }
}

public sealed class AliadoUsuarioAdminRow
{
    public int IdUsuario { get; init; }
    public string Nombre { get; init; } = string.Empty;
    public string Email { get; init; } = string.Empty;
    public string? AvatarUrl { get; init; }
    public string Rol { get; init; } = string.Empty;
    public string? Aliado { get; init; }
    public string? CodigoReferencia { get; init; }
    public bool Activo { get; init; }
    public bool Bloqueado { get; init; }
    public DateTime? FechaCreacion { get; init; }
}

public sealed class AliadoVendedorDisponible
{
    public int IdVendedor { get; init; }
    public int IdUsuario { get; init; }
    public string Nombre { get; init; } = string.Empty;
    public string Email { get; init; } = string.Empty;
}

internal sealed class FacturaPortalRow
{
    public int IdFactura { get; init; }
    public int? IdCliente { get; init; }
    public string Cliente { get; init; } = string.Empty;
    public string Producto { get; init; } = string.Empty;
    public string Plan { get; init; } = string.Empty;
    public DateTime Fecha { get; init; }
    public DateTime? FechaVencimiento { get; init; }
    public decimal Total { get; init; }
    public decimal? Subtotal { get; init; }
    public decimal? Subtotal0 { get; init; }
    public decimal? Subtotal12 { get; init; }
    public decimal? Comision { get; init; }
    public bool Autorizado { get; init; }
    public string? EstadoPago { get; init; }
    public string Estado => Autorizado ? "Activo" : "Pendiente";
}

internal sealed class FacturaComisionRow
{
    public int IdFactura { get; init; }
    public int? IdCliente { get; init; }
    public DateTime? FechaVencimiento { get; init; }
    public decimal? Subtotal { get; init; }
    public decimal? Subtotal0 { get; init; }
    public decimal? Subtotal12 { get; init; }
    public bool Autorizado { get; init; }
    public string? EstadoPago { get; init; }
}
