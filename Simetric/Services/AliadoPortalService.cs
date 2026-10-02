using Microsoft.EntityFrameworkCore;
using Simetric.Components.Helpers;
using Simetric.Data;
using Simetric.Models;
using System.Data;
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
    private readonly IEmailService _emailService;
    private readonly LiquidacionCompraService _liquidacionCompraService;
    private readonly AliadoComisionGenerationService _aliadoComisionGenerationService;

    public AliadoPortalService(
        IDbContextFactory<AppDbContext> dbFactory,
        VendedorBackOfficeService vendedorService,
        AuditService auditService,
        IEmailService emailService,
        LiquidacionCompraService liquidacionCompraService,
        AliadoComisionGenerationService aliadoComisionGenerationService)
    {
        _dbFactory = dbFactory;
        _vendedorService = vendedorService;
        _auditService = auditService;
        _emailService = emailService;
        _liquidacionCompraService = liquidacionCompraService;
        _aliadoComisionGenerationService = aliadoComisionGenerationService;
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
            var vendedorAdministrador = await _vendedorService.ObtenerPerfilUsuarioAsync(
                usuario.IdUsuario,
                $"{usuario.Nombres} {usuario.Apellidos}".Trim());

            return new AliadoPortalContext
            {
                IdUsuario = usuario.IdUsuario,
                IdTipoUsuario = usuario.IdTipoUsuario.Value,
                IdRolPortal = rolPortal.IdRol,
                Nombre = $"{usuario.Nombres} {usuario.Apellidos}".Trim(),
                Email = usuario.Email,
                Celular = usuario.Celular,
                AvatarUrl = usuario.AvatarUrl,
                IdVendedor = vendedorAdministrador?.IdVendedor ?? 0,
                NombreAliado = vendedorAdministrador?.Nombre ?? "Administración del Portal",
                CodigoReferencia = vendedorAdministrador?.CodigoReferencia ?? string.Empty,
                EnlaceRegistro = vendedorAdministrador is null
                    ? string.Empty
                    : _vendedorService.ConstruirRutaRegistro(vendedorAdministrador),
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

        var esReporteCompartido = relative.Equals($"{RootRoute}/renovaciones", StringComparison.OrdinalIgnoreCase) ||
                                  relative.Equals($"{RootRoute}/comisiones", StringComparison.OrdinalIgnoreCase) ||
                                  relative.Equals($"{RootRoute}/liquidaciones", StringComparison.OrdinalIgnoreCase);
        if (contexto.EsAdministrador &&
            !esRutaAdmin &&
            !esReporteCompartido &&
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

    public async Task<AliadoDashboardDto?> ObtenerDashboardAsync(int userId, DateTime? inicioPeriodo = null, DateTime? finPeriodo = null)
    {
        var contexto = await ObtenerContextoAsync(userId);
        if (contexto is null)
            return null;

        var enlacesRegistro = contexto.EsAdministrador
            ? (await ListarAliadosAsync())
                .Where(x => x.Activo && x.UsuarioActivo)
                .Select(x => new AliadoEnlaceRegistroDto
                {
                    NombreAliado = x.Nombre,
                    CodigoReferencia = x.CodigoReferencia,
                    RutaRegistro = _vendedorService.ConstruirRutaRegistro(new VendedorBackOffice
                    {
                        IdVendedor = x.IdVendedor,
                        CodigoReferencia = x.CodigoReferencia
                    })
                })
                .ToList()
            : new List<AliadoEnlaceRegistroDto>();

        await SincronizarComisionesAsync(contexto.IdVendedor);
        var facturas = await ObtenerFacturasAsync(contexto.IdVendedor);
        MarcarComprasRepetidas(facturas);
        var hoy = DateTime.Today;
        var desde = (inicioPeriodo ?? new DateTime(hoy.Year, hoy.Month, 1)).Date;
        var hasta = (finPeriodo ?? hoy).Date.AddDays(1);
        if (hasta <= desde)
            hasta = desde.AddDays(1);
        var ventasPeriodo = facturas.Where(x => x.Fecha >= desde && x.Fecha < hasta).ToList();
        var proximasRenovaciones = facturas
            .Where(x => EsRenovacionVisible(x, hoy, 0, 30))
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
            LinkPersonalizado = contexto.EnlaceRegistro,
            EnlacesRegistro = enlacesRegistro,
            
            PeriodoDesde = desde,
            PeriodoHasta = hasta.AddDays(-1)
        };
    }

    public async Task<IReadOnlyList<AliadoClienteDto>> ObtenerClientesAsync(int userId)
    {
        var contexto = await ObtenerContextoAsync(userId);
        if (contexto is null)
            return Array.Empty<AliadoClienteDto>();

        await using var db = await _dbFactory.CreateDbContextAsync();
        var vendedorIds = contexto.EsAdministrador
            ? (await ListarAliadosAsync()).Select(x => x.IdVendedor).ToList()
            : null;
        var clientes = await db.Clientes
            .AsNoTracking()
            .Where(x => x.Estado != false && x.Usuario != userId &&
                        (contexto.EsAdministrador
                            ? x.Idvendedor.HasValue && vendedorIds!.Contains(x.Idvendedor.Value)
                            : x.Idvendedor == contexto.IdVendedor))
            .Select(x => new AliadoClienteDto
            {
                IdCliente = x.Codcliente,
                IdUsuario = x.Usuario,
                Aliado = db.VendedoresBackOffice.Where(v => v.IdVendedor == x.Idvendedor).Select(v => v.Nombre).FirstOrDefault(),
                Identificacion = db.Usuarios.Where(u => u.IdUsuario == x.Usuario).Select(u => u.Identificacion).FirstOrDefault() ?? x.Numeroidentificacion,
                Nombre = db.Usuarios.Where(u => u.IdUsuario == x.Usuario).Select(u => u.TipoCliente == 2 ? (u.NombreEmpresa ?? u.Nombres) : (u.Nombres + " " + u.Apellidos)).FirstOrDefault()
                    ?? x.Nombrerazonsocial ?? x.Nombrecomercial ?? ((x.Nombres ?? "") + " " + (x.Apellidos ?? "")),
                Email = db.Usuarios.Where(u => u.IdUsuario == x.Usuario).Select(u => u.Email).FirstOrDefault() ?? x.Correo,
                Telefono = db.Usuarios.Where(u => u.IdUsuario == x.Usuario).Select(u => u.Celular).FirstOrDefault() ?? x.Celular ?? x.Telefonoconvencional,
                SaldoDocumentos = x.UsuarioNavegacion == null ? 0 : x.UsuarioNavegacion.SaldoDocumentos
            })
            .OrderBy(x => x.Nombre)
            .ToListAsync();

        var facturas = await ObtenerFacturasAsync(contexto.IdVendedor, vendedores: vendedorIds);
        var ultimaPorCliente = facturas
            .Where(x => x.IdCliente.HasValue)
            .GroupBy(x => x.IdCliente!.Value)
            .ToDictionary(x => x.Key, x => x.OrderByDescending(y => y.Fecha).First());
        var ultimaPorIdentificacion = facturas
            .Where(x => !string.IsNullOrWhiteSpace(x.IdentificacionCliente))
            .GroupBy(x => NormalizarIdentificacionCliente(x.IdentificacionCliente), StringComparer.OrdinalIgnoreCase)
            .ToDictionary(x => x.Key, x => x.OrderByDescending(y => y.Fecha).First(), StringComparer.OrdinalIgnoreCase);

        foreach (var cliente in clientes)
        {
            var identificacion = NormalizarIdentificacionCliente(cliente.Identificacion);
            var compras = facturas
                .Where(x => x.IdCliente == cliente.IdCliente ||
                            (!string.IsNullOrWhiteSpace(identificacion) &&
                             string.Equals(NormalizarIdentificacionCliente(x.IdentificacionCliente), identificacion, StringComparison.OrdinalIgnoreCase)))
                .OrderByDescending(x => x.Fecha)
                .ToList();
            if (compras.Count == 0)
            {
                cliente.Estado = "Sin compras";
                continue;
            }

            var factura = compras[0];
            cliente.TotalCompras = compras.Count;
            cliente.ComprasEFact = compras.Count(x => string.Equals(AliadoServicioHelper.Clasificar(x.Producto), "E-FACT", StringComparison.OrdinalIgnoreCase));
            cliente.ComprasERubrica = compras.Count(x => string.Equals(AliadoServicioHelper.Clasificar(x.Producto), "E-RÚBRICA", StringComparison.OrdinalIgnoreCase));
            cliente.Producto = factura.Producto;
            cliente.IdFactura = factura.IdFactura;
            cliente.FechaCompra = factura.Fecha;
            cliente.FechaVencimiento = factura.FechaVencimiento;
            cliente.Estado = factura.Estado;
            cliente.NivelAlerta = ObtenerNivelAlerta(cliente);
            cliente.Alerta = ObtenerDescripcionAlerta(cliente);
        }

        return clientes;
    }

    public async Task<AliadoClienteDetalleDto?> ObtenerClienteAsync(int userId, int idCliente)
    {
        var contexto = await ObtenerContextoAsync(userId);
        if (contexto is null || idCliente <= 0)
            return null;

        await using var db = await _dbFactory.CreateDbContextAsync();
        var vendedorIds = contexto.EsAdministrador
            ? (await ListarAliadosAsync()).Select(x => x.IdVendedor).ToList()
            : null;
        var cliente = await db.Clientes
            .AsNoTracking()
            .Where(x => x.Codcliente == idCliente && x.Usuario != userId &&
                        (contexto.EsAdministrador
                            ? x.Idvendedor.HasValue && vendedorIds!.Contains(x.Idvendedor.Value)
                            : x.Idvendedor == contexto.IdVendedor))
            .Select(x => new AliadoClienteDetalleDto
            {
                IdCliente = x.Codcliente,
                Identificacion = db.Usuarios.Where(u => u.IdUsuario == x.Usuario).Select(u => u.Identificacion).FirstOrDefault() ?? x.Numeroidentificacion,
                Nombre = db.Usuarios.Where(u => u.IdUsuario == x.Usuario).Select(u => u.TipoCliente == 2 ? (u.NombreEmpresa ?? u.Nombres) : (u.Nombres + " " + u.Apellidos)).FirstOrDefault()
                    ?? x.Nombrerazonsocial ?? x.Nombrecomercial ?? ((x.Nombres ?? "") + " " + (x.Apellidos ?? "")),
                Email = db.Usuarios.Where(u => u.IdUsuario == x.Usuario).Select(u => u.Email).FirstOrDefault() ?? x.Correo,
                Telefono = db.Usuarios.Where(u => u.IdUsuario == x.Usuario).Select(u => u.Celular).FirstOrDefault() ?? x.Celular ?? x.Telefonoconvencional,
                Direccion = db.Usuarios.Where(u => u.IdUsuario == x.Usuario).Select(u => u.DireccionEmpresa).FirstOrDefault() ?? x.Direccion
            })
            .FirstOrDefaultAsync();

        if (cliente is null)
            return null;

        if (!EsCorreoDisponible(cliente.Email) && !string.IsNullOrWhiteSpace(cliente.Identificacion))
        {
            var contactos = await db.Clientes
                .AsNoTracking()
                .Where(x => x.Estado != false && x.Usuario != userId &&
                            (contexto.EsAdministrador
                                ? x.Idvendedor.HasValue && vendedorIds!.Contains(x.Idvendedor.Value)
                                : x.Idvendedor == contexto.IdVendedor))
                .Select(x => new
                {
                    Identificacion = db.Usuarios.Where(u => u.IdUsuario == x.Usuario).Select(u => u.Identificacion).FirstOrDefault() ?? x.Numeroidentificacion,
                    Email = db.Usuarios.Where(u => u.IdUsuario == x.Usuario).Select(u => u.Email).FirstOrDefault() ?? x.Correo
                })
                .ToListAsync();
            cliente.Email = contactos
                .Where(x => string.Equals(NormalizarIdentificacionCliente(x.Identificacion), NormalizarIdentificacionCliente(cliente.Identificacion), StringComparison.OrdinalIgnoreCase))
                .Select(x => x.Email)
                .FirstOrDefault(EsCorreoDisponible);
        }

        var identificacionCliente = NormalizarIdentificacionCliente(cliente.Identificacion);
        var facturas = (await ObtenerFacturasAsync(contexto.IdVendedor, vendedores: vendedorIds))
            .Where(x => x.IdCliente == idCliente ||
                        (!string.IsNullOrWhiteSpace(identificacionCliente) &&
                         string.Equals(NormalizarIdentificacionCliente(x.IdentificacionCliente), identificacionCliente, StringComparison.OrdinalIgnoreCase)))
            .ToList();
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

        await using var db = await _dbFactory.CreateDbContextAsync();
        var clientes = (await ObtenerClientesAsync(userId))
            .Where(x => !string.IsNullOrWhiteSpace(x.NivelAlerta))
            .ToList();
        var ids = clientes.Select(x => x.IdFactura).Where(x => x > 0).Distinct().ToList();
        var gestiones = await db.AliadoRenovacionGestiones
            .AsNoTracking()
            .Where(x => (contexto.EsAdministrador || x.IdVendedor == contexto.IdVendedor) && ids.Contains(x.IdFactura))
            .GroupBy(x => x.IdFactura)
            .Select(x => x.OrderByDescending(y => y.FechaGestion).First())
            .ToDictionaryAsync(x => x.IdFactura);

        var resultado = clientes
            .Select(cliente => ToRenovacionCliente(cliente, gestiones.TryGetValue(cliente.IdFactura, out var gestion) ? gestion : null))
            .OrderBy(x => x.NivelAlerta == "danger" ? 0 : 1)
            .ThenBy(x => x.DiasFirmaElectronica ?? int.MaxValue)
            .ThenBy(x => x.SaldoDocumentos)
            .ToList();

        if (!string.IsNullOrWhiteSpace(filtro))
            resultado = resultado.Where(x => x.EstadoGestion.Contains(filtro, StringComparison.OrdinalIgnoreCase)).ToList();

        return resultado;
    }

    private static string? ObtenerNivelAlerta(AliadoClienteDto cliente)
    {
        var alertaRoja = cliente.Servicio == "E-FACT" && cliente.SaldoDocumentos <= 0 ||
                         cliente.DiasFirmaElectronica is <= 7;
        if (alertaRoja)
            return "danger";

        var alertaAmarilla = cliente.Servicio == "E-FACT" && cliente.SaldoDocumentos <= 5 ||
                             cliente.DiasFirmaElectronica is > 7 and <= 15;
        return alertaAmarilla ? "warning" : null;
    }

    private static string ObtenerDescripcionAlerta(AliadoClienteDto cliente)
    {
        var alertas = new List<string>();
        if (cliente.Servicio == "E-FACT" && cliente.SaldoDocumentos <= 5)
            alertas.Add($"{cliente.SaldoDocumentos} documentos");
        if (cliente.DiasFirmaElectronica.HasValue && cliente.DiasFirmaElectronica.Value <= 15)
            alertas.Add($"{cliente.DiasFirmaElectronica.Value} días E-RÚBRICA");
        return string.Join(" · ", alertas);
    }

    private static FirmaClienteEstado CalcularEstadoFirma(DateTime fechaBase, string? vigencia)
    {
        var texto = vigencia?.ToLowerInvariant() ?? string.Empty;
        var fechaVencimiento = texto.Contains("7") && texto.Contains("dia")
            ? fechaBase.AddDays(7)
            : texto.Contains("30") || texto.Contains("mes")
                ? fechaBase.AddDays(30)
                : texto.Contains("2") ? fechaBase.AddYears(2)
                : texto.Contains("3") ? fechaBase.AddYears(3)
                : texto.Contains("4") ? fechaBase.AddYears(4)
                : texto.Contains("5") ? fechaBase.AddYears(5)
                : fechaBase.AddYears(1);
        return new FirmaClienteEstado(fechaVencimiento);
    }

    private static AliadoRenovacionDto ToRenovacionCliente(AliadoClienteDto cliente, AliadoRenovacionGestion? gestion) => new()
    {
        IdFactura = cliente.IdFactura,
        IdCliente = cliente.IdCliente,
        Aliado = cliente.Aliado ?? string.Empty,
        Cliente = cliente.Nombre,
        Producto = cliente.Producto ?? cliente.Servicio,
        FechaVencimiento = cliente.FechaVencimientoFirmaElectronica ?? DateTime.Today,
        DiasRestantes = cliente.DiasFirmaElectronica ?? 0,
        EstadoGestion = gestion?.Resultado ?? "Pendiente",
        UltimaGestion = gestion?.FechaGestion,
        Observacion = gestion?.Observacion,
        ProximoSeguimiento = gestion?.ProximoSeguimiento,
        EsPorSaldo = cliente.Servicio == "E-FACT",
        SaldoDocumentos = cliente.SaldoDocumentos,
        Email = cliente.Email,
        Telefono = cliente.Telefono,
        Identificacion = cliente.Identificacion,
        Servicio = cliente.Servicio,
        DiasFirmaElectronica = cliente.DiasFirmaElectronica,
        FechaVencimientoFirmaElectronica = cliente.FechaVencimientoFirmaElectronica,
        NivelAlerta = cliente.NivelAlerta ?? "warning",
        Alerta = cliente.Alerta
    };

    private sealed record FirmaClienteEstado(DateTime FechaVencimiento);

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
                    (db.TipoUsuario.Any(t => t.IdTipoUsuario == u.IdTipoUsuario && t.NombreTipo == RoleName) ||
                     db.AliadoPortalUsuariosRoles.Any(ur => ur.IdUsuario == u.IdUsuario &&
                        db.AliadoPortalRoles.Any(r => r.IdRol == ur.IdRol && r.Activo && r.Nombre == RoleName)))) ||
                 db.AliadoComisiones.Any(c => c.IdVendedor == x.IdVendedor)))
            .Select(x => x.IdVendedor)
            .ToListAsync();
        foreach (var idVendedor in vendedorIds)
            await _aliadoComisionGenerationService.SincronizarAsync(idVendedor);

        await _aliadoComisionGenerationService.CerrarPeriodosAsync();
        await NormalizarComisionesPagadasAsync(db);

        return await db.AliadoComisiones
            .AsNoTracking()
            .Where(x => db.VendedoresBackOffice.Any(v => v.IdVendedor == x.IdVendedor && !v.EsSistema) &&
                        db.Facturas.Any(f => f.Codfactura == x.IdFactura &&
                                             (f.Estado == true || f.Estado == null)) &&
                        !db.NotaCreditos.Any(nc =>
                            nc.IdDocModificado == x.IdFactura &&
                            nc.Estado == true &&
                            nc.Autorizado == DocumentoAutorizacionHelper.EstadoAutorizado))
            .Join(db.VendedoresBackOffice.AsNoTracking(), x => x.IdVendedor, x => x.IdVendedor, (comision, aliado) => new { comision, aliado })
            .OrderByDescending(x => x.comision.FechaGeneracion)
            .Select(x => new AliadoAdminComisionRow
            {
                IdComision = x.comision.IdComision,
                IdVendedor = x.comision.IdVendedor,
                IdCliente = x.comision.IdCliente,
                Aliado = x.aliado.Nombre,
                IdFactura = x.comision.IdFactura,
                NumeroFactura = db.Facturas.Where(f => f.Codfactura == x.comision.IdFactura).Select(f => f.Numfactura).FirstOrDefault() ?? x.comision.IdFactura.ToString(),
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
                    .FirstOrDefault() ?? x.comision.Periodo,
                ReferenciaPago = db.AliadoLiquidaciones
                    .Where(l => l.IdLiquidacion == x.comision.IdLiquidacion)
                    .Select(l => l.ReferenciaPago)
                    .FirstOrDefault()
            })
            .ToListAsync();
    }

    private async Task NormalizarComisionesPagadasAsync(AppDbContext db)
    {
        var desincronizadas = await (
            from comision in db.AliadoComisiones
            join liquidacion in db.AliadoLiquidaciones
                on comision.IdLiquidacion equals liquidacion.IdLiquidacion
            where comision.Estado == "AjustePendiente" && liquidacion.Estado == "Pagada"
            select new
            {
                Comision = comision,
                liquidacion.FechaPago,
                liquidacion.Fecha,
                liquidacion.IdUsuarioPago
            })
            .ToListAsync();

        foreach (var item in desincronizadas)
        {
            item.Comision.Estado = "Pagada";
            item.Comision.FechaPago ??= item.FechaPago ?? item.Fecha;
            item.Comision.IdUsuarioPago ??= item.IdUsuarioPago;
        }

        if (desincronizadas.Count > 0)
            await db.SaveChangesAsync();
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
        try
        {
            AliadoComisionStateMachine.Require(comision.Estado, AliadoComisionEstado.Aprobada);
        }
        catch (InvalidOperationException)
        {
            return (false, "Solo se pueden aprobar comisiones generadas o pendientes.");
        }

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

    public async Task<(bool Success, string Message)> LiquidarComisionesAsync(int actorId, IReadOnlyCollection<int> idsComision, string? referenciaPago, string? observacionPago = null, string? comprobantePagoUrl = null)
    {
        await EnsureSchemaAsync();
        if (idsComision.Count == 0)
            return (false, "Selecciona al menos una comisión.");
        referenciaPago = string.IsNullOrWhiteSpace(referenciaPago) ? null : referenciaPago.Trim();
        if (referenciaPago is null && string.IsNullOrWhiteSpace(comprobantePagoUrl))
            return (false, "Registra la referencia de pago o adjunta el comprobante.");
        if (referenciaPago?.Length > 100)
            return (false, "La referencia de pago no puede superar 100 caracteres.");
        observacionPago = string.IsNullOrWhiteSpace(observacionPago) ? null : observacionPago.Trim();
        if (observacionPago?.Length > 500)
            return (false, "La observación no puede superar 500 caracteres.");

        await using var db = await _dbFactory.CreateDbContextAsync();
        if (!await EsAdministradorPortalAsync(db, actorId))
            return (false, "No tienes permisos para registrar pagos.");
        var ids = idsComision.Distinct().ToArray();
        var ahora = DateTime.Now;
        var liquidacionesPagadas = new List<int>();
        var cantidadLiquidada = 0;
        var executionStrategy = db.Database.CreateExecutionStrategy();

        try
        {
            await executionStrategy.ExecuteAsync(async () =>
            {
            liquidacionesPagadas.Clear();
            await using var transaction = await db.Database.BeginTransactionAsync(IsolationLevel.Serializable);
            var comisiones = await db.AliadoComisiones.Where(x => ids.Contains(x.IdComision)).ToListAsync();
            if (comisiones.Count != ids.Length)
                throw new InvalidOperationException("Una o más comisiones seleccionadas no existen.");
            if (comisiones.Any(x => !string.Equals(x.Estado, AliadoComisionEstado.Aprobada.ToString(), StringComparison.OrdinalIgnoreCase)))
                throw new InvalidOperationException("Solo se pueden liquidar comisiones aprobadas.");

            foreach (var grupo in comisiones.GroupBy(x => new { x.IdVendedor, x.Periodo }))
            {
                var liquidacionIdsOrigen = grupo.Select(x => x.IdLiquidacion).Distinct().ToArray();
                var liquidacionIdOrigen = liquidacionIdsOrigen.Length == 1 ? liquidacionIdsOrigen[0] : null;
                var liquidacionesOrigen = liquidacionIdsOrigen.Length == 0
                    ? new List<AliadoLiquidacion>()
                    : await db.AliadoLiquidaciones.Where(x => liquidacionIdsOrigen.Contains(x.IdLiquidacion) && x.Estado == "Pendiente").ToListAsync();
                var liquidacionOrigen = liquidacionIdOrigen.HasValue ? liquidacionesOrigen.SingleOrDefault() : null;
                var pendientesOrigen = liquidacionOrigen is null
                    ? 0
                    : await db.AliadoComisiones.CountAsync(x => x.IdLiquidacion == liquidacionOrigen.IdLiquidacion && x.Estado != "Pagada" && x.Estado != "Anulada" && x.Estado != "Revertida");
                var reutilizarLiquidacion = liquidacionOrigen is not null && liquidacionOrigen.IdCliente is null && pendientesOrigen == grupo.Count();
                var liquidacion = reutilizarLiquidacion ? liquidacionOrigen! : new AliadoLiquidacion
                {
                    IdVendedor = grupo.Key.IdVendedor,
                    IdCliente = null,
                    Periodo = grupo.Key.Periodo,
                    Fecha = ahora,
                    Total = grupo.Sum(x => x.Valor),
                    Estado = "Pagada"
                };

                if (!reutilizarLiquidacion)
                {
                    db.AliadoLiquidaciones.Add(liquidacion);
                    await db.SaveChangesAsync();
                }
                else
                {
                    liquidacion.Total = grupo.Sum(x => x.Valor);
                }

                liquidacion.Fecha = ahora;
                liquidacion.Estado = "Pagada";
                liquidacion.ReferenciaPago = referenciaPago;
                liquidacion.ObservacionPago = observacionPago;
                liquidacion.ComprobantePagoUrl = comprobantePagoUrl;
                liquidacion.FechaPago = ahora;
                liquidacion.IdUsuarioPago = actorId;
                liquidacion.EstadoSri = "Pendiente";
                liquidacion.ErrorSri = null;
                liquidacionesPagadas.Add(liquidacion.IdLiquidacion);

                foreach (var comision in grupo)
                {
                    AliadoComisionStateMachine.Require(comision.Estado, AliadoComisionEstado.Pagada);
                    comision.Estado = "Pagada";
                    comision.FechaPago = ahora;
                    comision.IdLiquidacion = liquidacion.IdLiquidacion;
                    comision.IdUsuarioPago = actorId;
                }

                if (!reutilizarLiquidacion)
                {
                    foreach (var origen in liquidacionesOrigen)
                    {
                        origen.Total = await db.AliadoComisiones
                            .Where(x => x.IdLiquidacion == origen.IdLiquidacion && !ids.Contains(x.IdComision) && x.Estado != "Pagada" && x.Estado != "Anulada" && x.Estado != "Revertida")
                            .SumAsync(x => (decimal?)x.Valor) ?? 0m;
                        if (origen.Total <= 0m)
                            origen.Estado = "Anulada";
                    }
                }
            }

            cantidadLiquidada = comisiones.Count;
            await db.SaveChangesAsync();
            await transaction.CommitAsync();
            });
        }
        catch (InvalidOperationException ex)
        {
            return (false, ex.Message);
        }
        await _auditService.TryRegistrarAuditoriaAsync(actorId, "PAGAR", null, new { IdsComision = ids, ReferenciaPago = referenciaPago }, new { Modulo = "PortalAliados", Entidad = "Liquidacion" });
        var erroresLiquidacionCompra = new List<string>();
        foreach (var idLiquidacion in liquidacionesPagadas.Distinct())
        {
            try
            {
                await _liquidacionCompraService.GenerarLiquidacionCompraAliadoAsync(idLiquidacion);
                await ActualizarEstadoSriAsync(idLiquidacion, "Enviada", null);
            }
            catch (Exception ex)
            {
                await ActualizarEstadoSriAsync(idLiquidacion, "Error", ex.Message);
                erroresLiquidacionCompra.Add(ex.Message);
            }
        }
        return erroresLiquidacionCompra.Count == 0
            ? (true, $"Se liquidaron {cantidadLiquidada} comisión(es) y se enviaron al SRI.")
            : (true, $"Se liquidaron {cantidadLiquidada} comisión(es). La liquidación SRI queda pendiente de reintento: {erroresLiquidacionCompra[0]}");
    }

    public async Task<(bool Success, string Message)> ReintentarLiquidacionSriAsync(int actorId, int idLiquidacion)
    {
        await EnsureSchemaAsync();
        await using var db = await _dbFactory.CreateDbContextAsync();
        if (!await EsAdministradorPortalAsync(db, actorId)) return (false, "No tienes permisos para reenviar al SRI.");
        var liquidacion = await db.AliadoLiquidaciones.FirstOrDefaultAsync(x => x.IdLiquidacion == idLiquidacion);
        if (liquidacion is null || liquidacion.Estado != "Pagada") return (false, "La liquidación no está pagada.");
        try
        {
            await _liquidacionCompraService.GenerarLiquidacionCompraAliadoAsync(idLiquidacion);
            await ActualizarEstadoSriAsync(idLiquidacion, "Enviada", null);
            return (true, "Liquidación enviada al SRI.");
        }
        catch (Exception ex)
        {
            await ActualizarEstadoSriAsync(idLiquidacion, "Error", ex.Message);
            return (false, "No se pudo enviar al SRI: " + ex.Message);
        }
    }

    public async Task<int> ReintentarLiquidacionesSriAutomaticamenteAsync(int maxRegistros = 10)
    {
        if (maxRegistros <= 0) return 0;

        await EnsureSchemaAsync();
        await using var db = await _dbFactory.CreateDbContextAsync();
        var candidatas = await (
            from liquidacion in db.AliadoLiquidaciones.AsNoTracking()
            join compra in db.ComprasFacturas.AsNoTracking()
                on liquidacion.CodLiquidacionCompra equals compra.CodFactura
            where liquidacion.Estado == "Pagada" &&
                  liquidacion.CodLiquidacionCompra.HasValue &&
                  (liquidacion.EstadoSri == "Pendiente" || liquidacion.EstadoSri == "Error") &&
                  compra.Estado == true &&
                  compra.CodDocumento == "03" &&
                  (compra.EstadoEnvioSRI == null ||
                   compra.EstadoEnvioSRI == "PENDIENTE" ||
                   compra.EstadoEnvioSRI == "MANUAL" ||
                   EF.Functions.Like(compra.EstadoEnvioSRI, "ERROR%"))
            orderby liquidacion.Fecha
            select new
            {
                liquidacion.IdLiquidacion,
                CodLiquidacionCompra = liquidacion.CodLiquidacionCompra ?? 0
            })
            .Take(maxRegistros)
            .ToListAsync();

        foreach (var candidata in candidatas)
        {
            try
            {
                var resultado = await _liquidacionCompraService.EmitirLiquidacionSriAsync(
                    candidata.CodLiquidacionCompra,
                    intentarEnviarCorreo: true);
                var autorizada = string.Equals(
                    resultado.estado,
                    DocumentoAutorizacionHelper.EstadoAutorizado,
                    StringComparison.OrdinalIgnoreCase) ||
                    !string.IsNullOrWhiteSpace(resultado.autorizacion);

                await ActualizarEstadoSriAsync(
                    candidata.IdLiquidacion,
                    autorizada ? "Enviada" : "Error",
                    autorizada ? null : resultado.mensaje ?? resultado.estado);
            }
            catch (Exception ex)
            {
                await ActualizarEstadoSriAsync(candidata.IdLiquidacion, "Error", ex.Message);
            }
        }

        return candidatas.Count;
    }

    public async Task CancelarComisionesFacturaAsync(int idFactura, string motivo, int? actorId = null)
    {
        if (idFactura <= 0) return;
        await EnsureSchemaAsync();
        await using var db = await _dbFactory.CreateDbContextAsync();
        await using var transaction = await db.Database.BeginTransactionAsync(IsolationLevel.Serializable);
        var comisiones = await db.AliadoComisiones
            .Where(x => x.IdFactura == idFactura &&
                        !x.TipoComision.StartsWith("Reversion-") &&
                        x.Estado != "Anulada" && x.Estado != "Revertida")
            .ToListAsync();
        foreach (var comision in comisiones)
        {
            var estadoDestino = comision.Estado is "Pagada" or "AjustePendiente"
                ? AliadoComisionEstado.Revertida
                : AliadoComisionEstado.Anulada;
            AliadoComisionStateMachine.Require(comision.Estado, estadoDestino);
            comision.Estado = estadoDestino.ToString();
            if (estadoDestino == AliadoComisionEstado.Revertida)
            {
                var tipo = $"Reversion-{comision.TipoComision}";
                tipo = tipo[..Math.Min(40, tipo.Length)];
                if (!await db.AliadoComisiones.AnyAsync(x => x.IdVendedor == comision.IdVendedor &&
                                                             x.IdFactura == idFactura &&
                                                             x.TipoComision == tipo))
                {
                    db.AliadoComisiones.Add(new AliadoComision
                    {
                        IdVendedor = comision.IdVendedor,
                        IdFactura = idFactura,
                        IdCliente = comision.IdCliente,
                        TipoComision = tipo,
                        BaseComisionable = -comision.BaseComisionable,
                        Porcentaje = comision.Porcentaje,
                        Valor = -comision.Valor,
                        Periodo = DateTime.Now.ToString("yyyy-MM"),
                        Estado = "Generada",
                        FechaGeneracion = DateTime.Now
                    });
                }
            }
        }
        if (comisiones.Count > 0) await db.SaveChangesAsync();
        await transaction.CommitAsync();
        if (comisiones.Count > 0 && actorId is > 0)
            await _auditService.TryRegistrarAuditoriaAsync(actorId.Value, "ANULAR", null,
                new { IdFactura = idFactura, Cantidad = comisiones.Count, Motivo = motivo },
                new { Modulo = "PortalAliados", Entidad = "Comision" });
    }

    private async Task ActualizarEstadoSriAsync(int idLiquidacion, string estadoSri, string? error)
    {
        await using var db = await _dbFactory.CreateDbContextAsync();
        var liquidacion = await db.AliadoLiquidaciones.FirstOrDefaultAsync(x => x.IdLiquidacion == idLiquidacion);
        if (liquidacion is null) return;
        liquidacion.EstadoSri = estadoSri;
        liquidacion.ErrorSri = string.IsNullOrWhiteSpace(error) ? null : error[..Math.Min(error.Length, 1000)];
        await db.SaveChangesAsync();
    }

    public async Task<(bool Success, string Message)> GuardarParametroComisionAsync(int actorId, int idVendedor, string periodo, decimal porcentajeHastaMil, decimal porcentajeDesdeMil)
    {
        await EnsureSchemaAsync();
        if (!DateTime.TryParseExact($"{periodo}-01", "yyyy-MM-dd", null, System.Globalization.DateTimeStyles.None, out _) ||
            !TryNormalizarPorcentaje(porcentajeHastaMil, out porcentajeHastaMil) ||
            !TryNormalizarPorcentaje(porcentajeDesdeMil, out porcentajeDesdeMil))
            return (false, "El período o porcentaje no es válido.");
        await using var db = await _dbFactory.CreateDbContextAsync();
        if (!await EsAdministradorPortalAsync(db, actorId))
            return (false, "No tienes permisos para parametrizar comisiones.");
        var parametro = await db.AliadoComisionParametros.SingleOrDefaultAsync(x => x.IdVendedor == idVendedor && x.Periodo == periodo);
        if (parametro is null)
        {
            parametro = new AliadoComisionParametro { IdVendedor = idVendedor, Periodo = periodo };
            db.AliadoComisionParametros.Add(parametro);
        }
        parametro.Porcentaje = porcentajeHastaMil;
        parametro.PorcentajeHastaMil = porcentajeHastaMil;
        parametro.PorcentajeDesdeMil = porcentajeDesdeMil;
        parametro.FechaActualizacion = DateTime.Now;
        parametro.IdUsuarioActualizacion = actorId;
        await db.SaveChangesAsync();
        return (true, "Parámetro mensual guardado. No altera otros períodos.");
    }

    public async Task<(bool Success, string Message)> AjustarComisionAsync(int actorId, int idComision, decimal porcentaje, decimal? valor)
    {
        await EnsureSchemaAsync();
        if (!TryNormalizarPorcentaje(porcentaje, out porcentaje) || valor is < 0)
            return (false, "El ajuste no es válido.");
        await using var db = await _dbFactory.CreateDbContextAsync();
        if (!await EsAdministradorPortalAsync(db, actorId))
            return (false, "No tienes permisos para ajustar comisiones.");
        var comision = await db.AliadoComisiones.SingleOrDefaultAsync(x => x.IdComision == idComision);
        if (comision is null || comision.Estado is "Pagada" or "AjustePendiente")
            return (false, "Solo se pueden ajustar comisiones sin pagar.");
        comision.Porcentaje = porcentaje;
        comision.Valor = valor ?? decimal.Round(comision.BaseComisionable * porcentaje / 100m, 2, MidpointRounding.AwayFromZero);
        await db.SaveChangesAsync();
        return (true, "Comisión ajustada.");
    }

    public async Task<(bool Success, string Message)> AjustarValorComisionAsync(int actorId, int idComision, decimal valor)
    {
        await EnsureSchemaAsync();
        if (valor < 0)
            return (false, "El valor de la comisión no puede ser negativo.");
        await using var db = await _dbFactory.CreateDbContextAsync();
        if (!await EsAdministradorPortalAsync(db, actorId))
            return (false, "No tienes permisos para ajustar comisiones.");
        var comision = await db.AliadoComisiones.SingleOrDefaultAsync(x => x.IdComision == idComision);
        if (comision is null || comision.Estado is "Pagada" or "AjustePendiente")
            return (false, "Solo se pueden ajustar comisiones sin pagar.");

        comision.Valor = decimal.Round(valor, 2, MidpointRounding.AwayFromZero);
        if (comision.BaseComisionable > 0)
            comision.Porcentaje = decimal.Round(comision.Valor / comision.BaseComisionable * 100m, 2, MidpointRounding.AwayFromZero);
        await db.SaveChangesAsync();
        return (true, "Valor de comisión ajustado.");
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
        await using var transaction = await db.Database.BeginTransactionAsync(IsolationLevel.Serializable);
        var original = await db.AliadoComisiones.FirstOrDefaultAsync(x => x.IdComision == idComision);
        if (original is null)
            return (false, "La comisión no existe.");
        if (original.Estado is "Anulada" or "Revertida")
            return (false, "La comisión ya fue anulada o revertida.");
        if (original.Estado is "Pagada" or "AjustePendiente")
        {
            AliadoComisionStateMachine.Require(original.Estado, AliadoComisionEstado.Revertida);
            original.Estado = "Revertida";
            var tipo = $"Reversion-{original.TipoComision}";
            tipo = tipo[..Math.Min(40, tipo.Length)];
            db.AliadoComisiones.Add(new AliadoComision
            {
                IdVendedor = original.IdVendedor,
                IdFactura = original.IdFactura,
                IdCliente = original.IdCliente,
                TipoComision = tipo,
                BaseComisionable = -original.BaseComisionable,
                Porcentaje = original.Porcentaje,
                Valor = -original.Valor,
                Periodo = DateTime.Now.ToString("yyyy-MM"),
                Estado = "Generada",
                FechaGeneracion = DateTime.Now
            });
        }
        else
        {
            AliadoComisionStateMachine.Require(original.Estado, AliadoComisionEstado.Anulada);
            original.Estado = "Anulada";
        }
        await db.SaveChangesAsync();
        await transaction.CommitAsync();
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

        if (contexto.EsAdministrador)
        {
            var administracion = await ObtenerComisionesAdministracionAsync(userId);
            return new AliadoComisionesDto
            {
                Movimientos = administracion.Select(x => new AliadoComisionMovimientoDto
                {
                    IdComision = x.IdComision,
                    IdFactura = x.IdFactura,
                    NumeroFactura = x.NumeroFactura,
                    Aliado = x.Aliado,
                    Cliente = x.Cliente,
                    Producto = "Servicio Numerica",
                    Tipo = x.TipoComision,
                    BaseComisionable = x.BaseComisionable,
                    Porcentaje = x.Porcentaje,
                    ValorComision = x.Valor,
                    FechaGeneracion = x.FechaGeneracion,
                    Estado = x.Estado,
                    PeriodoLiquidacion = x.Periodo,
                    EstadoPago = x.Estado == "Pagada" ? "Pagada" : null,
                    ReferenciaPago = x.ReferenciaPago
                }).ToList()
            };
        }

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
                Aliado = db.VendedoresBackOffice
                    .Where(v => v.IdVendedor == x.IdVendedor)
                    .Select(v => v.Nombre)
                    .FirstOrDefault() ?? string.Empty,
                NumeroFactura = db.Facturas.Where(f => f.Codfactura == x.IdFactura).Select(f => f.Numfactura).FirstOrDefault() ?? x.IdFactura.ToString(),
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
                    .FirstOrDefault() ?? x.Periodo,
                 EstadoPago = db.AliadoLiquidaciones
                     .Where(l => l.IdLiquidacion == x.IdLiquidacion)
                     .Select(l => l.Estado)
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
        return await db.VendedoresBackOffice
            .AsNoTracking()
            .Where(x => !x.EsSistema &&
                (db.Usuarios.Any(u => u.IdVendedor == x.IdVendedor &&
                    (db.TipoUsuario.Any(t => t.IdTipoUsuario == u.IdTipoUsuario && t.NombreTipo == RoleName) ||
                     db.AliadoPortalUsuariosRoles.Any(ur => ur.IdUsuario == u.IdUsuario &&
                         db.AliadoPortalRoles.Any(r => r.IdRol == ur.IdRol && r.Activo && r.Nombre == RoleName)))) ||
                 db.AliadoComisiones.Any(c => c.IdVendedor == x.IdVendedor)))
            .OrderBy(x => x.Nombre)
            .Select(x => new AliadoAdminRow
            {
                IdVendedor = x.IdVendedor,
                Nombre = x.Nombre,
                CodigoReferencia = x.CodigoReferencia,
                Activo = x.Activo,
                PorcentajeBase = x.PorcentajeBase,
                IdUsuario = db.Usuarios.Where(u => u.IdVendedor == x.IdVendedor &&
                    (db.TipoUsuario.Any(t => t.IdTipoUsuario == u.IdTipoUsuario && t.NombreTipo == RoleName) ||
                     db.AliadoPortalUsuariosRoles.Any(ur => ur.IdUsuario == u.IdUsuario && db.AliadoPortalRoles.Any(r => r.IdRol == ur.IdRol && r.Activo && r.Nombre == RoleName)))).Select(u => (int?)u.IdUsuario).FirstOrDefault(),
                Usuario = db.Usuarios.Where(u => u.IdVendedor == x.IdVendedor &&
                    (db.TipoUsuario.Any(t => t.IdTipoUsuario == u.IdTipoUsuario && t.NombreTipo == RoleName) ||
                     db.AliadoPortalUsuariosRoles.Any(ur => ur.IdUsuario == u.IdUsuario && db.AliadoPortalRoles.Any(r => r.IdRol == ur.IdRol && r.Activo && r.Nombre == RoleName)))).Select(u => u.Email).FirstOrDefault(),
                UsuarioActivo = db.Usuarios.Where(u => u.IdVendedor == x.IdVendedor &&
                    (db.TipoUsuario.Any(t => t.IdTipoUsuario == u.IdTipoUsuario && t.NombreTipo == RoleName) ||
                     db.AliadoPortalUsuariosRoles.Any(ur => ur.IdUsuario == u.IdUsuario && db.AliadoPortalRoles.Any(r => r.IdRol == ur.IdRol && r.Activo && r.Nombre == RoleName)))).Select(u => u.Estado ?? false).FirstOrDefault()
            })
            .ToListAsync();
    }

    public async Task<IReadOnlyList<AliadoClienteAsignacionRow>> ListarClientesParaAsignacionAsync(int actorId)
    {
        await EnsureSchemaAsync();
        await using var db = await _dbFactory.CreateDbContextAsync();
        if (!await EsAdministradorInternoAsync(db, actorId))
            return Array.Empty<AliadoClienteAsignacionRow>();

        var aliadosDisponibles = (await ListarAliadosAsync())
            .Select(x => x.IdVendedor)
            .ToHashSet();

        return await db.Clientes
            .AsNoTracking()
            .Where(x => x.Estado != false &&
                        (!x.Idvendedor.HasValue || aliadosDisponibles.Contains(x.Idvendedor.Value)))
            .OrderBy(x => x.Nombrerazonsocial ?? x.Nombrecomercial ?? ((x.Nombres ?? string.Empty) + " " + (x.Apellidos ?? string.Empty)))
            .Select(x => new AliadoClienteAsignacionRow
            {
                IdCliente = x.Codcliente,
                Nombre = x.Nombrerazonsocial ?? x.Nombrecomercial ?? ((x.Nombres ?? string.Empty) + " " + (x.Apellidos ?? string.Empty)),
                Identificacion = x.Numeroidentificacion,
                Correo = x.Correo,
                IdVendedorActual = x.Idvendedor,
                AliadoActual = db.VendedoresBackOffice
                    .Where(v => v.IdVendedor == x.Idvendedor)
                    .Select(v => v.Nombre)
                    .FirstOrDefault()
            })
            .ToListAsync();
    }

    public async Task<(bool Success, string Message)> AsignarClienteAliadoAsync(int actorId, int idCliente, int idVendedorDestino)
    {
        await EnsureSchemaAsync();
        await using var db = await _dbFactory.CreateDbContextAsync();
        if (!await EsAdministradorInternoAsync(db, actorId))
            return (false, "No tienes permisos para asignar clientes.");

        var aliado = await db.VendedoresBackOffice
            .FirstOrDefaultAsync(x => x.IdVendedor == idVendedorDestino && !x.EsSistema && x.Activo);
        if (aliado is null)
            return (false, "El aliado seleccionado no está disponible.");

        var aliadosDisponibles = (await ListarAliadosAsync())
            .Select(x => x.IdVendedor)
            .ToHashSet();
        if (!aliadosDisponibles.Contains(idVendedorDestino))
            return (false, "El destino seleccionado no es un asociado válido.");

        var cliente = await db.Clientes.FirstOrDefaultAsync(x => x.Codcliente == idCliente && x.Estado != false);
        if (cliente is null)
            return (false, "No se encontró el cliente seleccionado.");
        if (cliente.Idvendedor.HasValue && !aliadosDisponibles.Contains(cliente.Idvendedor.Value))
            return (false, "Los clientes de vendedores no pueden asignarse a un asociado.");
        if (cliente.Idvendedor == idVendedorDestino)
            return (false, "El cliente ya está asignado a este aliado.");

        var idVendedorAnterior = cliente.Idvendedor;
        var aliadoAnterior = idVendedorAnterior.HasValue
            ? await db.VendedoresBackOffice.AsNoTracking()
                .Where(x => x.IdVendedor == idVendedorAnterior.Value)
                .Select(x => x.Nombre)
                .FirstOrDefaultAsync()
            : null;

        cliente.Idvendedor = idVendedorDestino;
        if (cliente.Usuario.HasValue)
        {
            var usuario = await db.Usuarios.FirstOrDefaultAsync(x => x.IdUsuario == cliente.Usuario.Value);
            if (usuario is not null)
                usuario.IdVendedor = idVendedorDestino;
        }

        await db.SaveChangesAsync();
        await _auditService.TryRegistrarAuditoriaAsync(
            actorId,
            "ASIGNAR",
            new { IdCliente = idCliente, IdVendedor = idVendedorAnterior, Aliado = aliadoAnterior },
            new { IdCliente = idCliente, IdVendedor = idVendedorDestino, Aliado = aliado.Nombre, ComisionesTransferidas = false },
            new { Modulo = "PortalAliados", Entidad = "AsignacionCliente" });

        var retiro = string.IsNullOrWhiteSpace(aliadoAnterior)
            ? string.Empty
            : $" Se retiró del aliado {aliadoAnterior}; sus comisiones anteriores no se transfirieron.";
        return (true, $"Cliente asignado a {aliado.Nombre}.{retiro}");
    }

    public async Task<IReadOnlyList<AliadoUsuarioAsignacionRow>> ListarUsuariosEfactParaAsignacionAsync(int actorId)
    {
        await EnsureSchemaAsync();
        await using var db = await _dbFactory.CreateDbContextAsync();
        if (!await EsAdministradorInternoAsync(db, actorId))
            return Array.Empty<AliadoUsuarioAsignacionRow>();

        return await db.Usuarios.AsNoTracking()
            .Where(u => u.IdTipoUsuario == 1 && u.Estado == true &&
                u.Identificacion != "9999999999999" &&
                u.Email != "consumidorfinal@numerica" &&
                db.Clientes.Any(c => c.Usuario == u.IdUsuario && c.Estado != false && c.Numeroidentificacion != "9999999999999"))
            .OrderBy(u => u.Nombres).ThenBy(u => u.Apellidos)
            .Select(u => new AliadoUsuarioAsignacionRow
            {
                IdUsuario = u.IdUsuario,
                Nombre = ((u.Nombres ?? string.Empty) + " " + (u.Apellidos ?? string.Empty)).Trim(),
                Identificacion = u.Identificacion,
                Correo = u.Email,
                IdVendedorActual = db.VendedoresBackOffice
                    .Where(v => v.IdVendedor == u.IdVendedor && !v.EsSistema)
                    .Select(v => (int?)v.IdVendedor)
                    .FirstOrDefault(),
                AliadoActual = db.VendedoresBackOffice
                    .Where(v => v.IdVendedor == u.IdVendedor && !v.EsSistema)
                    .Select(v => v.Nombre)
                    .FirstOrDefault()
            })
            .ToListAsync();
    }

    public async Task<(bool Success, string Message)> AsignarUsuarioAliadoAsync(int actorId, int idUsuario, int idVendedorDestino)
    {
        await EnsureSchemaAsync();
        await using var db = await _dbFactory.CreateDbContextAsync();
        if (!await EsAdministradorInternoAsync(db, actorId))
            return (false, "No tienes permisos para asignar usuarios.");

        var aliado = await db.VendedoresBackOffice
            .FirstOrDefaultAsync(x => x.IdVendedor == idVendedorDestino && !x.EsSistema && x.Activo);
        if (aliado is null)
            return (false, "El aliado seleccionado no está disponible.");

        var usuario = await db.Usuarios.FirstOrDefaultAsync(x =>
            x.IdUsuario == idUsuario && x.IdTipoUsuario == 1 && x.Estado == true &&
            x.Identificacion != "9999999999999" && x.Email != "consumidorfinal@numerica");
        if (usuario is null)
            return (false, "No se encontró un usuario normal de E-Fact válido.");
        if (usuario.IdVendedor == idVendedorDestino)
            return (false, "El usuario ya está asignado a este aliado.");

        var vendedorAnterior = usuario.IdVendedor.HasValue
            ? await db.VendedoresBackOffice.AsNoTracking()
                .Where(x => x.IdVendedor == usuario.IdVendedor.Value && !x.EsSistema)
                .Select(x => new { x.IdVendedor, x.Nombre })
                .FirstOrDefaultAsync()
            : null;

        usuario.IdVendedor = idVendedorDestino;
        var clientesUsuario = await db.Clientes
            .Where(x => x.Usuario == idUsuario && x.Estado != false && x.Numeroidentificacion != "9999999999999")
            .ToListAsync();
        foreach (var cliente in clientesUsuario)
            cliente.Idvendedor = idVendedorDestino;
        await db.SaveChangesAsync();
        await _auditService.TryRegistrarAuditoriaAsync(
            actorId,
            "ASIGNAR",
            new { IdUsuario = idUsuario, IdVendedor = vendedorAnterior?.IdVendedor, Aliado = vendedorAnterior?.Nombre },
            new { IdUsuario = idUsuario, IdVendedor = idVendedorDestino, Aliado = aliado.Nombre, ClientesActualizados = clientesUsuario.Count, ComisionesTransferidas = false },
            new { Modulo = "PortalAliados", Entidad = "AsignacionUsuario" });

        var retiro = vendedorAnterior is null
            ? string.Empty
            : $" Se retiró del aliado {vendedorAnterior.Nombre}; sus comisiones anteriores no se transfirieron.";
        return (true, $"Usuario asignado a {aliado.Nombre}.{retiro}");
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
                Nombres = u.Nombres,
                Apellidos = u.Apellidos,
                Nombre = ((u.Nombres ?? string.Empty) + " " + (u.Apellidos ?? string.Empty)).Trim(),
                Email = u.Email,
                AvatarUrl = u.AvatarUrl,
                Activo = u.Estado ?? false,
                Bloqueado = u.CuentaBloqueada ?? false,
                ClaveTemporal = u.ClaveTemporal ?? false,
                Identificacion = u.Identificacion,
                 TipoCliente = u.TipoCliente ?? 1,
                 TipoDocumento = u.IdTipoIdentificacion == 2 ? "RUC" : u.IdTipoIdentificacion == 3 ? "EXTERIOR" : "CEDULA",
                 RazonSocial = u.TipoCliente == 2 ? u.Nombres : null,
                 NombreComercial = u.NombreEmpresa,
                 Celular = u.Celular,
                 Direccion = u.DireccionEmpresa,
                FechaNacimiento = u.FechaNacimiento,
                FechaCreacion = u.FechaCreacion,
                Rol = db.AliadoPortalRoles.Where(r => db.AliadoPortalUsuariosRoles.Any(ur => ur.IdUsuario == u.IdUsuario && ur.IdRol == r.IdRol)).Select(r => r.Nombre).FirstOrDefault() ?? "Sin rol",
                Aliado = db.VendedoresBackOffice.Where(v => v.IdVendedor == u.IdVendedor).Select(v => v.Nombre).FirstOrDefault(),
                CodigoReferencia = db.VendedoresBackOffice.Where(v => v.IdVendedor == u.IdVendedor).Select(v => v.CodigoReferencia).FirstOrDefault(),
                PorcentajeComision = db.VendedoresBackOffice.Where(v => v.IdVendedor == u.IdVendedor || v.IdUsuarioCreacion == u.IdUsuario).Select(v => (decimal?)v.PorcentajeBase).FirstOrDefault()
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

    public async Task<(bool Success, string Message)> ActualizarPerfilUsuarioAliadoAsync(
        int actorId, int idUsuario, string nombres, string apellidos, string email,
        string? identificacion, DateTime? fechaNacimiento, string? avatarUrl, decimal porcentajeComision,
        bool esAdministradorPortal, int tipoCliente = 1, string? razonSocial = null,
        string? nombreComercial = null, string? tipoDocumento = null, string? celular = null,
        string? direccion = null, bool validarDatosFiscales = false)
    {
        await EnsureSchemaAsync();
        tipoDocumento = (tipoDocumento ?? "CEDULA").Trim().ToUpperInvariant();
        razonSocial = razonSocial?.Trim();
        nombreComercial = nombreComercial?.Trim();
        celular = celular?.Trim();
        direccion = direccion?.Trim();
        nombres = nombres.Trim();
        apellidos = apellidos.Trim();
        if (tipoCliente == 2)
        {
            nombres = razonSocial ?? nombres;
            apellidos = string.Empty;
        }
        email = email.Trim();
        identificacion = identificacion?.Trim();
        if (!MailAddress.TryCreate(email, out _))
            return (false, "Ingresa un correo válido.");
        if (tipoCliente == 2 && (string.IsNullOrWhiteSpace(razonSocial) || razonSocial.Length < 3))
            return (false, "La razón social es obligatoria.");
        if (tipoCliente != 2 && (nombres.Length < 2 || apellidos.Length < 2))
            return (false, "Ingresa nombres y apellidos válidos.");
        var validacionDatos = ValidarDatosAliado(tipoCliente, tipoDocumento, nombres, apellidos, razonSocial, nombreComercial, email, identificacion, celular, direccion, null, validarDatosFiscales);
        if (validacionDatos is not null)
            return (false, validacionDatos);
        if (!TryNormalizarPorcentaje(porcentajeComision, out porcentajeComision))
            return (false, "La comisión debe estar entre 0 y 100.");

        await using var db = await _dbFactory.CreateDbContextAsync();
        if (!await EsAdministradorInternoAsync(db, actorId))
            return (false, "No tienes permisos para modificar esta cuenta.");
        var usuario = await db.Usuarios.FirstOrDefaultAsync(u =>
            u.IdUsuario == idUsuario && db.AliadoPortalUsuariosRoles.Any(ur => ur.IdUsuario == u.IdUsuario));
        if (usuario is null)
            return (false, "La cuenta del Portal de Aliados no existe.");
        if (await db.Usuarios.AnyAsync(u => u.IdUsuario != idUsuario && u.Email.ToLower() == email.ToLower()))
            return (false, "Ya existe una cuenta con ese correo.");

        var rolActual = await db.AliadoPortalUsuariosRoles
            .Where(x => x.IdUsuario == idUsuario)
            .Join(db.AliadoPortalRoles, x => x.IdRol, x => x.IdRol, (_, rol) => rol.Nombre)
            .FirstOrDefaultAsync();
        var esAdministradorActual = string.Equals(rolActual, AdminRoleName, StringComparison.OrdinalIgnoreCase);
        if (esAdministradorPortal != esAdministradorActual)
        {
            var actorEsSuperAdministrador = await db.Usuarios
                .Where(x => x.IdUsuario == actorId)
                .Select(x => x.IdTipoUsuario == BackOfficePermissionHelper.SuperAdministradorRoleId)
                .FirstOrDefaultAsync();
            if (!actorEsSuperAdministrador)
                return (false, "Solo un superadministrador puede cambiar el rol de una cuenta.");
            if (actorId == idUsuario)
                return (false, "No puedes cambiar tu propio rol administrativo.");

            var nuevoRol = esAdministradorPortal ? AdminRoleName : RoleName;
            var idTipoNuevo = await db.TipoUsuario
                .Where(x => x.NombreTipo == nuevoRol && x.Estado == true)
                .Select(x => (int?)x.IdTipoUsuario)
                .FirstOrDefaultAsync();
            var idRolNuevo = await db.AliadoPortalRoles
                .Where(x => x.Nombre == nuevoRol && x.Activo)
                .Select(x => (int?)x.IdRol)
                .FirstOrDefaultAsync();
            if (!idTipoNuevo.HasValue || !idRolNuevo.HasValue)
                return (false, "No se encontró la configuración del rol seleccionado.");

            usuario.IdTipoUsuario = idTipoNuevo.Value;
            var asignacionRol = await db.AliadoPortalUsuariosRoles.FirstAsync(x => x.IdUsuario == idUsuario);
            asignacionRol.IdRol = idRolNuevo.Value;

            if (!esAdministradorPortal && !usuario.IdVendedor.HasValue)
            {
                var aliadoNuevo = new VendedorBackOffice
                {
                    Nombre = $"{nombres} {apellidos}".Trim(),
                    CodigoReferencia = await GenerarCodigoAsync(db, nombres),
                    Activo = true,
                    EsSistema = false,
                    PorcentajeBase = porcentajeComision,
                    IdUsuarioCreacion = actorId,
                    FechaCreacion = DateTime.Now
                };
                db.VendedoresBackOffice.Add(aliadoNuevo);
                await db.SaveChangesAsync();
                usuario.IdVendedor = aliadoNuevo.IdVendedor;
            }
        }

        usuario.Nombres = nombres;
        usuario.Apellidos = apellidos;
        usuario.Email = email;
        usuario.Identificacion = identificacion;
        usuario.IdTipoIdentificacion = ObtenerIdTipoIdentificacion(tipoDocumento);
        usuario.TipoCliente = tipoCliente;
        usuario.NombreEmpresa = tipoCliente == 2 ? nombreComercial : string.Empty;
        usuario.DireccionEmpresa = direccion;
        usuario.Celular = celular;
        usuario.FechaNacimiento = fechaNacimiento;
        usuario.AvatarUrl = string.IsNullOrWhiteSpace(avatarUrl) ? null : avatarUrl;
        var aliado = usuario.IdVendedor.HasValue
            ? await db.VendedoresBackOffice.FirstOrDefaultAsync(v => v.IdVendedor == usuario.IdVendedor)
            : await db.VendedoresBackOffice.FirstOrDefaultAsync(v => v.IdUsuarioCreacion == usuario.IdUsuario && !v.EsSistema);
        if (aliado is not null)
        {
            aliado.Nombre = (tipoCliente == 2 ? nombreComercial : $"{nombres} {apellidos}")?.Trim() ?? nombres;
            aliado.PorcentajeBase = porcentajeComision;
        }
        await db.SaveChangesAsync();
        await _auditService.TryRegistrarAuditoriaAsync(actorId, "MODIFICAR", new { idUsuario }, new { nombres, apellidos, email, esAdministradorPortal, porcentajeComision }, new { Modulo = "PortalAliados", Entidad = "Usuario" });
        return (true, "Usuario actualizado correctamente.");
    }

    public async Task<(bool Success, string Message)> DesbloquearUsuarioAliadoAsync(int actorId, int idUsuario)
    {
        await EnsureSchemaAsync();
        await using var db = await _dbFactory.CreateDbContextAsync();
        if (!await EsAdministradorInternoAsync(db, actorId))
            return (false, "No tienes permisos para desbloquear esta cuenta.");
        var usuario = await db.Usuarios.FirstOrDefaultAsync(u =>
            u.IdUsuario == idUsuario && db.AliadoPortalUsuariosRoles.Any(ur => ur.IdUsuario == u.IdUsuario));
        if (usuario is null)
            return (false, "La cuenta del Portal de Aliados no existe.");
        usuario.CuentaBloqueada = false;
        usuario.IntentosFallidos = 0;
        usuario.FechaDesbloqueo = null;
        await db.SaveChangesAsync();
        await _auditService.TryRegistrarAuditoriaAsync(actorId, "DESBLOQUEAR", new { idUsuario }, new { Bloqueado = false }, new { Modulo = "PortalAliados", Entidad = "Usuario" });
        return (true, "Usuario desbloqueado correctamente.");
    }

    public async Task<(bool Success, string Message)> ResetearClaveUsuarioAliadoAsync(int actorId, int idUsuario)
    {
        await EnsureSchemaAsync();
        await using var db = await _dbFactory.CreateDbContextAsync();
        if (!await EsAdministradorInternoAsync(db, actorId))
            return (false, "No tienes permisos para resetear esta cuenta.");
        var usuario = await db.Usuarios.FirstOrDefaultAsync(u =>
            u.IdUsuario == idUsuario && db.AliadoPortalUsuariosRoles.Any(ur => ur.IdUsuario == u.IdUsuario));
        if (usuario is null)
            return (false, "La cuenta del Portal de Aliados no existe.");

        const string claveInicial = "00000000";
        const int minutosExpira = RecoveryCodeHelper.MinutosExpiracionPorDefecto;
        var codigoAcceso = RecoveryCodeHelper.GenerarCodigoNumerico();
        usuario.PasswordHash = SecurityHelper.HashPassword(claveInicial);
        usuario.ClaveTemporal = true;
        usuario.CuentaBloqueada = false;
        usuario.IntentosFallidos = 0;
        usuario.FechaDesbloqueo = null;
        usuario.FechaExpiracionToken = DateTime.Now.AddMinutes(minutosExpira);
        usuario.TokenRecuperacion = SecurityHelper.HashPassword(codigoAcceso);
        await db.SaveChangesAsync();
        await _auditService.TryRegistrarAuditoriaAsync(actorId, "RESETEAR_CLAVE", new { idUsuario }, new { ClaveTemporal = true }, new { Modulo = "PortalAliados", Entidad = "Usuario" });
        try
        {
            await _emailService.EnviarClaveTemporal(usuario.Email, codigoAcceso, minutosExpira, "Reseteo de contraseña del Portal de Aliados");
            return (true, "Contraseña restablecida. El usuario debe ingresar con '00000000' y usar el código enviado a su correo.");
        }
        catch
        {
            return (true, $"La contraseña fue restablecida, pero el correo no pudo enviarse. Código de acceso manual: {codigoAcceso}.");
        }
    }

    public async Task<(bool Success, string Message)> LimpiarAvatarUsuariosAliadosAsync(int actorId, string avatarUrl)
    {
        await EnsureSchemaAsync();
        await using var db = await _dbFactory.CreateDbContextAsync();
        if (!await EsAdministradorInternoAsync(db, actorId))
            return (false, "No tienes permisos para modificar avatares.");
        var avatarSinBarra = avatarUrl.TrimStart('/');
        var usadoFueraDelPortal = await db.Usuarios.AnyAsync(u =>
            (u.AvatarUrl == avatarUrl || u.AvatarUrl == avatarSinBarra) &&
            !db.AliadoPortalUsuariosRoles.Any(ur => ur.IdUsuario == u.IdUsuario));
        if (usadoFueraDelPortal)
            return (false, "El avatar está siendo utilizado por una cuenta de otro módulo y no puede eliminarse desde el Portal de Aliados.");
        var usuarios = await db.Usuarios
            .Where(u => (u.AvatarUrl == avatarUrl || u.AvatarUrl == avatarSinBarra) &&
                        db.AliadoPortalUsuariosRoles.Any(ur => ur.IdUsuario == u.IdUsuario))
            .ToListAsync();
        foreach (var usuario in usuarios)
            usuario.AvatarUrl = null;
        await db.SaveChangesAsync();
        return (true, "Avatar eliminado correctamente.");
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

    public async Task<(bool Success, string Message)> CrearCuentaAliadoAsync(
        int actorId, string nombre, string email, string password, decimal porcentajeBase = 30m,
        bool esAdministradorPortal = false, string? apellidos = null, string? identificacion = null,
        DateTime? fechaNacimiento = null, string? avatarUrl = null, int tipoCliente = 1,
        string? razonSocial = null, string? nombreComercial = null, string? tipoDocumento = null,
        string? celular = null, string? direccion = null, bool validarDatosFiscales = false)
    {
        await EnsureSchemaAsync();
        tipoDocumento = (tipoDocumento ?? "CEDULA").Trim().ToUpperInvariant();
        razonSocial = razonSocial?.Trim();
        nombreComercial = nombreComercial?.Trim();
        celular = celular?.Trim();
        direccion = direccion?.Trim();
        nombre = nombre.Trim();
        apellidos = apellidos?.Trim();
        if (tipoCliente == 2)
        {
            nombre = razonSocial ?? nombre;
            apellidos = string.Empty;
        }
        email = email.Trim();
        password = password.Trim();
        if (string.IsNullOrWhiteSpace(nombre) || string.IsNullOrWhiteSpace(email) || password.Length < 8)
            return (false, "Nombre, correo y una clave de al menos 8 caracteres son obligatorios.");
        if (nombre.Length > 120 || email.Length > 254 || !MailAddress.TryCreate(email, out _))
            return (false, "El nombre o el correo no tienen un formato válido.");
        if (!TryNormalizarPorcentaje(porcentajeBase, out porcentajeBase))
            return (false, "El porcentaje debe estar entre 0 y 100.");
        var validacionDatos = ValidarDatosAliado(tipoCliente, tipoDocumento, nombre, apellidos ?? string.Empty, razonSocial, nombreComercial, email, identificacion, celular, direccion, password, validarDatosFiscales);
        if (validacionDatos is not null)
            return (false, validacionDatos);

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
                Nombre = (tipoCliente == 2 ? nombreComercial : $"{nombre} {apellidos}")?.Trim() ?? nombre,
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
            Apellidos = apellidos ?? string.Empty,
            Email = email,
            PasswordHash = SecurityHelper.HashPassword(password),
            IdTipoUsuario = roleId,
            IdVendedor = aliado?.IdVendedor,
            Estado = true,
            ClaveTemporal = true,
            CuentaBloqueada = false,
            AvatarUrl = string.IsNullOrWhiteSpace(avatarUrl) ? null : avatarUrl,
            Identificacion = identificacion?.Trim(),
            IdTipoIdentificacion = ObtenerIdTipoIdentificacion(tipoDocumento),
            TipoCliente = tipoCliente,
            NombreEmpresa = tipoCliente == 2 ? nombreComercial : string.Empty,
            DireccionEmpresa = direccion,
            Celular = celular,
            FechaNacimiento = fechaNacimiento,
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
        var mensaje = esAdministradorPortal
            ? "Cuenta creada correctamente con el rol Administrador Portal de Aliados."
            : "Cuenta de aliado creada correctamente con el rol Aliado Comercial.";
        try
        {
            await _emailService.EnviarCuentaCreadaAsync(email, $"{nombre} {apellidos}".Trim(), password);
        }
        catch
        {
            mensaje += " No se pudo enviar el correo de bienvenida.";
        }
        return (true, mensaje);
    }

    public async Task<(bool Success, string Message)> CrearCuentaClienteAliadoAsync(
        int aliadoUserId,
        AliadoClienteRegistroDto registro)
    {
        await EnsureSchemaAsync();
        var contexto = await ObtenerContextoAsync(aliadoUserId);
        if (contexto is null)
            return (false, "No tienes permisos para crear cuentas de clientes.");

        registro.Normalizar();
        var validacion = registro.Validar();
        if (validacion is not null)
            return (false, validacion);

        await using var db = await _dbFactory.CreateDbContextAsync();
        var idVendedor = contexto.IdVendedor;
        if (contexto.EsAdministrador)
        {
            if (registro.IdVendedorDestino is not > 0)
                return (false, "Selecciona el aliado al que se asociará el cliente.");

            var aliadosDisponibles = (await ListarAliadosAsync())
                .Select(x => x.IdVendedor)
                .ToHashSet();
            if (!await db.VendedoresBackOffice.AnyAsync(x => x.IdVendedor == registro.IdVendedorDestino.Value && !x.EsSistema && x.Activo) ||
                !aliadosDisponibles.Contains(registro.IdVendedorDestino.Value))
                return (false, "El aliado seleccionado no está disponible.");

            idVendedor = registro.IdVendedorDestino.Value;
        }

        if (idVendedor <= 0)
            return (false, "No se pudo determinar el aliado asociado.");

        if (await db.Usuarios.AnyAsync(x => x.Email.ToLower() == registro.Email.ToLower()))
            return (false, "Ya existe una cuenta con ese correo.");
        if (await db.Usuarios.AnyAsync(x => x.Identificacion == registro.Identificacion))
            return (false, "Ya existe una cuenta con esa identificación.");

        await using var transaction = await db.Database.BeginTransactionAsync();
        var usuario = new Usuario
        {
            Nombres = registro.TipoCliente == 2 ? registro.RazonSocial : registro.Nombres,
            Apellidos = registro.TipoCliente == 2 ? string.Empty : registro.Apellidos,
            NombreEmpresa = registro.TipoCliente == 2 ? registro.RazonSocial : string.Empty,
            Email = registro.Email,
            DireccionEmpresa = registro.Direccion,
            Celular = registro.Celular,
            AvatarUrl = string.IsNullOrWhiteSpace(registro.AvatarUrl) ? null : registro.AvatarUrl,
            Identificacion = registro.Identificacion,
            IdTipoIdentificacion = registro.TipoDocumento switch
            {
                "RUC" => 2,
                "PASAPORTE" or "EXTERIOR" => 3,
                _ => 1
            },
            PasswordHash = SecurityHelper.HashPassword(registro.Password),
            IdTipoUsuario = 1,
            Estado = true,
            ClaveTemporal = true,
            FechaCreacion = DateTime.Now,
            SaldoDocumentos = 5,
            TipoCliente = registro.TipoCliente,
            IdVendedor = idVendedor,
            estadoAsociado = true
        };
        db.Usuarios.Add(usuario);
        await db.SaveChangesAsync();

        db.Clientes.Add(new Cliente
        {
            Nombres = registro.TipoCliente == 2 ? registro.RazonSocial : registro.Nombres,
            Apellidos = registro.TipoCliente == 2 ? string.Empty : registro.Apellidos,
            Nombrerazonsocial = registro.TipoCliente == 2 ? registro.RazonSocial : null,
            Nombrecomercial = registro.TipoCliente == 2 ? registro.RazonSocial : null,
            Tipoidentificacion = registro.TipoDocumento,
            Numeroidentificacion = registro.Identificacion,
            Direccion = registro.Direccion,
            Celular = registro.Celular,
            Correo = registro.Email,
            TipoCliente = registro.TipoCliente,
            Usuario = usuario.IdUsuario,
            Idvendedor = idVendedor,
            Estado = true,
            Fechaingreso = DateOnly.FromDateTime(DateTime.Today)
        });
        await db.SaveChangesAsync();
        await transaction.CommitAsync();

        await _auditService.TryRegistrarAuditoriaAsync(
            aliadoUserId,
            "CREAR",
            null,
            new { usuario.IdUsuario, usuario.Email, usuario.IdVendedor, ClaveTemporal = true },
            new { Modulo = "PortalAliados", Entidad = "CuentaCliente" });
        try
        {
            await _emailService.EnviarCuentaCreadaAsync(usuario.Email, usuario.Nombres, registro.Password);
        }
        catch
        {
        }

        return (true, "Cuenta creada. El cliente deberá cambiar la clave en su primer ingreso.");
    }

    private async Task<List<FacturaPortalRow>> ObtenerFacturasAsync(int idVendedor, int? idCliente = null, IReadOnlyCollection<int>? vendedores = null)
    {
        await using var db = await _dbFactory.CreateDbContextAsync();
        return await db.Facturas
            .AsNoTracking()
            .Where(x => (vendedores == null ? x.Idvendedor == idVendedor : x.Idvendedor.HasValue && vendedores.Contains(x.Idvendedor.Value)) && (!idCliente.HasValue || x.Codclientes == idCliente) && (x.Estado == true || x.Estado == null))
            .OrderByDescending(x => x.Fchautorizacion ?? x.Fechaentrega)
            .Select(x => new FacturaPortalRow
            {
                IdFactura = x.Codfactura,
                Aliado = db.VendedoresBackOffice.Where(v => v.IdVendedor == x.Idvendedor).Select(v => v.Nombre).FirstOrDefault() ?? string.Empty,
                PorcentajeBase = db.VendedoresBackOffice.Where(v => v.IdVendedor == x.Idvendedor).Select(v => v.PorcentajeBase).FirstOrDefault(),
                IdCliente = x.Codclientes,
                IdentificacionCliente = x.CodclientesNavigation == null ? null : x.CodclientesNavigation.Numeroidentificacion,
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

    private static void MarcarComprasRepetidas(IEnumerable<FacturaPortalRow> facturas)
    {
        var comprasClienteServicio = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var factura in facturas.OrderBy(x => x.Fecha).ThenBy(x => x.IdFactura))
        {
            if (factura.IdCliente is not > 0 || string.IsNullOrWhiteSpace(factura.Producto))
                continue;
            var clave = $"{factura.IdCliente.Value}|{AliadoServicioHelper.Normalizar(factura.Producto)}";
            factura.EsCompraRepetida = !comprasClienteServicio.Add(clave);
            factura.EsRenovacion = factura.EsCompraRepetida || factura.FechaVencimiento.HasValue;
        }
    }

    private static AliadoRenovacionDto ToRenovacion(FacturaPortalRow factura, AliadoRenovacionGestion? gestion, decimal porcentajeBase) => new()
    {
        IdFactura = factura.IdFactura,
        IdCliente = factura.IdCliente ?? 0,
        Aliado = factura.Aliado,
        Cliente = factura.Cliente,
        Producto = factura.Producto,
        FechaVencimiento = factura.FechaVencimiento ?? factura.Fecha,
        DiasRestantes = factura.FechaVencimiento.HasValue ? (factura.FechaVencimiento.Value.Date - DateTime.Today).Days : 0,
        Valor = factura.Total,
        ComisionPotencial = AliadoComisionCalculationService.CalcularValor(AliadoComisionCalculationService.ObtenerBaseNeta(factura.Subtotal, factura.Subtotal0, factura.Subtotal12), porcentajeBase),
        EstadoGestion = gestion?.Resultado ?? "Pendiente",
        UltimaGestion = gestion?.FechaGestion,
        Observacion = gestion?.Observacion,
        ProximoSeguimiento = gestion?.ProximoSeguimiento,
        EsCompraRepetida = factura.EsCompraRepetida
    };

    private static AliadoRenovacionDto ToRenovacionPorSaldo(FacturaPortalRow factura, AliadoRenovacionGestion? gestion, decimal porcentajeBase, int saldoDocumentos) => new()
    {
        IdFactura = factura.IdFactura,
        IdCliente = factura.IdCliente ?? 0,
        Aliado = factura.Aliado,
        Cliente = factura.Cliente,
        Producto = factura.Producto,
        FechaVencimiento = DateTime.MaxValue,
        DiasRestantes = int.MaxValue,
        Valor = factura.Total,
        ComisionPotencial = AliadoComisionCalculationService.CalcularValor(AliadoComisionCalculationService.ObtenerBaseNeta(factura.Subtotal, factura.Subtotal0, factura.Subtotal12), porcentajeBase),
        EstadoGestion = gestion?.Resultado ?? "Pendiente",
        UltimaGestion = gestion?.FechaGestion,
        Observacion = gestion?.Observacion,
        ProximoSeguimiento = gestion?.ProximoSeguimiento,
        EsPorSaldo = true,
        EsCompraRepetida = factura.EsCompraRepetida,
        SaldoDocumentos = Math.Max(saldoDocumentos, 0)
    };

    private static bool EsRenovacionVisible(FacturaPortalRow factura, DateTime hoy, int diasDesde, int diasHasta)
        => (factura.EsCompraRepetida
                ? factura.Fecha.Date >= hoy.AddDays(diasDesde) && factura.Fecha.Date <= hoy.AddDays(diasHasta)
                : factura.FechaVencimiento.HasValue && factura.FechaVencimiento.Value.Date >= hoy.AddDays(diasDesde) && factura.FechaVencimiento.Value.Date <= hoy.AddDays(diasHasta)) &&
           factura.Autorizado &&
           EsPagoConfirmado(factura.EstadoPago);

    public async Task<IReadOnlyList<AliadoLiquidacionDto>> ObtenerLiquidacionesAsync(int userId)
    {
        await EnsureSchemaAsync();
        var contexto = await ObtenerContextoAsync(userId);
        if (contexto is null)
            return Array.Empty<AliadoLiquidacionDto>();

        if (contexto.EsAdministrador)
            return (await ObtenerLiquidacionesAdministracionAsync(userId)).Cast<AliadoLiquidacionDto>().ToList();

        await using var db = await _dbFactory.CreateDbContextAsync();
        return await db.AliadoLiquidaciones.AsNoTracking()
            .Where(x => x.IdVendedor == contexto.IdVendedor)
            .OrderByDescending(x => x.Fecha)
            .Select(x => new AliadoLiquidacionDto
            {
                IdLiquidacion = x.IdLiquidacion,
                Aliado = db.VendedoresBackOffice
                    .Where(v => v.IdVendedor == x.IdVendedor)
                    .Select(v => v.Nombre)
                    .FirstOrDefault() ?? string.Empty,
                Periodo = x.Periodo,
                Total = x.Total,
                Fecha = x.Fecha,
                Estado = x.Estado,
                ReferenciaPago = x.ReferenciaPago,
                EstadoSri = x.EstadoSri,
                ErrorSri = x.ErrorSri,
                CodLiquidacionCompra = x.CodLiquidacionCompra,
                CodClave = db.ComprasFacturas
                    .Where(c => c.CodFactura == x.CodLiquidacionCompra)
                    .Select(c => c.CodClave)
                    .FirstOrDefault(),
                FechaPago = x.FechaPago,
                ObservacionPago = x.ObservacionPago,
                ComprobantePagoUrl = x.ComprobantePagoUrl,
                IdUsuarioPago = x.IdUsuarioPago,
                CantidadComisiones = db.AliadoComisiones.Count(c => c.IdLiquidacion == x.IdLiquidacion)
            })
            .ToListAsync();
    }

    public async Task<string?> AsegurarXmlLiquidacionAsync(int userId, int idLiquidacion)
    {
        var documento = await ObtenerDocumentoLiquidacionAsync(userId, idLiquidacion);
        return documento is null
            ? null
            : await _liquidacionCompraService.AsegurarXmlLiquidacionUsuarioAsync(documento.Value.CodFactura, documento.Value.IdUsuario);
    }

    public async Task<string?> AsegurarPdfLiquidacionAsync(int userId, int idLiquidacion)
    {
        var documento = await ObtenerDocumentoLiquidacionAsync(userId, idLiquidacion);
        return documento is null
            ? null
            : await _liquidacionCompraService.AsegurarPdfLiquidacionUsuarioAsync(documento.Value.CodFactura, documento.Value.IdUsuario);
    }

    private async Task<(int CodFactura, int IdUsuario)?> ObtenerDocumentoLiquidacionAsync(int userId, int idLiquidacion)
    {
        if (idLiquidacion <= 0)
            return null;

        var contexto = await ObtenerContextoAsync(userId);
        if (contexto is null)
            return null;

        await using var db = await _dbFactory.CreateDbContextAsync();
        var documento = await (
            from liquidacion in db.AliadoLiquidaciones.AsNoTracking()
            join compra in db.ComprasFacturas.AsNoTracking()
                on liquidacion.CodLiquidacionCompra equals compra.CodFactura
            where liquidacion.IdLiquidacion == idLiquidacion &&
                  liquidacion.CodLiquidacionCompra.HasValue &&
                  compra.Estado == true &&
                  compra.CodDocumento == "03" &&
                  compra.Usuario.HasValue &&
                  (contexto.EsAdministrador || liquidacion.IdVendedor == contexto.IdVendedor)
            select new
            {
                compra.CodFactura,
                IdUsuario = compra.Usuario.Value
            })
            .FirstOrDefaultAsync();

        return documento is null ? null : (documento.CodFactura, documento.IdUsuario);
    }

    public async Task<IReadOnlyList<AliadoAdminLiquidacionRow>> ObtenerLiquidacionesAdministracionAsync(int actorId)
    {
        await EnsureSchemaAsync();
        await using var db = await _dbFactory.CreateDbContextAsync();
        if (!await EsAdministradorPortalAsync(db, actorId))
            return Array.Empty<AliadoAdminLiquidacionRow>();
        return await db.AliadoLiquidaciones.AsNoTracking()
            .Join(db.VendedoresBackOffice.AsNoTracking(), l => l.IdVendedor, v => v.IdVendedor, (l, v) => new { l, v })
            .OrderByDescending(x => x.l.Fecha)
            .Select(x => new AliadoAdminLiquidacionRow
            {
                IdLiquidacion = x.l.IdLiquidacion,
                Aliado = x.v.Nombre,
                Periodo = x.l.Periodo,
                Total = x.l.Total,
                Fecha = x.l.Fecha,
                Estado = x.l.Estado,
                ReferenciaPago = x.l.ReferenciaPago,
                EstadoSri = x.l.EstadoSri,
                ErrorSri = x.l.ErrorSri,
                CodLiquidacionCompra = x.l.CodLiquidacionCompra,
                CodClave = db.ComprasFacturas
                    .Where(c => c.CodFactura == x.l.CodLiquidacionCompra)
                    .Select(c => c.CodClave)
                    .FirstOrDefault(),
                FechaPago = x.l.FechaPago,
                IdUsuarioPago = x.l.IdUsuarioPago,
                ObservacionPago = x.l.ObservacionPago,
                ComprobantePagoUrl = x.l.ComprobantePagoUrl,
                CantidadComisiones = db.AliadoComisiones.Count(c => c.IdLiquidacion == x.l.IdLiquidacion)
            }).ToListAsync();
    }

    public async Task<IReadOnlyList<AliadoComisionMovimientoDto>> ObtenerDetalleLiquidacionAsync(int userId, int idLiquidacion)
    {
        await EnsureSchemaAsync();
        var contexto = await ObtenerContextoAsync(userId);
        if (contexto is null || idLiquidacion <= 0)
            return Array.Empty<AliadoComisionMovimientoDto>();

        await using var db = await _dbFactory.CreateDbContextAsync();
        var pertenece = await db.AliadoLiquidaciones.AsNoTracking()
            .AnyAsync(x => x.IdLiquidacion == idLiquidacion && (contexto.EsAdministrador || x.IdVendedor == contexto.IdVendedor));
        if (!pertenece)
            return Array.Empty<AliadoComisionMovimientoDto>();

        return await db.AliadoComisiones.AsNoTracking()
            .Where(x => x.IdLiquidacion == idLiquidacion && (contexto.EsAdministrador || x.IdVendedor == contexto.IdVendedor))
            .OrderByDescending(x => x.FechaGeneracion)
            .Select(x => new AliadoComisionMovimientoDto
            {
                IdComision = x.IdComision,
                IdFactura = x.IdFactura,
                Aliado = db.VendedoresBackOffice
                    .Where(v => v.IdVendedor == x.IdVendedor)
                    .Select(v => v.Nombre)
                    .FirstOrDefault() ?? string.Empty,
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
                 EstadoPago = db.AliadoLiquidaciones
                     .Where(l => l.IdLiquidacion == x.IdLiquidacion)
                     .Select(l => l.Estado)
                     .FirstOrDefault(),
                 ReferenciaPago = db.AliadoLiquidaciones
                     .Where(l => l.IdLiquidacion == x.IdLiquidacion)
                     .Select(l => l.ReferenciaPago)
                     .FirstOrDefault()
            })
            .ToListAsync();
    }

    public async Task SincronizarComisionesPortalAsync()
    {
        await EnsureSchemaAsync();
        await _aliadoComisionGenerationService.SincronizarPortalAsync();
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

    private Task SincronizarComisionesAsync(int idVendedor)
        => _aliadoComisionGenerationService.SincronizarAsync(idVendedor);

    private Task CerrarPeriodosComisionesAsync()
        => _aliadoComisionGenerationService.CerrarPeriodosAsync();

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
        if (actor?.IdTipoUsuario is BackOfficePermissionHelper.SuperAdministradorRoleId or BackOfficePermissionHelper.BackOfficeRoleId)
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

    private static int ObtenerIdTipoIdentificacion(string? tipoDocumento) =>
        tipoDocumento?.Trim().ToUpperInvariant() switch
        {
            "RUC" => 2,
            "PASAPORTE" or "EXTERIOR" => 3,
            _ => 1
        };

    private static string? ValidarDatosAliado(
        int tipoCliente, string tipoDocumento, string nombres, string apellidos,
        string? razonSocial, string? nombreComercial, string email, string? identificacion,
        string? celular, string? direccion, string? password, bool requerirDatosFiscales)
    {
        if (!requerirDatosFiscales)
            return null;
        if (tipoCliente is not 1 and not 2)
            return "Selecciona un tipo de cliente válido.";
        if (!MailAddress.TryCreate(email, out _))
            return "Ingresa un correo válido.";
        if (tipoCliente == 2)
        {
            if (tipoDocumento != "RUC")
                return "Las empresas deben registrarse con RUC.";
            if (string.IsNullOrWhiteSpace(razonSocial) || razonSocial.Length is < 3 or > 150)
                return "La razón social debe tener entre 3 y 150 caracteres.";
            if (string.IsNullOrWhiteSpace(nombreComercial) || nombreComercial.Length is < 2 or > 150)
                return "El nombre comercial debe tener entre 2 y 150 caracteres.";
        }
        else if (!EsNombreValidoAliado(nombres) || !EsNombreValidoAliado(apellidos))
        {
            return "Ingresa nombres y apellidos válidos.";
        }

        if (string.IsNullOrWhiteSpace(identificacion))
            return "La identificación es obligatoria.";
        var identificacionNormalizada = identificacion.Trim();
        if (tipoDocumento == "RUC")
        {
            if (identificacionNormalizada.Length != 13 || !identificacionNormalizada.All(char.IsDigit) ||
                !identificacionNormalizada.EndsWith("001", StringComparison.Ordinal) ||
                !ValidarCedulaAliado(identificacionNormalizada[..10]))
                return "El RUC debe tener 13 dígitos y terminar en 001.";
        }
        else if (tipoDocumento == "CEDULA")
        {
            if (identificacionNormalizada.Length != 10 || !identificacionNormalizada.All(char.IsDigit) || !ValidarCedulaAliado(identificacionNormalizada))
                return "La cédula debe tener 10 dígitos.";
        }
        else if (identificacionNormalizada.Length is < 3 or > 20 || !identificacionNormalizada.All(char.IsLetterOrDigit))
        {
            return "La identificación debe tener entre 3 y 20 caracteres alfanuméricos.";
        }

        if (string.IsNullOrWhiteSpace(celular) || celular.Count(char.IsDigit) is < 7 or > 15)
            return "El teléfono debe tener entre 7 y 15 dígitos.";
        if (string.IsNullOrWhiteSpace(direccion) || direccion.Length is < 5 or > 100)
            return "La dirección debe tener entre 5 y 100 caracteres.";
        if (password is not null && !System.Text.RegularExpressions.Regex.IsMatch(password, @"^(?=.*[a-z])(?=.*[A-Z])(?=.*\d)(?=.*[^\da-zA-Z]).{8,}$"))
            return "La clave debe tener mínimo 8 caracteres, mayúscula, minúscula, número y carácter especial.";
        return null;
    }

    private static bool EsNombreValidoAliado(string valor) =>
        valor.Length >= 2 && System.Text.RegularExpressions.Regex.IsMatch(valor, @"^[a-zA-ZÀ-ÿ\s]{2,}$");

    private static bool ValidarCedulaAliado(string cedula)
    {
        if (cedula.Length != 10 || !cedula.All(char.IsDigit))
            return false;
        var provincia = int.Parse(cedula[..2]);
        var tercerDigito = int.Parse(cedula[2].ToString());
        if (provincia is < 1 or > 24 || tercerDigito > 5)
            return false;
        var suma = 0;
        for (var i = 0; i < 9; i++)
        {
            var valor = int.Parse(cedula[i].ToString()) * (i % 2 == 0 ? 2 : 1);
            suma += valor > 9 ? valor - 9 : valor;
        }
        return (10 - suma % 10) % 10 == int.Parse(cedula[9].ToString());
    }

    private static bool EsPagoConfirmado(string? estadoPago)
        => estadoPago?.Trim().ToUpperInvariant() is "PAGADA" or "PAGADO" or "CANCELADA" or "CANCELADO" or "COBRADA" or "COBRADO";

    private static string NormalizarIdentificacionCliente(string? identificacion)
        => string.IsNullOrWhiteSpace(identificacion)
            ? string.Empty
            : new string(identificacion.Where(char.IsLetterOrDigit).ToArray());

    private static bool EsCorreoDisponible(string? correo)
        => !string.IsNullOrWhiteSpace(correo) &&
           !correo.EndsWith("@deleted.local", StringComparison.OrdinalIgnoreCase);

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
INSERT dbo.ALIADO_PORTAL_ROL_MENU(IdRol,IdMenu) SELECT r.IdRol,m.IdMenu FROM dbo.ALIADO_PORTAL_ROL r CROSS JOIN dbo.ALIADO_PORTAL_MENU m WHERE ((r.Nombre=N'{RoleName}' AND m.Ruta NOT IN(N'{AdminRoute}',N'{AdminRoute}/usuarios')) OR (r.Nombre=N'{AdminRoleName}' AND m.Ruta IN(N'{AdminRoute}',N'{AdminRoute}/usuarios',N'{RootRoute}/renovaciones',N'{RootRoute}/comisiones',N'{RootRoute}/liquidaciones'))) AND NOT EXISTS(SELECT 1 FROM dbo.ALIADO_PORTAL_ROL_MENU x WHERE x.IdRol=r.IdRol AND x.IdMenu=m.IdMenu);
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
WHERE m.RUTAMENU IN (N'{AdminRoute}', N'{RootRoute}/renovaciones', N'{RootRoute}/comisiones', N'{RootRoute}/liquidaciones')
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
        ,ObservacionPago NVARCHAR(500) NULL
        ,ComprobantePagoUrl NVARCHAR(300) NULL
        ,CodLiquidacionCompra INT NULL
        ,EstadoSri NVARCHAR(30) NOT NULL CONSTRAINT DF_ALIADO_LIQUIDACION_ESTADO_SRI DEFAULT(N'Pendiente')
        ,ErrorSri NVARCHAR(1000) NULL
        ,FechaPago DATETIME2 NULL
        ,IdUsuarioPago INT NULL
    );
END
IF COL_LENGTH('dbo.ALIADO_LIQUIDACION', 'IdCliente') IS NULL ALTER TABLE dbo.ALIADO_LIQUIDACION ADD IdCliente INT NULL;
IF COL_LENGTH('dbo.ALIADO_LIQUIDACION', 'ObservacionPago') IS NULL ALTER TABLE dbo.ALIADO_LIQUIDACION ADD ObservacionPago NVARCHAR(500) NULL;
IF COL_LENGTH('dbo.ALIADO_LIQUIDACION', 'ComprobantePagoUrl') IS NULL ALTER TABLE dbo.ALIADO_LIQUIDACION ADD ComprobantePagoUrl NVARCHAR(300) NULL;
IF COL_LENGTH('dbo.ALIADO_LIQUIDACION', 'CodLiquidacionCompra') IS NULL ALTER TABLE dbo.ALIADO_LIQUIDACION ADD CodLiquidacionCompra INT NULL;
IF COL_LENGTH('dbo.ALIADO_LIQUIDACION', 'EstadoSri') IS NULL ALTER TABLE dbo.ALIADO_LIQUIDACION ADD EstadoSri NVARCHAR(30) NOT NULL CONSTRAINT DF_ALIADO_LIQUIDACION_ESTADO_SRI DEFAULT(N'Pendiente');
IF COL_LENGTH('dbo.ALIADO_LIQUIDACION', 'ErrorSri') IS NULL ALTER TABLE dbo.ALIADO_LIQUIDACION ADD ErrorSri NVARCHAR(1000) NULL;
IF COL_LENGTH('dbo.ALIADO_LIQUIDACION', 'FechaPago') IS NULL ALTER TABLE dbo.ALIADO_LIQUIDACION ADD FechaPago DATETIME2 NULL;
IF COL_LENGTH('dbo.ALIADO_LIQUIDACION', 'IdUsuarioPago') IS NULL ALTER TABLE dbo.ALIADO_LIQUIDACION ADD IdUsuarioPago INT NULL;
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
        ,Periodo NVARCHAR(7) NULL
    );
END
IF COL_LENGTH('dbo.ALIADO_COMISION', 'Periodo') IS NULL ALTER TABLE dbo.ALIADO_COMISION ADD Periodo NVARCHAR(7) NULL;
EXEC(N'UPDATE dbo.ALIADO_COMISION SET Periodo = CONVERT(char(7), FechaGeneracion, 120) WHERE Periodo IS NULL;');
IF OBJECT_ID(N'dbo.ALIADO_COMISION_PARAMETRO', N'U') IS NULL
    CREATE TABLE dbo.ALIADO_COMISION_PARAMETRO (IdParametro INT IDENTITY(1,1) NOT NULL PRIMARY KEY, IdVendedor INT NOT NULL, Periodo NVARCHAR(7) NOT NULL, Porcentaje DECIMAL(9,4) NOT NULL, PorcentajeHastaMil DECIMAL(9,4) NOT NULL CONSTRAINT DF_ALIADO_COMISION_PARAMETRO_HASTA_MIL DEFAULT(5), PorcentajeDesdeMil DECIMAL(9,4) NOT NULL CONSTRAINT DF_ALIADO_COMISION_PARAMETRO_DESDE_MIL DEFAULT(10), FechaActualizacion DATETIME2 NOT NULL, IdUsuarioActualizacion INT NOT NULL, CONSTRAINT UX_ALIADO_COMISION_PARAMETRO UNIQUE(IdVendedor, Periodo));
IF COL_LENGTH('dbo.ALIADO_COMISION_PARAMETRO', 'PorcentajeHastaMil') IS NULL ALTER TABLE dbo.ALIADO_COMISION_PARAMETRO ADD PorcentajeHastaMil DECIMAL(9,4) NOT NULL CONSTRAINT DF_ALIADO_COMISION_PARAMETRO_HASTA_MIL DEFAULT(5);
IF COL_LENGTH('dbo.ALIADO_COMISION_PARAMETRO', 'PorcentajeDesdeMil') IS NULL ALTER TABLE dbo.ALIADO_COMISION_PARAMETRO ADD PorcentajeDesdeMil DECIMAL(9,4) NOT NULL CONSTRAINT DF_ALIADO_COMISION_PARAMETRO_DESDE_MIL DEFAULT(10);
EXEC(N'UPDATE dbo.ALIADO_COMISION_PARAMETRO SET PorcentajeHastaMil = Porcentaje, PorcentajeDesdeMil = Porcentaje WHERE PorcentajeHastaMil = 5 AND PorcentajeDesdeMil = 10;');
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'UX_ALIADO_COMISION_VENDEDOR_FACTURA_TIPO' AND object_id = OBJECT_ID(N'dbo.ALIADO_COMISION'))
    CREATE UNIQUE INDEX UX_ALIADO_COMISION_VENDEDOR_FACTURA_TIPO ON dbo.ALIADO_COMISION (IdVendedor, IdFactura, TipoComision);
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_ALIADO_COMISION_VENDEDOR_ESTADO' AND object_id = OBJECT_ID(N'dbo.ALIADO_COMISION'))
    CREATE INDEX IX_ALIADO_COMISION_VENDEDOR_ESTADO ON dbo.ALIADO_COMISION (IdVendedor, Estado, FechaGeneracion DESC);
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_ALIADO_COMISION_FACTURA_ESTADO' AND object_id = OBJECT_ID(N'dbo.ALIADO_COMISION'))
    CREATE INDEX IX_ALIADO_COMISION_FACTURA_ESTADO ON dbo.ALIADO_COMISION (IdFactura, Estado) INCLUDE(IdVendedor, IdCliente, TipoComision, Valor, IdLiquidacion);
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_ALIADO_COMISION_LIQUIDACION' AND object_id = OBJECT_ID(N'dbo.ALIADO_COMISION'))
    CREATE INDEX IX_ALIADO_COMISION_LIQUIDACION ON dbo.ALIADO_COMISION (IdLiquidacion, Estado);
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'UX_ALIADO_LIQUIDACION_PENDIENTE' AND object_id = OBJECT_ID(N'dbo.ALIADO_LIQUIDACION'))
   AND NOT EXISTS (SELECT 1 FROM dbo.ALIADO_LIQUIDACION WHERE Estado = N'Pendiente' GROUP BY IdVendedor, IdCliente, Periodo HAVING COUNT(*) > 1)
    CREATE UNIQUE INDEX UX_ALIADO_LIQUIDACION_PENDIENTE ON dbo.ALIADO_LIQUIDACION(IdVendedor, IdCliente, Periodo) WHERE Estado = N'Pendiente';
IF NOT EXISTS (SELECT 1 FROM sys.check_constraints WHERE name = N'CK_ALIADO_COMISION_ESTADO')
   AND NOT EXISTS (SELECT 1 FROM dbo.ALIADO_COMISION WHERE Estado NOT IN (N'Generada',N'Pendiente',N'Aprobada',N'Pagada',N'Anulada',N'Revertida',N'AjustePendiente'))
    ALTER TABLE dbo.ALIADO_COMISION ADD CONSTRAINT CK_ALIADO_COMISION_ESTADO CHECK (Estado IN (N'Generada',N'Pendiente',N'Aprobada',N'Pagada',N'Anulada',N'Revertida',N'AjustePendiente'));
IF NOT EXISTS (SELECT 1 FROM sys.check_constraints WHERE name = N'CK_ALIADO_COMISION_PORCENTAJE')
   AND NOT EXISTS (SELECT 1 FROM dbo.ALIADO_COMISION WHERE Porcentaje < 0 OR Porcentaje > 100)
    ALTER TABLE dbo.ALIADO_COMISION ADD CONSTRAINT CK_ALIADO_COMISION_PORCENTAJE CHECK (Porcentaje BETWEEN 0 AND 100);
IF NOT EXISTS (SELECT 1 FROM sys.check_constraints WHERE name = N'CK_ALIADO_LIQUIDACION_ESTADO')
   AND NOT EXISTS (SELECT 1 FROM dbo.ALIADO_LIQUIDACION WHERE Estado NOT IN (N'Pendiente',N'Pagada',N'Anulada'))
    ALTER TABLE dbo.ALIADO_LIQUIDACION ADD CONSTRAINT CK_ALIADO_LIQUIDACION_ESTADO CHECK (Estado IN (N'Pendiente',N'Pagada',N'Anulada'));
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
    public IReadOnlyList<AliadoEnlaceRegistroDto> EnlacesRegistro { get; init; } = Array.Empty<AliadoEnlaceRegistroDto>();
    public DateTime PeriodoDesde { get; init; }
    public DateTime PeriodoHasta { get; init; }
}

public sealed class AliadoEnlaceRegistroDto
{
    public string NombreAliado { get; init; } = string.Empty;
    public string CodigoReferencia { get; init; } = string.Empty;
    public string RutaRegistro { get; init; } = string.Empty;
}

public class AliadoClienteDto
{
    public int IdCliente { get; init; }
    public int? IdUsuario { get; init; }
    public int IdFactura { get; set; }
    public string? Aliado { get; init; }
    public string? Identificacion { get; init; }
    public string Nombre { get; init; } = string.Empty;
    public string? Email { get; set; }
    public string? Telefono { get; init; }
    public int SaldoDocumentos { get; set; }
    public string? Producto { get; set; }
    public DateTime? FechaCompra { get; set; }
    public DateTime? FechaVencimiento { get; set; }
    public string? Estado { get; set; }
    public int TotalCompras { get; set; }
    public int ComprasEFact { get; set; }
    public int ComprasERubrica { get; set; }
    public int? DiasFirmaElectronica { get; set; }
    public DateTime? FechaVencimientoFirmaElectronica { get; set; }
    public string NivelAlerta { get; set; } = "warning";
    public string Alerta { get; set; } = string.Empty;
    public string Servicio => AliadoServicioHelper.Clasificar(Producto);
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
    public string Servicio => AliadoServicioHelper.Clasificar(Producto);
}

public sealed class AliadoRenovacionDto
{
    public int IdFactura { get; init; }
    public int IdCliente { get; init; }
    public string Aliado { get; init; } = string.Empty;
    public string Cliente { get; init; } = string.Empty;
    public string Producto { get; init; } = string.Empty;
    public DateTime FechaVencimiento { get; init; }
    public int DiasRestantes { get; init; }
    public decimal Valor { get; init; }
    public decimal ComisionPotencial { get; init; }
    public string EstadoGestion { get; init; } = "Pendiente";
    public DateTime? UltimaGestion { get; init; }
    public string? Observacion { get; init; }
    public DateTime? ProximoSeguimiento { get; init; }
    public bool EsPorSaldo { get; init; }
    public bool EsCompraRepetida { get; init; }
    public int SaldoDocumentos { get; init; }
    public string? Email { get; init; }
    public string? Telefono { get; init; }
    public string? Identificacion { get; init; }
    public string Servicio { get; init; } = "E-FACT";
    public int? DiasFirmaElectronica { get; init; }
    public DateTime? FechaVencimientoFirmaElectronica { get; init; }
    public string NivelAlerta { get; init; } = "warning";
    public string Alerta { get; init; } = string.Empty;
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
    public string Aliado { get; init; } = string.Empty;
    public string NumeroFactura { get; init; } = string.Empty;
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
    public string? EstadoPago { get; init; }
    public string? ReferenciaPago { get; init; }
    public string Servicio => AliadoServicioHelper.Clasificar(Producto);
}

internal static class AliadoServicioHelper
{
    public static string Clasificar(string? producto)
    {
        var texto = producto ?? string.Empty;
        return texto.Contains("firma", StringComparison.OrdinalIgnoreCase) ||
               texto.Contains("rúbrica", StringComparison.OrdinalIgnoreCase) ||
               texto.Contains("rubrica", StringComparison.OrdinalIgnoreCase) ||
               texto.Contains("e-sign", StringComparison.OrdinalIgnoreCase) ||
               texto.Contains("esign", StringComparison.OrdinalIgnoreCase) ||
               texto.Contains("certificado", StringComparison.OrdinalIgnoreCase)
            ? "E-RÚBRICA"
            : "E-FACT";
    }

    public static string Normalizar(string? producto)
        => string.Join(' ', (producto ?? string.Empty).Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)).ToUpperInvariant();
}

