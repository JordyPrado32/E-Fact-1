using System.Globalization;
using Microsoft.EntityFrameworkCore;
using Simetric.Data;
using Simetric.DTOs;
using Simetric.DTOs.EContax;
using Simetric.Models;
using Simetric.Models.EContax;

namespace Simetric.Services.EContax;

public sealed class EContaxOrdenCompraService
{
    private sealed record ProveedorCompraInfo(string Nombre, string Identificacion);

    private const string Borrador = "BORRADOR";
    private const string Aprobada = "APROBADA";
    private const string Enviada = "ENVIADA";
    private const string Cerrada = "CERRADA";
    private const string Cancelada = "CANCELADA";

    private readonly IDbContextFactory<AppDbContext> _dbFactory;
    private readonly EContaxTenantService _tenantService;
    private readonly EContaxSeguridadService _seguridad;
    private readonly EContaxCatalogService _catalogService;

    public EContaxOrdenCompraService(
        IDbContextFactory<AppDbContext> dbFactory,
        EContaxTenantService tenantService,
        EContaxSeguridadService seguridad,
        EContaxCatalogService catalogService)
    {
        _dbFactory = dbFactory;
        _tenantService = tenantService;
        _seguridad = seguridad;
        _catalogService = catalogService;
    }

    public async Task<List<EContaxOrdenCompraListaDto>> ListarAsync(
        int userId,
        string? search = null,
        string? estado = null,
        DateTime? fechaDesde = null,
        DateTime? fechaHasta = null)
    {
        await _seguridad.ExigirAccionAsync(userId, EContaxRoutes.ComprasOrdenes, EContaxAccion.Ver);
        await using var context = await _dbFactory.CreateDbContextAsync();
        var actor = await _tenantService.GetContextAsync(context, userId);
        var query = context.EContaxOrdenesCompra.AsNoTracking()
            .Where(x => x.IdEmpresa == actor.IdEmpresa);

        if (!actor.EsJefeEmpresa && actor.IdSucursal is > 0)
            query = query.Where(x => x.IdSucursal == actor.IdSucursal.Value);

        if (!string.IsNullOrWhiteSpace(search))
        {
            var filtro = search.Trim();
            query = query.Where(x => x.NumeroOrden.Contains(filtro) ||
                x.NombreProveedor.Contains(filtro) ||
                x.RucProveedor.Contains(filtro));
        }

        if (!string.IsNullOrWhiteSpace(estado))
            query = query.Where(x => x.Estado == estado.Trim().ToUpperInvariant());

        if (fechaDesde.HasValue)
            query = query.Where(x => x.FechaEmision >= fechaDesde.Value.Date);
        if (fechaHasta.HasValue)
            query = query.Where(x => x.FechaEmision < fechaHasta.Value.Date.AddDays(1));

        return await query
            .OrderByDescending(x => x.IdOrdenCompra)
            .Select(x => new EContaxOrdenCompraListaDto
            {
                IdOrdenCompra = x.IdOrdenCompra,
                NumeroOrden = x.NumeroOrden,
                IdSucursal = x.IdSucursal,
                IdProveedor = x.IdProveedorCliente,
                NombreProveedor = x.NombreProveedor,
                RucProveedor = x.RucProveedor,
                FechaEmision = x.FechaEmision,
                FechaEntregaEstimada = x.FechaEntregaEstimada,
                Estado = x.Estado,
                Total = x.Total,
                Lineas = x.Detalles.Count
            })
            .ToListAsync();
    }

    public async Task<EContaxOrdenCompraDto?> ObtenerAsync(int userId, int id)
    {
        await _seguridad.ExigirAccionAsync(userId, EContaxRoutes.ComprasOrdenes, EContaxAccion.Ver);
        await using var context = await _dbFactory.CreateDbContextAsync();
        var actor = await _tenantService.GetContextAsync(context, userId);
        var query = context.EContaxOrdenesCompra.AsNoTracking()
            .Include(x => x.Detalles)
            .Where(x => x.IdOrdenCompra == id && x.IdEmpresa == actor.IdEmpresa);

        if (!actor.EsJefeEmpresa && actor.IdSucursal is > 0)
            query = query.Where(x => x.IdSucursal == actor.IdSucursal.Value);

        var entity = await query.FirstOrDefaultAsync();
        return entity is null ? null : Map(entity);
    }

