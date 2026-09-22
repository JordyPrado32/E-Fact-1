using Microsoft.EntityFrameworkCore;
using Simetric.Components.Helpers;
using Simetric.Data;
using Simetric.Models;
using System.Security.Claims;

namespace Simetric.Services;

public sealed class AliadoPortalService
{
    public const string RoleName = "Aliado Comercial";
    public const string AdminRoleName = "Administrador Portal de Aliados";
    public const string RootRoute = "/aliados";
    public const string AdminRoute = "/aliados/admin";

    private static readonly SemaphoreSlim SchemaLock = new(1, 1);
    private static bool _schemaEnsured;

    private readonly IDbContextFactory<AppDbContext> _dbFactory;
    private readonly VendedorBackOfficeService _vendedorService;

    public AliadoPortalService(
        IDbContextFactory<AppDbContext> dbFactory,
        VendedorBackOfficeService vendedorService)
    {
        _dbFactory = dbFactory;
        _vendedorService = vendedorService;
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
                x.Celular
            })
            .FirstOrDefaultAsync();

        if (usuario?.IdTipoUsuario is not > 0)
            return null;

        var tipo = await context.TipoUsuario
            .AsNoTracking()
            .Where(x => x.IdTipoUsuario == usuario.IdTipoUsuario && x.Estado == true)
            .Select(x => x.NombreTipo)
            .FirstOrDefaultAsync();

        var esAdministrador = string.Equals(tipo, AdminRoleName, StringComparison.OrdinalIgnoreCase) ||
                              usuario.IdTipoUsuario == BackOfficePermissionHelper.SuperAdministradorRoleId;
        if (!esAdministrador && !string.Equals(tipo, RoleName, StringComparison.OrdinalIgnoreCase))
            return null;

        var rolPortal = await context.AliadoPortalUsuariosRoles.AsNoTracking()
            .Where(x => x.IdUsuario == userId)
            .Join(context.AliadoPortalRoles.AsNoTracking(), x => x.IdRol, x => x.IdRol, (_, rol) => rol)
            .FirstOrDefaultAsync();
        if (rolPortal is null || !rolPortal.Activo)
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
        return (await ObtenerMenusAsync(contexto.IdRolPortal)).Any(x =>
            relative.Equals(x.Ruta, StringComparison.OrdinalIgnoreCase) ||
            relative.StartsWith($"{x.Ruta.TrimEnd('/')}/", StringComparison.OrdinalIgnoreCase));
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

    public async Task ActualizarPermisosAsync(int idRol, IReadOnlyCollection<int> menuIds)
    {
        await EnsureSchemaAsync();
        await using var db = await _dbFactory.CreateDbContextAsync();
        var existentes = await db.AliadoPortalRolesMenus.Where(x => x.IdRol == idRol).ToListAsync();
        db.AliadoPortalRolesMenus.RemoveRange(existentes);
        var permitidos = await db.AliadoPortalMenus.Where(x => x.Activo && menuIds.Contains(x.IdMenu)).Select(x => x.IdMenu).ToListAsync();
        db.AliadoPortalRolesMenus.AddRange(permitidos.Select(idMenu => new AliadoPortalRolMenu { IdRol = idRol, IdMenu = idMenu }));
        await db.SaveChangesAsync();
    }

    public async Task<AliadoDashboardDto?> ObtenerDashboardAsync(int userId)
    {
        var contexto = await ObtenerContextoAsync(userId);
        if (contexto is null)
            return null;

        var facturas = await ObtenerFacturasAsync(contexto.IdVendedor);
        var inicioMes = new DateTime(DateTime.Today.Year, DateTime.Today.Month, 1);
        var ventasPeriodo = facturas.Where(x => x.Fecha >= inicioMes).ToList();
        var renovaciones = facturas
            .Where(x => x.FechaVencimiento.HasValue && x.FechaVencimiento.Value.Date >= DateTime.Today && x.FechaVencimiento.Value.Date <= DateTime.Today.AddDays(30))
            .OrderBy(x => x.FechaVencimiento)
            .Take(5)
            .Select(x => ToRenovacion(x, null, contexto.PorcentajeBase))
            .ToList();
        var comisiones = facturas
            .Where(EsPagoConfirmado)
            .Select(x => CalcularComision(x, contexto.PorcentajeBase))
            .ToList();

        return new AliadoDashboardDto
        {
            Contexto = contexto,
            VentasPeriodo = ventasPeriodo.Sum(x => x.Total),
            VentasCantidad = ventasPeriodo.Count,
            ComisionesGeneradas = comisiones.Sum(x => x.ValorComision),
            ComisionesPagadas = 0m,
            RenovacionesProximas = renovaciones.Count,
            RenovacionesUrgentes = renovaciones.Count(x => x.DiasRestantes <= 7),
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
            .Where(x => x.Idvendedor == contexto.IdVendedor && x.Estado != false)
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
            .Where(x => x.Codcliente == idCliente && x.Idvendedor == contexto.IdVendedor)
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

        if (idFactura <= 0 || string.IsNullOrWhiteSpace(resultado))
            return (false, "Selecciona un resultado para registrar la gestión.");

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
            Resultado = resultado.Trim(),
            Observacion = string.IsNullOrWhiteSpace(observacion) ? null : observacion.Trim(),
            ProximoSeguimiento = proximoSeguimiento?.Date,
            IdUsuario = userId
        });
        await db.SaveChangesAsync();
        return (true, "Gestión de renovación registrada correctamente.");
    }

    public async Task<AliadoComisionesDto?> ObtenerComisionesAsync(int userId)
    {
        var contexto = await ObtenerContextoAsync(userId);
        if (contexto is null)
            return null;

        var movimientos = (await ObtenerFacturasAsync(contexto.IdVendedor))
            .Where(x => x.Total > 0 && EsPagoConfirmado(x))
            .OrderByDescending(x => x.Fecha)
            .Select(x => CalcularComision(x, contexto.PorcentajeBase))
            .ToList();

        return new AliadoComisionesDto
        {
            PorcentajeBase = contexto.PorcentajeBase,
            Movimientos = movimientos,
            Generadas = movimientos.Sum(x => x.ValorComision),
            Pagadas = 0m,
            Pendientes = movimientos.Count
        };
    }

    public async Task<IReadOnlyList<AliadoAdminRow>> ListarAliadosAsync()
    {
        await EnsureSchemaAsync();
        await using var db = await _dbFactory.CreateDbContextAsync();
        return await db.VendedoresBackOffice
            .AsNoTracking()
            .Where(x => !x.EsSistema)
            .OrderBy(x => x.Nombre)
            .Select(x => new AliadoAdminRow
            {
                IdVendedor = x.IdVendedor,
                Nombre = x.Nombre,
                CodigoReferencia = x.CodigoReferencia,
                Activo = x.Activo,
                PorcentajeBase = x.PorcentajeBase,
                Usuario = db.Usuarios.Where(u => u.IdVendedor == x.IdVendedor).Select(u => u.Email).FirstOrDefault()
            })
            .ToListAsync();
    }

    public async Task<(bool Success, string Message)> CrearCuentaAliadoAsync(int actorId, string nombre, string email, string password, decimal porcentajeBase = 30m, bool esAdministradorPortal = false)
    {
        await EnsureSchemaAsync();
        nombre = nombre.Trim();
        email = email.Trim();
        password = password.Trim();
        if (string.IsNullOrWhiteSpace(nombre) || string.IsNullOrWhiteSpace(email) || password.Length < 8)
            return (false, "Nombre, correo y una clave de al menos 8 caracteres son obligatorios.");
        if (porcentajeBase is < 0 or > 100)
            return (false, "El porcentaje debe estar entre 0 y 100.");

        await using var db = await _dbFactory.CreateDbContextAsync();
        var actor = await db.Usuarios
            .AsNoTracking()
            .Where(x => x.IdUsuario == actorId && x.Estado == true)
            .Select(x => new { x.IdTipoUsuario, Tipo = x.IdTipoUsuarioNavigation!.NombreTipo })
            .FirstOrDefaultAsync();
        var actorEsSuperAdministrador = actor?.IdTipoUsuario == BackOfficePermissionHelper.SuperAdministradorRoleId;
        var actorEsBackOffice = actor?.IdTipoUsuario == BackOfficePermissionHelper.BackOfficeRoleId;
        var actorEsAdministradorPortal = string.Equals(actor?.Tipo, AdminRoleName, StringComparison.OrdinalIgnoreCase);
        if ((!actorEsSuperAdministrador && !actorEsBackOffice && !actorEsAdministradorPortal) ||
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
        ComisionPotencial = decimal.Round((factura.Subtotal ?? factura.Total) * porcentajeBase / 100m, 2, MidpointRounding.AwayFromZero),
        EstadoGestion = gestion?.Resultado ?? "Pendiente",
        UltimaGestion = gestion?.FechaGestion,
        Observacion = gestion?.Observacion
    };

    private static AliadoComisionMovimientoDto CalcularComision(FacturaPortalRow factura, decimal porcentajeBase)
    {
        var baseComisionable = factura.Subtotal ?? factura.Total;
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

    private static bool EsPagoConfirmado(FacturaPortalRow factura)
    {
        var estado = factura.EstadoPago?.Trim().ToUpperInvariant();
        return estado is "PAGADA" or "PAGADO" or "CANCELADA" or "CANCELADO" or "COBRADA" or "COBRADO";
    }

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
MERGE dbo.ALIADO_PORTAL_MENU AS t USING (VALUES (N'Inicio',N'{RootRoute}',N'ri-dashboard-3-line',1),(N'Mis clientes',N'{RootRoute}/clientes',N'ri-user-3-line',2),(N'Renovaciones',N'{RootRoute}/renovaciones',N'ri-refresh-line',3),(N'Comisiones',N'{RootRoute}/comisiones',N'ri-hand-coin-line',4),(N'Liquidaciones',N'{RootRoute}/liquidaciones',N'ri-bank-card-line',5),(N'Mi perfil',N'{RootRoute}/perfil',N'ri-user-settings-line',6),(N'Administración de aliados',N'{AdminRoute}',N'ri-admin-line',10)) s(Nombre,Ruta,Icono,Orden) ON t.Ruta=s.Ruta WHEN NOT MATCHED THEN INSERT(Nombre,Ruta,Icono,Orden,Activo) VALUES(s.Nombre,s.Ruta,s.Icono,s.Orden,1);
INSERT dbo.ALIADO_PORTAL_ROL_MENU(IdRol,IdMenu) SELECT r.IdRol,m.IdMenu FROM dbo.ALIADO_PORTAL_ROL r CROSS JOIN dbo.ALIADO_PORTAL_MENU m WHERE ((r.Nombre=N'{RoleName}' AND m.Ruta<>N'{AdminRoute}') OR (r.Nombre=N'{AdminRoleName}' AND m.Ruta=N'{AdminRoute}')) AND NOT EXISTS(SELECT 1 FROM dbo.ALIADO_PORTAL_ROL_MENU x WHERE x.IdRol=r.IdRol AND x.IdMenu=m.IdMenu);
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
        Observacion NVARCHAR(500) NULL,
        ProximoSeguimiento DATE NULL,
        IdUsuario INT NOT NULL
    );
END
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
    public decimal Pagadas { get; init; }
    public int Pendientes { get; init; }
    public IReadOnlyList<AliadoComisionMovimientoDto> Movimientos { get; init; } = Array.Empty<AliadoComisionMovimientoDto>();
}

public sealed class AliadoComisionMovimientoDto
{
    public int IdFactura { get; init; }
    public string Cliente { get; init; } = string.Empty;
    public string Producto { get; init; } = string.Empty;
    public string Tipo { get; init; } = string.Empty;
    public decimal BaseComisionable { get; init; }
    public decimal Porcentaje { get; init; }
    public decimal ValorComision { get; init; }
    public DateTime FechaGeneracion { get; init; }
    public string Estado { get; init; } = string.Empty;
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

public sealed class AliadoAdminRow
{
    public int IdVendedor { get; init; }
    public string Nombre { get; init; } = string.Empty;
    public string CodigoReferencia { get; init; } = string.Empty;
    public bool Activo { get; init; }
    public decimal PorcentajeBase { get; init; }
    public string? Usuario { get; init; }
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
    public decimal? Comision { get; init; }
    public bool Autorizado { get; init; }
    public string? EstadoPago { get; init; }
    public string Estado => Autorizado ? "Activo" : "Pendiente";
}