public sealed class AliadoAdminComisionRow
{
    public int IdComision { get; init; }
    public int IdVendedor { get; init; }
    public int? IdCliente { get; init; }
    public string Aliado { get; init; } = string.Empty;
    public int IdFactura { get; init; }
    public string NumeroFactura { get; init; } = string.Empty;
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

public class AliadoLiquidacionDto
{
    public int IdLiquidacion { get; init; }
    public string Aliado { get; init; } = string.Empty;
    public string Periodo { get; init; } = string.Empty;
    public decimal Total { get; init; }
    public DateTime Fecha { get; init; }
    public string Estado { get; init; } = string.Empty;
    public string? ReferenciaPago { get; init; }
    public string EstadoSri { get; init; } = "Pendiente";
    public string? ErrorSri { get; init; }
    public int? CodLiquidacionCompra { get; init; }
    public string? CodClave { get; init; }
    public DateTime? FechaPago { get; init; }
    public string? ObservacionPago { get; init; }
    public string? ComprobantePagoUrl { get; init; }
    public int? IdUsuarioPago { get; init; }
    public int CantidadComisiones { get; init; }
}

public sealed class AliadoAdminLiquidacionRow : AliadoLiquidacionDto
{
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

public sealed class AliadoClienteAsignacionRow
{
    public int IdCliente { get; init; }
    public string Nombre { get; init; } = string.Empty;
    public string? Identificacion { get; init; }
    public string? Correo { get; init; }
    public int? IdVendedorActual { get; init; }
    public string? AliadoActual { get; init; }
}

public sealed class AliadoClienteRegistroDto
{
    public string Nombres { get; set; } = string.Empty;
    public string Apellidos { get; set; } = string.Empty;
    public string RazonSocial { get; set; } = string.Empty;
    public string? AvatarUrl { get; set; }
    public string Email { get; set; } = string.Empty;
    public string Direccion { get; set; } = string.Empty;
    public string Celular { get; set; } = string.Empty;
    public string TipoDocumento { get; set; } = "CEDULA";
    public string Identificacion { get; set; } = string.Empty;
    public string Password { get; set; } = string.Empty;
    public string ConfirmarPassword { get; set; } = string.Empty;
    public int TipoCliente { get; set; } = 1;
    public int? IdVendedorDestino { get; set; }