    public async Task<List<EContaxProveedorLookupDto>> ListarProveedoresAsync(int userId)
    {
        await _seguridad.ExigirAccionAsync(userId, EContaxRoutes.ComprasOrdenes, EContaxAccion.Ver);
        await using var context = await _dbFactory.CreateDbContextAsync();
        var actor = await _tenantService.GetContextAsync(context, userId);
        var proveedores = await context.Clientes
            .AsNoTracking()
            .Where(c => c.Idempresa == actor.IdEmpresa && c.Estado == true &&
                context.Proveedores.Any(p => p.ruc == c.Numeroidentificacion && p.estado != false))
            .Select(c => new
            {
                c.Codcliente,
                c.Nombrerazonsocial,
                c.Nombrecomercial,
                c.Nombres,
                c.Apellidos,
                c.Numeroidentificacion
            })
            .OrderBy(x => x.Nombrerazonsocial ?? x.Nombrecomercial ?? x.Apellidos ?? x.Nombres)
            .ToListAsync();

        return proveedores.Select(x => new EContaxProveedorLookupDto
        {
            IdProveedor = x.Codcliente,
            Nombre = ObtenerNombre(x.Nombrerazonsocial, x.Nombrecomercial, x.Nombres, x.Apellidos),
            Identificacion = x.Numeroidentificacion ?? string.Empty
        }).ToList();
    }

    public Task<List<ProductoLookupDetalleDto>> BuscarProductosAsync(int userId, string? filtro, int? idProveedor) =>
        _catalogService.BuscarProductosFiltroAsync(userId, filtro ?? string.Empty, idProveedor, soloProductos: true);

    public async Task<EContaxOrdenCompraDto> CrearAsync(int userId, EContaxOrdenCompraUpsertDto dto)
    {
        await _seguridad.ExigirAccionAsync(userId, EContaxRoutes.ComprasOrdenes, EContaxAccion.Crear);
        await using var context = await _dbFactory.CreateDbContextAsync();
        await using var transaction = await context.Database.BeginTransactionAsync();
        var actor = await _tenantService.GetContextAsync(context, userId);
        var sucursal = await _tenantService.ResolveTargetSucursalAsync(actor, dto.IdSucursal);
        var proveedor = await ObtenerProveedorAsync(context, actor, dto.IdProveedor);
        await ValidarDocumentoProveedorAsync(context, actor.IdEmpresa, dto.NumeroFacturaProveedor, dto.ClaveAccesoProveedor, null);
        var detalles = await PrepararDetallesAsync(context, actor, sucursal, dto.Detalles);

        var ahora = DateTime.Now;
        var entity = new EContaxOrdenCompra
        {
            IdEmpresa = actor.IdEmpresa,
            IdSucursal = sucursal,
            IdProveedorCliente = dto.IdProveedor,
            RucProveedor = proveedor.Identificacion,
            NombreProveedor = proveedor.Nombre,
            NumeroOrden = string.Empty,
            FechaEmision = ahora,
            FechaEntregaEstimada = dto.FechaEntregaEstimada,
            FormaPago = Normalizar(dto.FormaPago),
            PlazoPago = dto.PlazoPago,
            Observacion = Normalizar(dto.Observacion),
            NumeroFacturaProveedor = Normalizar(dto.NumeroFacturaProveedor),
            ClaveAccesoProveedor = Normalizar(dto.ClaveAccesoProveedor),
            Estado = Borrador,
            UsuarioCreacion = userId,
            FechaCreacion = ahora
        };
        AplicarTotales(entity, detalles);
        entity.Detalles = detalles;
        await SincronizarRelacionesProveedorProductoAsync(context, actor.IdEmpresa, dto.IdProveedor, detalles);
        context.EContaxOrdenesCompra.Add(entity);
        await context.SaveChangesAsync();

        entity.NumeroOrden = $"OC-{entity.IdOrdenCompra:D6}";
        context.EContaxOrdenesCompraHistorial.Add(new EContaxOrdenCompraEstadoHistorial
        {
            IdOrdenCompra = entity.IdOrdenCompra,
            EstadoNuevo = Borrador,
            Usuario = userId,
            Fecha = ahora
        });
        await context.SaveChangesAsync();
        await transaction.CommitAsync();
        return Map(entity);
    }