    public void Normalizar()
    {
        Nombres = Nombres.Trim();
        Apellidos = Apellidos.Trim();
        RazonSocial = RazonSocial.Trim();
        Email = Email.Trim();
        Direccion = Direccion.Trim();
        Celular = Celular.Trim();
        TipoDocumento = TipoDocumento.Trim().ToUpperInvariant();
        Identificacion = PermiteAlfanumerico(TipoDocumento)
            ? new string(Identificacion.Trim().Where(char.IsLetterOrDigit).ToArray()).ToUpperInvariant()
            : new string(Identificacion.Trim().Where(char.IsDigit).ToArray());
    }

    public string? Validar()
    {
        if (TipoCliente is not 1 and not 2)
            return "Selecciona un tipo de cliente válido.";
        if (TipoCliente == 2 && !string.Equals(TipoDocumento, "RUC", StringComparison.Ordinal))
            return "Las empresas deben registrarse con RUC.";
        if (TipoCliente == 2)
        {
            if (RazonSocial.Length < 3 || RazonSocial.Length > 150)
                return "La razón social debe tener entre 3 y 150 caracteres.";
        }
        else
        {
            if (!EsNombreValido(Nombres))
                return "Ingresa nombres válidos.";
            if (!EsNombreValido(Apellidos))
                return "Ingresa apellidos válidos.";
        }
        if (string.IsNullOrWhiteSpace(Email) || Email.Length > 254 || !MailAddress.TryCreate(Email, out _))
            return "Ingresa un correo válido.";
        if (Direccion.Length is < 5 or > 100)
            return "La dirección debe tener entre 5 y 100 caracteres.";
        if (!string.IsNullOrWhiteSpace(Celular))
        {
            var digitos = Celular.Count(char.IsDigit);
            if (digitos is < 7 or > 15)
                return "El celular debe tener entre 7 y 15 dígitos.";
        }
        if (string.IsNullOrWhiteSpace(Identificacion) || !ValidarIdentificacion())
            return "La identificación no es válida.";
        if (!System.Text.RegularExpressions.Regex.IsMatch(Password, @"^(?=.*[a-z])(?=.*[A-Z])(?=.*\d)(?=.*[^\da-zA-Z]).{8,}$"))
            return "La clave debe tener mínimo 8 caracteres, mayúscula, minúscula, número y carácter especial.";
        if (!string.Equals(Password, ConfirmarPassword, StringComparison.Ordinal))
            return "La confirmación de la clave no coincide.";
        return null;
    }