    public async Task<EContaxOrdenCompraDto> ActualizarAsync(int userId, int id, EContaxOrdenCompraUpsertDto dto)
    {
        await _seguridad.ExigirAccionAsync(userId, EContaxRoutes.ComprasOrdenes, EContaxAccion.Editar);
        await using var context = await _dbFactory.CreateDbContextAsync();
        await using var transaction = await context.Database.BeginTransactionAsync();
        var actor = await _tenantService.GetContextAsync(context, userId);
        var entity = await context.EContaxOrdenesCompra
            .Include(x => x.Detalles)
            .FirstOrDefaultAsync(x => x.IdOrdenCompra == id && x.IdEmpresa == actor.IdEmpresa &&
                (actor.EsJefeEmpresa || x.IdSucursal == actor.IdSucursal));

        if (entity is null)
            throw new InvalidOperationException("La orden de compra no existe o no pertenece a tu contexto.");
        if (entity.Estado != Borrador)
            throw new InvalidOperationException("Solo se pueden editar órdenes en estado borrador.");

        var sucursal = await _tenantService.ResolveTargetSucursalAsync(actor, dto.IdSucursal ?? entity.IdSucursal);
        var proveedor = await ObtenerProveedorAsync(context, actor, dto.IdProveedor);
        await ValidarDocumentoProveedorAsync(context, actor.IdEmpresa, dto.NumeroFacturaProveedor, dto.ClaveAccesoProveedor, id);
        var detalles = await PrepararDetallesAsync(context, actor, sucursal, dto.Detalles);

        entity.IdSucursal = sucursal;
        entity.IdProveedorCliente = dto.IdProveedor;
        entity.RucProveedor = proveedor.Identificacion;
        entity.NombreProveedor = proveedor.Nombre;
        entity.FechaEntregaEstimada = dto.FechaEntregaEstimada;
        entity.FormaPago = Normalizar(dto.FormaPago);
        entity.PlazoPago = dto.PlazoPago;
        entity.Observacion = Normalizar(dto.Observacion);
        entity.NumeroFacturaProveedor = Normalizar(dto.NumeroFacturaProveedor);
        entity.ClaveAccesoProveedor = Normalizar(dto.ClaveAccesoProveedor);
        entity.UsuarioActualizacion = userId;
        entity.FechaActualizacion = DateTime.Now;
        context.EContaxOrdenesCompraDetalles.RemoveRange(entity.Detalles);
        entity.Detalles = detalles;
        AplicarTotales(entity, detalles);
        await SincronizarRelacionesProveedorProductoAsync(context, actor.IdEmpresa, dto.IdProveedor, detalles);
        await context.SaveChangesAsync();
        await transaction.CommitAsync();
        return Map(entity);
    }

    public async Task<EContaxOrdenCompraDto> CambiarEstadoAsync(int userId, int id, string estado, string? observacion)
    {
        var nuevoEstado = estado.Trim().ToUpperInvariant();
        var accion = nuevoEstado == Cancelada ? EContaxAccion.Eliminar : EContaxAccion.Editar;
        await _seguridad.ExigirAccionAsync(userId, EContaxRoutes.ComprasOrdenes, accion);
        if (nuevoEstado is not (Aprobada or Enviada or Cerrada or Cancelada))
            throw new InvalidOperationException("Estado de orden de compra no válido.");

        await using var context = await _dbFactory.CreateDbContextAsync();
        var actor = await _tenantService.GetContextAsync(context, userId);
        var entity = await context.EContaxOrdenesCompra
            .Include(x => x.Detalles)
            .FirstOrDefaultAsync(x => x.IdOrdenCompra == id && x.IdEmpresa == actor.IdEmpresa &&
                (actor.EsJefeEmpresa || x.IdSucursal == actor.IdSucursal));

        if (entity is null)
            throw new InvalidOperationException("La orden de compra no existe o no pertenece a tu contexto.");
        if (!EsTransicionValida(entity.Estado, nuevoEstado))
            throw new InvalidOperationException($"No se puede pasar de {entity.Estado} a {nuevoEstado}.");

        var anterior = entity.Estado;
        entity.Estado = nuevoEstado;
        entity.UsuarioActualizacion = userId;
        entity.FechaActualizacion = DateTime.Now;
        context.EContaxOrdenesCompraHistorial.Add(new EContaxOrdenCompraEstadoHistorial
        {
            IdOrdenCompra = entity.IdOrdenCompra,
            EstadoAnterior = anterior,
            EstadoNuevo = nuevoEstado,
            Observacion = Normalizar(observacion),
            Usuario = userId,
            Fecha = DateTime.Now
        });
        await context.SaveChangesAsync();
        return Map(entity);
    }

    private async Task<ProveedorCompraInfo> ObtenerProveedorAsync(AppDbContext context, EContaxUserContext actor, int idProveedor)
    {
        var proveedor = await context.Clientes
            .AsNoTracking()
            .Where(c => c.Codcliente == idProveedor && c.Idempresa == actor.IdEmpresa && c.Estado == true &&
                context.Proveedores.Any(p => p.ruc == c.Numeroidentificacion && p.estado != false))
            .Select(c => new
            {
                c.Nombrerazonsocial,
                c.Nombrecomercial,
                c.Nombres,
                c.Apellidos,
                c.Numeroidentificacion
            })
            .FirstOrDefaultAsync();

        if (proveedor is null)
            throw new InvalidOperationException("El proveedor no existe, está inactivo o no pertenece a la empresa.");

        return new ProveedorCompraInfo(
            ObtenerNombre(proveedor.Nombrerazonsocial, proveedor.Nombrecomercial, proveedor.Nombres, proveedor.Apellidos),
            proveedor.Numeroidentificacion ?? string.Empty);
    }

    private static async Task ValidarDocumentoProveedorAsync(AppDbContext context, int empresa, string? numeroFactura, string? claveAcceso, int? excluirId)
    {
        var numero = Normalizar(numeroFactura);
        var clave = Normalizar(claveAcceso);
        if (numero is null && clave is null) return;
        var duplicado = await context.EContaxOrdenesCompra.AsNoTracking().AnyAsync(x => x.IdEmpresa == empresa && x.Estado != Cancelada &&
            (!excluirId.HasValue || x.IdOrdenCompra != excluirId.Value) &&
            ((numero != null && x.NumeroFacturaProveedor == numero) || (clave != null && x.ClaveAccesoProveedor == clave)));
        if (duplicado)
            throw new InvalidOperationException("La factura o clave de acceso del proveedor ya está registrada en otra compra.");
    }

    private static async Task SincronizarRelacionesProveedorProductoAsync(
        AppDbContext context,
        int idEmpresa,
        int idProveedor,
        IEnumerable<EContaxOrdenCompraDetalle> detalles)
    {
        foreach (var detalle in detalles)
        {
            var relacion = await context.EContaxProductosProveedores
                .FirstOrDefaultAsync(x => x.IdEmpresa == idEmpresa && x.CodigoProducto == detalle.CodigoProducto &&
                    x.IdProveedorCliente == idProveedor);

            if (relacion is null)
            {
                var tienePrincipal = await context.EContaxProductosProveedores
                    .AnyAsync(x => x.IdEmpresa == idEmpresa && x.CodigoProducto == detalle.CodigoProducto && x.Activo && x.EsPrincipal);
                relacion = new EContaxProductoProveedor
                {
                    IdEmpresa = idEmpresa,
                    CodigoProducto = detalle.CodigoProducto,
                    IdProveedorCliente = idProveedor,
                    PrecioCompra = detalle.PrecioUnitario,
                    EsPrincipal = !tienePrincipal,
                    Activo = true,
                    FechaActualizacion = DateTime.Now
                };
                context.EContaxProductosProveedores.Add(relacion);
            }
            else
            {
                relacion.PrecioCompra = detalle.PrecioUnitario;
                relacion.Activo = true;
                relacion.FechaActualizacion = DateTime.Now;
            }
        }
    }

    private async Task<List<EContaxOrdenCompraDetalle>> PrepararDetallesAsync(
        AppDbContext context,
        EContaxUserContext actor,
        int sucursal,
        IEnumerable<EContaxOrdenCompraDetalleUpsertDto>? modelos)
    {
        var items = (modelos ?? Enumerable.Empty<EContaxOrdenCompraDetalleUpsertDto>()).ToList();
        if (items.Count == 0)
            throw new InvalidOperationException("La orden debe contener al menos un producto.");

        if (items.GroupBy(x => x.CodigoProducto).Any(x => x.Key <= 0 || x.Count() > 1))
            throw new InvalidOperationException("No se pueden repetir productos en la orden.");

        var ids = items.Select(x => x.CodigoProducto).ToList();
        var productos = await context.Productos
            .AsNoTracking()
            .Where(p => p.Idempresa == actor.IdEmpresa && p.Estado == true && ids.Contains(p.Codigo) &&
                p.Tipocompravena != null && p.Tipocompravena.ToUpper() == "PRODUCTO" &&
                (p.Idsucursal == sucursal || context.EContaxProductosSucursales.Any(a =>
                    a.CodigoProducto == p.Codigo && a.IdEmpresa == actor.IdEmpresa && a.IdSucursal == sucursal)))
            .ToListAsync();

        if (productos.Count != ids.Count)
            throw new InvalidOperationException("Uno o más registros no están catalogados como productos activos o no pertenecen a la sucursal seleccionada.");

        var resultado = new List<EContaxOrdenCompraDetalle>(items.Count);
        foreach (var item in items)
        {
            if (item.Cantidad <= 0)
                throw new InvalidOperationException("La cantidad debe ser mayor que cero.");
            if (item.PrecioUnitario < 0 || item.Descuento < 0)
                throw new InvalidOperationException("El precio y el descuento no pueden ser negativos.");

            var producto = productos.First(x => x.Codigo == item.CodigoProducto);
            var bruto = decimal.Round(item.Cantidad * item.PrecioUnitario, 2, MidpointRounding.AwayFromZero);
            if (item.Descuento > bruto)
                throw new InvalidOperationException($"El descuento de {producto.Nombre} no puede superar el subtotal.");

            var subtotal = decimal.Round(bruto - item.Descuento, 2, MidpointRounding.AwayFromZero);
            var tarifaIva = ObtenerTarifaIva(producto.Porcentajeimpuesto);
            var iva = decimal.Round(subtotal * tarifaIva / 100m, 2, MidpointRounding.AwayFromZero);
            resultado.Add(new EContaxOrdenCompraDetalle
            {
                CodigoProducto = producto.Codigo,
                CodigoPrincipal = producto.CodigoPrincipal,
                Descripcion = producto.Nombre?.Trim() ?? string.Empty,
                Cantidad = item.Cantidad,
                PrecioUnitario = item.PrecioUnitario,
                Descuento = item.Descuento,
                TarifaIva = tarifaIva,
                Subtotal = subtotal,
                Iva = iva,
                Total = subtotal + iva
            });
        }

        return resultado;
    }