    private bool ValidarIdentificacion()
    {
        if (PermiteAlfanumerico(TipoDocumento))
            return Identificacion.Length is >= 3 and <= 20 && Identificacion.All(char.IsLetterOrDigit);
        if (TipoDocumento == "RUC")
            return Identificacion.Length == 13 && Identificacion.EndsWith("001", StringComparison.Ordinal) && ValidarCedula(Identificacion[..10]);
        return Identificacion.Length == 10 && ValidarCedula(Identificacion);
    }

    private static bool ValidarCedula(string cedula)
    {
        if (cedula.Length != 10 || !cedula.All(char.IsDigit))
            return false;
        var provincia = int.Parse(cedula[..2]);
        var tercerDigito = int.Parse(cedula[2].ToString());
        if (provincia is < 1 or > 24 || tercerDigito > 5)
            return false;
        var coeficientes = new[] { 2, 1, 2, 1, 2, 1, 2, 1, 2 };
        var suma = 0;
        for (var i = 0; i < coeficientes.Length; i++)
        {
            var valor = int.Parse(cedula[i].ToString()) * coeficientes[i];
            suma += valor > 9 ? valor - 9 : valor;
        }
        return (10 - suma % 10) % 10 == int.Parse(cedula[9].ToString());
    }

    private static bool EsNombreValido(string valor) =>
        valor.Length >= 2 && System.Text.RegularExpressions.Regex.IsMatch(valor, @"^[a-zA-ZÀ-ÿ\s]{2,}$");