    private static void AplicarTotales(EContaxOrdenCompra entity, IEnumerable<EContaxOrdenCompraDetalle> detalles)
    {
        entity.Subtotal = decimal.Round(detalles.Sum(x => x.Subtotal), 2, MidpointRounding.AwayFromZero);
        entity.Iva = decimal.Round(detalles.Sum(x => x.Iva), 2, MidpointRounding.AwayFromZero);
        entity.Total = entity.Subtotal + entity.Iva;
    }

    private static EContaxOrdenCompraDto Map(EContaxOrdenCompra entity) => new()
    {
        IdOrdenCompra = entity.IdOrdenCompra,
        NumeroOrden = entity.NumeroOrden,
        IdSucursal = entity.IdSucursal,
        IdProveedor = entity.IdProveedorCliente,
        NombreProveedor = entity.NombreProveedor,
        RucProveedor = entity.RucProveedor,
        FechaEmision = entity.FechaEmision,
        FechaEntregaEstimada = entity.FechaEntregaEstimada,
        Estado = entity.Estado,
        Total = entity.Total,
        Lineas = entity.Detalles.Count,
        FormaPago = entity.FormaPago,
        PlazoPago = entity.PlazoPago,
        Observacion = entity.Observacion,
        NumeroFacturaProveedor = entity.NumeroFacturaProveedor,
        ClaveAccesoProveedor = entity.ClaveAccesoProveedor,
        Subtotal = entity.Subtotal,
        Iva = entity.Iva,
        Detalles = entity.Detalles.Select(x => new EContaxOrdenCompraDetalleDto
        {
            IdDetalle = x.IdDetalle,
            CodigoProducto = x.CodigoProducto,
            CodigoPrincipal = x.CodigoPrincipal,
            Descripcion = x.Descripcion,
            Cantidad = x.Cantidad,
            PrecioUnitario = x.PrecioUnitario,
            Descuento = x.Descuento,
            TarifaIva = x.TarifaIva,
            Subtotal = x.Subtotal,
            Iva = x.Iva,
            Total = x.Total
        }).ToList()
    };

    private static bool EsTransicionValida(string actual, string nuevo) =>
        (actual, nuevo) switch
        {
            (Borrador, Aprobada) or (Aprobada, Enviada) => true,
            (Aprobada, Cerrada) or (Enviada, Cerrada) => true,
            (Borrador, Cancelada) or (Aprobada, Cancelada) or (Enviada, Cancelada) => true,
            _ => false
        };

    private static string ObtenerNombre(string? razonSocial, string? comercial, string? nombres, string? apellidos) =>
        !string.IsNullOrWhiteSpace(razonSocial) ? razonSocial.Trim() :
        !string.IsNullOrWhiteSpace(comercial) ? comercial.Trim() :
        string.Join(" ", new[] { nombres, apellidos }.Where(x => !string.IsNullOrWhiteSpace(x)).Select(x => x!.Trim()));

    private static int ObtenerTarifaIva(string? valor)
    {
        if (decimal.TryParse(valor?.Trim(), NumberStyles.Any, CultureInfo.InvariantCulture, out var tarifa))
            return (int)Math.Round(tarifa, MidpointRounding.AwayFromZero);
        return int.TryParse(valor, out var entero) ? entero : 0;
    }

    private static string? Normalizar(string? valor) =>
        string.IsNullOrWhiteSpace(valor) ? null : valor.Trim();
}