    private static bool PermiteAlfanumerico(string tipoDocumento) =>
        tipoDocumento is "PASAPORTE" or "EXTERIOR";
}

public sealed class AliadoUsuarioAsignacionRow
{
    public int IdUsuario { get; init; }
    public string Nombre { get; init; } = string.Empty;
    public string? Identificacion { get; init; }
    public string? Correo { get; init; }
    public int? IdVendedorActual { get; init; }
    public string? AliadoActual { get; init; }
}

public sealed class AliadoUsuarioAdminRow
{
    public int IdUsuario { get; init; }
    public string Nombres { get; init; } = string.Empty;
    public string Apellidos { get; init; } = string.Empty;
    public string Nombre { get; init; } = string.Empty;
    public string Email { get; init; } = string.Empty;
    public string? AvatarUrl { get; init; }
    public string Rol { get; init; } = string.Empty;
    public string? Aliado { get; init; }
    public string? CodigoReferencia { get; init; }
    public bool Activo { get; init; }
    public bool Bloqueado { get; init; }
    public bool ClaveTemporal { get; init; }
    public string? Identificacion { get; init; }
    public int TipoCliente { get; init; }
    public string TipoDocumento { get; init; } = "CEDULA";
    public string? RazonSocial { get; init; }
    public string? NombreComercial { get; init; }
    public string? Celular { get; init; }
    public string? Direccion { get; init; }
    public DateTime? FechaNacimiento { get; init; }
    public DateTime? FechaCreacion { get; init; }
    public decimal? PorcentajeComision { get; init; }
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
    public string Aliado { get; init; } = string.Empty;
    public decimal PorcentajeBase { get; init; }
    public string? IdentificacionCliente { get; init; }
    public string Cliente { get; init; } = string.Empty;
    public string Producto { get; init; } = string.Empty;
    public string Plan { get; init; } = string.Empty;
    public bool EsRenovacion { get; set; }
    public bool EsCompraRepetida { get; set; }
    public DateTime Fecha { get; init; }
    public DateTime? FechaVencimiento { get; init; }
    public decimal Total { get; init; }
    public decimal? Subtotal { get; init; }
    public decimal? Subtotal0 { get; init; }
    public decimal? Subtotal12 { get; init; }
    public decimal? Comision { get; init; }
    public bool Autorizado { get; init; }
    public string? EstadoPago { get; init; }
    public string Estado => !Autorizado
        ? "Pendiente"
        : FechaVencimiento?.Date < DateTime.Today
            ? "Vencido"
            : "Activo";
}

internal sealed class FacturaComisionRow
{
    public int IdFactura { get; init; }
    public int? IdCliente { get; init; }
    public string Producto { get; init; } = string.Empty;
    public bool EsRenovacion { get; set; }
    public DateTime? FechaVencimiento { get; init; }
    public decimal? Subtotal { get; init; }
    public decimal? Subtotal0 { get; init; }
    public decimal? Subtotal12 { get; init; }
    public DateTime Fecha { get; init; }
    public bool Autorizado { get; init; }
    public string? EstadoPago { get; init; }
}
