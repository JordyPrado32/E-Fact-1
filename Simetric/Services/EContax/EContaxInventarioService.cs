using System.Data;
using Microsoft.EntityFrameworkCore;
using System.Text.Json;
using Simetric.Data;
using Simetric.DTOs.EContax;
using Simetric.Models;
using Simetric.Models.EContax;

namespace Simetric.Services.EContax;

public sealed class EContaxInventarioService
{
    private static readonly string[] Metodos = ["FIFO", "LIFO", "PROMEDIO"];
    private sealed record LoteSalida(long IdLote, decimal Cantidad, decimal CostoUnitario, string? NumeroLote, DateTime? FechaVencimiento);
    private readonly IDbContextFactory<AppDbContext> _dbFactory;
    private readonly EContaxTenantService _tenant;
    private readonly EContaxSeguridadService _seguridad;

    public EContaxInventarioService(
        IDbContextFactory<AppDbContext> dbFactory,
        EContaxTenantService tenant,
        EContaxSeguridadService seguridad)
    {
        _dbFactory = dbFactory;
        _tenant = tenant;
        _seguridad = seguridad;
    }

    public static IReadOnlyList<string> MetodosValoracion => Metodos;

    public async Task<EContaxInventarioDashboardDto> GetDashboardAsync(int userId, int? sucursalId, string? filtro)
    {
        await _seguridad.ExigirAccionAsync(userId, EContaxRoutes.Inventarios, EContaxAccion.Ver);
        await using var context = await _dbFactory.CreateDbContextAsync();
        var userContext = await _tenant.GetContextAsync(context, userId);
        var sucursales = await _tenant.GetSucursalesEmpresaAsync(context, userContext.IdEmpresa);
        var sucursalesConsulta = ResolverSucursalesConsulta(userContext, sucursales, sucursalId);
        var idsSucursal = sucursalesConsulta.Select(x => x.IdSucursal).ToList();
        var productosQuery = context.Productos
            .AsNoTracking()
            .Where(p => p.Idempresa == userContext.IdEmpresa && p.Estado == true &&
                        (p.Tipocompravena == null || p.Tipocompravena.ToUpper() != "SERVICIO") &&
                        (p.Idsucursal.HasValue && idsSucursal.Contains(p.Idsucursal.Value) ||
                         context.EContaxProductosSucursales.Any(a => a.CodigoProducto == p.Codigo &&
                             a.IdEmpresa == userContext.IdEmpresa && idsSucursal.Contains(a.IdSucursal))));

        if (!string.IsNullOrWhiteSpace(filtro))
        {
            var texto = filtro.Trim().ToLowerInvariant();
            productosQuery = productosQuery.Where(p =>
                (p.Nombre ?? string.Empty).ToLower().Contains(texto) ||
                (p.CodigoPrincipal ?? string.Empty).ToLower().Contains(texto) ||
                p.Codigo.ToString().Contains(texto));
        }

        var productos = await productosQuery
            .OrderBy(p => p.Nombre)
            .Select(p => new { p.Codigo, p.CodigoPrincipal, p.Nombre, p.Idsucursal })
            .ToListAsync();
        var codigos = productos.Select(x => x.Codigo).ToList();
        var asignaciones = await context.EContaxProductosSucursales.AsNoTracking()
            .Where(x => x.IdEmpresa == userContext.IdEmpresa && idsSucursal.Contains(x.IdSucursal) && codigos.Contains(x.CodigoProducto))
            .Select(x => new { x.CodigoProducto, x.IdSucursal })
            .ToListAsync();
        var existencias = await context.EContaxInventarioExistencias
            .AsNoTracking()
            .Where(x => x.IdEmpresa == userContext.IdEmpresa && idsSucursal.Contains(x.IdSucursal) && codigos.Contains(x.CodigoProducto))
            .ToListAsync();
        var nombresSucursal = sucursales.ToDictionary(x => x.IdSucursal, x => x.Nombre);
        var fechaHoy = DateTime.UtcNow.Date;
        var fechaLimite7Dias = fechaHoy.AddDays(7);
        var fechaLimiteVencimiento = fechaHoy.AddDays(30);
        var lotesReporte = await context.EContaxInventarioLotes.AsNoTracking()
            .Where(x => x.IdEmpresa == userContext.IdEmpresa && idsSucursal.Contains(x.IdSucursal) && codigos.Contains(x.CodigoProducto) && x.CantidadDisponible > 0)
            .Select(x => new { x.CodigoProducto, x.IdSucursal, x.CantidadDisponible, x.CostoUnitario, x.FechaVencimiento })
            .ToListAsync();
        var items = productos
            .SelectMany(producto => sucursalesConsulta
                .Where(sucursal => producto.Idsucursal == sucursal.IdSucursal ||
                    asignaciones.Any(x => x.CodigoProducto == producto.Codigo && x.IdSucursal == sucursal.IdSucursal))
                .Select(sucursal =>
                {
                    var existencia = existencias.FirstOrDefault(x =>
                        x.CodigoProducto == producto.Codigo && x.IdSucursal == sucursal.IdSucursal);
                    var stock = existencia?.StockActual ?? 0;
                    var minimo = existencia?.StockMinimo ?? 0;
                    var maximo = existencia?.StockMaximo ?? 0;
                    var lotesProducto = lotesReporte.Where(x => x.CodigoProducto == producto.Codigo && x.IdSucursal == sucursal.IdSucursal).ToList();
                    var stockLotesVigentes = lotesProducto.Count == 0
                        ? stock
                        : lotesProducto.Where(x => !x.FechaVencimiento.HasValue || x.FechaVencimiento.Value.Date > fechaHoy).Sum(x => x.CantidadDisponible);
                    var stockDisponibleVigente = Math.Max(0, Math.Min(stock, stockLotesVigentes) - (existencia?.StockReservado ?? 0));
                    var loteVencido = lotesReporte
                        .Where(x => x.CodigoProducto == producto.Codigo && x.IdSucursal == sucursal.IdSucursal &&
                            x.FechaVencimiento.HasValue && x.FechaVencimiento.Value.Date <= fechaHoy)
                        .OrderBy(x => x.FechaVencimiento)
                        .FirstOrDefault();
                    var lotePorVencer = lotesReporte
                        .Where(x => x.CodigoProducto == producto.Codigo && x.IdSucursal == sucursal.IdSucursal &&
                            x.FechaVencimiento.HasValue && x.FechaVencimiento.Value.Date > fechaHoy && x.FechaVencimiento.Value.Date <= fechaLimiteVencimiento)
                        .OrderBy(x => x.FechaVencimiento)
                        .FirstOrDefault();
                    var alertaVencimiento = loteVencido is not null
                        ? "VENCIDO"
                        : lotePorVencer?.FechaVencimiento is { } fechaVencimiento && fechaVencimiento.Date <= fechaLimite7Dias
                            ? "VENCE_7"
                            : lotePorVencer is not null ? "VENCE_30" : null;
                    return new EContaxInventarioItemDto
                    {
                        CodigoProducto = producto.Codigo,
                        CodigoPrincipal = producto.CodigoPrincipal,
                        NombreProducto = producto.Nombre,
                        IdSucursal = sucursal.IdSucursal,
                        NombreSucursal = nombresSucursal.GetValueOrDefault(sucursal.IdSucursal),
                        StockActual = stock,
                         StockReservado = existencia?.StockReservado ?? 0,
                        StockDisponibleVigente = stockDisponibleVigente,
                        StockMinimo = minimo,
                        StockMaximo = maximo,
                        CostoPromedio = existencia?.CostoPromedio ?? 0,
                        Alerta = ObtenerAlerta(stock, minimo, maximo),
                        AlertaVencimiento = alertaVencimiento,
                        TieneLotePorVencer = alertaVencimiento is "VENCE_7" or "VENCE_30",
                        ProximoVencimiento = loteVencido?.FechaVencimiento ?? lotePorVencer?.FechaVencimiento
                    };
                }))
            .ToList();

        return new EContaxInventarioDashboardDto
        {
            MetodoValoracion = await ObtenerMetodoAsync(context, userContext.IdEmpresa),
            PuedeEditar = userContext.EsJefeEmpresa && await _seguridad.PuedeAccederRutaAsync(userId, EContaxRoutes.Inventarios, EContaxAccion.Editar),
            PuedeConsultarTodasSucursales = userContext.EsJefeEmpresa,
            TotalProductos = items.Select(x => x.CodigoProducto).Distinct().Count(),
            StockTotal = items.Sum(x => x.StockDisponibleVigente),
            ProductosBajoMinimo = items.Count(x => x.Alerta == "MINIMO"),
            ProductosSobreMaximo = items.Count(x => x.Alerta == "MAXIMO"),
            LotesVencidos = lotesReporte.Count(x => x.FechaVencimiento.HasValue && x.FechaVencimiento.Value.Date <= fechaHoy),
            LotesPorVencer7Dias = lotesReporte.Count(x => x.FechaVencimiento.HasValue && x.FechaVencimiento.Value.Date > fechaHoy && x.FechaVencimiento.Value.Date <= fechaLimite7Dias),
            LotesPorVencer = lotesReporte.Count(x => x.FechaVencimiento.HasValue && x.FechaVencimiento.Value.Date > fechaHoy && x.FechaVencimiento.Value.Date <= fechaLimiteVencimiento),
            ValorInventario = Math.Round(lotesReporte.Sum(x => x.CantidadDisponible * x.CostoUnitario), 4),
            Sucursales = userContext.EsJefeEmpresa ? sucursales : sucursalesConsulta,
            Items = items
        };
    }

    public async Task<EContaxInventarioKardexDto> GetKardexAsync(int userId, int codigoProducto, int? sucursalId)
    {
        await _seguridad.ExigirAccionAsync(userId, EContaxRoutes.Inventarios, EContaxAccion.Ver);
        await using var context = await _dbFactory.CreateDbContextAsync();
        var userContext = await _tenant.GetContextAsync(context, userId);
        var sucursales = await _tenant.GetSucursalesEmpresaAsync(context, userContext.IdEmpresa);
        var producto = await context.Productos.AsNoTracking().FirstOrDefaultAsync(p =>
            p.Codigo == codigoProducto && p.Idempresa == userContext.IdEmpresa && p.Estado == true &&
            (p.Tipocompravena == null || p.Tipocompravena.ToUpper() != "SERVICIO"));
        if (producto is null)
            throw new InvalidOperationException("El producto no existe o no pertenece a la empresa.");

        if (!sucursalId.HasValue && userContext.EsJefeEmpresa)
        {
            var idsSucursal = sucursales.Select(x => x.IdSucursal).ToHashSet();
            sucursalId = producto.Idsucursal is > 0 && idsSucursal.Contains(producto.Idsucursal.Value)
                ? producto.Idsucursal
                : await context.EContaxProductosSucursales.AsNoTracking()
                    .Where(x => x.CodigoProducto == codigoProducto && x.IdEmpresa == userContext.IdEmpresa && idsSucursal.Contains(x.IdSucursal))
                    .OrderBy(x => x.IdSucursal)
                    .Select(x => (int?)x.IdSucursal)
                    .FirstOrDefaultAsync();
            sucursalId ??= userContext.IdSucursal is > 0 && idsSucursal.Contains(userContext.IdSucursal.Value)
                ? userContext.IdSucursal
                : sucursales.FirstOrDefault()?.IdSucursal;
        }

        var sucursal = ResolverSucursalUnica(userContext, sucursales, sucursalId);
        var perteneceASucursal = producto.Idsucursal == sucursal.IdSucursal ||
            await context.EContaxProductosSucursales.AnyAsync(x =>
                x.CodigoProducto == codigoProducto && x.IdEmpresa == userContext.IdEmpresa && x.IdSucursal == sucursal.IdSucursal);
        if (!perteneceASucursal)
            throw new InvalidOperationException("El producto no está asignado a la sucursal seleccionada.");

        var existencia = await context.EContaxInventarioExistencias.AsNoTracking().FirstOrDefaultAsync(x =>
            x.CodigoProducto == codigoProducto && x.IdEmpresa == userContext.IdEmpresa && x.IdSucursal == sucursal.IdSucursal);
        var movimientos = await (from movimiento in context.EContaxInventarioMovimientos.AsNoTracking()
            join usuario in context.Usuarios.AsNoTracking() on movimiento.IdUsuario equals usuario.IdUsuario into usuarios
            from usuario in usuarios.DefaultIfEmpty()
            where movimiento.CodigoProducto == codigoProducto && movimiento.IdEmpresa == userContext.IdEmpresa && movimiento.IdSucursal == sucursal.IdSucursal
            orderby movimiento.FechaMovimiento descending, movimiento.IdMovimiento descending
            select new EContaxInventarioMovimientoDto
            {
                IdMovimiento = movimiento.IdMovimiento,
                IdUsuario = movimiento.IdUsuario,
                NombreUsuario = usuario == null ? null : (usuario.Nombres + " " + usuario.Apellidos),
                FechaMovimiento = movimiento.FechaMovimiento,
                TipoMovimiento = movimiento.TipoMovimiento,
                Cantidad = movimiento.Cantidad,
                CostoUnitario = movimiento.CostoUnitario,
                CostoTotal = movimiento.CostoTotal,
                SaldoCantidad = movimiento.SaldoCantidad,
                SaldoCosto = movimiento.SaldoCosto,
                NumeroLote = movimiento.NumeroLote,
                FechaVencimiento = movimiento.FechaVencimiento,
                MetodoValoracion = movimiento.MetodoValoracion,
                Referencia = movimiento.Referencia,
                Observacion = movimiento.Observacion
            }).ToListAsync();

        return new EContaxInventarioKardexDto
        {
            CodigoProducto = codigoProducto,
            IdSucursal = sucursal.IdSucursal,
            NombreProducto = producto.Nombre,
            CodigoPrincipal = producto.CodigoPrincipal,
            NombreSucursal = sucursal.Nombre,
            StockActual = existencia?.StockActual ?? 0,
             StockReservado = existencia?.StockReservado ?? 0,
            CostoPromedio = existencia?.CostoPromedio ?? 0,
            MetodoValoracion = await ObtenerMetodoAsync(context, userContext.IdEmpresa),
            PuedeEditar = userContext.EsJefeEmpresa && await _seguridad.PuedeAccederRutaAsync(userId, EContaxRoutes.Inventarios, EContaxAccion.Editar),
            PuedeConsultarTodasSucursales = userContext.EsJefeEmpresa,
            Sucursales = userContext.EsJefeEmpresa ? sucursales : [sucursal],
            Movimientos = movimientos
        };
    }

    public async Task GuardarConfiguracionAsync(int userId, EContaxInventarioConfiguracionDto dto)
    {
        await _seguridad.ExigirAccionAsync(userId, EContaxRoutes.Inventarios, EContaxAccion.Editar);
        await using var context = await _dbFactory.CreateDbContextAsync();
        var userContext = await _tenant.GetContextAsync(context, userId);
        ExigirAdminEmpresa(userContext);
        var metodo = NormalizarMetodo(dto.MetodoValoracion);
        var config = await context.EContaxInventarioConfiguraciones.FindAsync(userContext.IdEmpresa);
        var metodoAnterior = config is null ? "PROMEDIO" : NormalizarMetodo(config.MetodoValoracion);
        if (config is null)
        {
            context.EContaxInventarioConfiguraciones.Add(new EContaxInventarioConfiguracion
            {
                IdEmpresa = userContext.IdEmpresa,
                MetodoValoracion = metodo,
                IdUsuarioActualizacion = userId,
                FechaActualizacion = DateTime.UtcNow
            });
        }
        else
        {
            config.MetodoValoracion = metodo;
            config.IdUsuarioActualizacion = userId;
            config.FechaActualizacion = DateTime.UtcNow;
        }
        if (!string.Equals(metodoAnterior, metodo, StringComparison.Ordinal))
            context.EContaxInventarioValoracionesHistorial.Add(new EContaxInventarioValoracionHistorial
            {
                IdEmpresa = userContext.IdEmpresa,
                MetodoAnterior = metodoAnterior,
                MetodoNuevo = metodo,
                IdUsuario = userId,
                Fecha = DateTime.UtcNow,
                Observacion = "Cambio de método de valoración"
            });
        RegistrarAuditoria(context, userId, "Configuración de valoración", new { Metodo = metodoAnterior }, new { Metodo = metodo }, new { Modulo = "Inventarios", Empresa = userContext.IdEmpresa });
        await context.SaveChangesAsync();
    }

    public async Task AplicarAjusteAsync(int userId, EContaxInventarioAjusteDto dto)
    {
        await _seguridad.ExigirAccionAsync(userId, EContaxRoutes.Inventarios, EContaxAccion.Editar);
        await using var context = await _dbFactory.CreateDbContextAsync();
        await using var transaction = await context.Database.BeginTransactionAsync(IsolationLevel.Serializable);
        var userContext = await _tenant.GetContextAsync(context, userId);
        ExigirAdminEmpresa(userContext);
        var sucursales = await _tenant.GetSucursalesEmpresaAsync(context, userContext.IdEmpresa);
        var sucursal = ResolverSucursalUnica(userContext, sucursales, dto.IdSucursal);
        var producto = await ObtenerProductoInventariableAsync(context, userContext.IdEmpresa, sucursal.IdSucursal, dto.CodigoProducto);
        if (producto is null)
            throw new InvalidOperationException("El producto no existe, no está asignado a la sucursal o es un servicio.");
        var tipo = (dto.TipoMovimiento ?? string.Empty).Trim().ToUpperInvariant();
        if (dto.Cantidad < 0 || (dto.Cantidad == 0 && tipo != "AJUSTE"))
            throw new InvalidOperationException("La cantidad debe ser mayor que cero, salvo para fijar el stock en cero.");

        var metodo = await ObtenerMetodoAsync(context, userContext.IdEmpresa);
        var existencia = await ObtenerExistenciaAsync(context, userContext.IdEmpresa, sucursal.IdSucursal, dto.CodigoProducto);
        var stockAnterior = existencia.StockActual;
        var costoAnterior = existencia.CostoPromedio;
        if (dto.StockMinimo is < 0 || dto.StockMaximo is < 0)
            throw new InvalidOperationException("Los stocks mínimo y máximo no pueden ser negativos.");
        if (dto.StockMinimo.HasValue) existencia.StockMinimo = Math.Max(0, dto.StockMinimo.Value);
        if (dto.StockMaximo.HasValue) existencia.StockMaximo = Math.Max(0, dto.StockMaximo.Value);
        if (existencia.StockMaximo > 0 && existencia.StockMinimo > existencia.StockMaximo)
            throw new InvalidOperationException("El stock mínimo no puede ser mayor que el stock máximo.");

        var huboMovimiento = false;
        var tipoMovimientoLog = tipo;
        var cantidadMovimiento = dto.Cantidad;
        decimal? costoMovimiento = null;
        List<LoteSalida>? lotesSalidaMovimiento = null;
        if (tipo == "BAJA_VENCIDO")
        {
            lotesSalidaMovimiento = new();
            costoMovimiento = await AplicarSalidaAsync(context, existencia, dto.Cantidad, metodo, lotesSalidaMovimiento, soloVencidos: true);
            huboMovimiento = true;
        }
        else if (tipo == "ENTRADA")
        {
            var costo = dto.CostoUnitario ?? existencia.CostoPromedio;
            if (costo < 0) throw new InvalidOperationException("El costo unitario no puede ser negativo.");
            await ValidarEntradaConLoteAsync(context, existencia, dto.Cantidad, dto.NumeroLote, dto.FechaVencimiento);
            AgregarEntrada(context, existencia, dto.Cantidad, costo, metodo, dto.NumeroLote, dto.FechaVencimiento);
            costoMovimiento = costo;
            huboMovimiento = true;
        }
        else if (tipo == "SALIDA")
        {
            costoMovimiento = await AplicarSalidaAsync(context, existencia, dto.Cantidad, metodo);
            huboMovimiento = true;
        }
        else if (tipo == "AJUSTE")
        {
            var diferencia = dto.Cantidad - existencia.StockActual;
            if (diferencia > 0)
            {
                var costo = dto.CostoUnitario ?? existencia.CostoPromedio;
                if (costo < 0) throw new InvalidOperationException("El costo unitario no puede ser negativo.");
                await ValidarEntradaConLoteAsync(context, existencia, diferencia, dto.NumeroLote, dto.FechaVencimiento);
                AgregarEntrada(context, existencia, diferencia, costo, metodo, dto.NumeroLote, dto.FechaVencimiento);
                tipoMovimientoLog = "AJUSTE_ENTRADA";
                cantidadMovimiento = diferencia;
                costoMovimiento = costo;
                huboMovimiento = true;
            }
            else if (diferencia < 0)
            {
                tipoMovimientoLog = "AJUSTE_SALIDA";
                cantidadMovimiento = Math.Abs(diferencia);
                costoMovimiento = await AplicarSalidaAsync(context, existencia, cantidadMovimiento, metodo);
                huboMovimiento = true;
            }
            else
                existencia.FechaActualizacion = DateTime.UtcNow;
        }
        else
        {
            throw new InvalidOperationException("Tipo de movimiento no válido.");
        }

        if (huboMovimiento)
        {
            var lote = lotesSalidaMovimiento?.Count == 1 ? lotesSalidaMovimiento[0] : null;
            context.EContaxInventarioMovimientos.Add(CrearMovimiento(
                existencia, userId, metodo, tipo == "BAJA_VENCIDO" ? "BAJA_VENCIDO" : tipoMovimientoLog,
                cantidadMovimiento, dto.Observacion, "AJUSTE", costoMovimiento, null,
                lote?.NumeroLote ?? dto.NumeroLote, lote?.FechaVencimiento ?? dto.FechaVencimiento));
        }
        RegistrarAuditoria(context, userId, "Ajuste de inventario", new { Stock = stockAnterior, Costo = costoAnterior }, new { Stock = existencia.StockActual, Costo = existencia.CostoPromedio }, new { Modulo = "Inventarios", Tipo = tipoMovimientoLog, Producto = dto.CodigoProducto, Sucursal = sucursal.IdSucursal, Observacion = dto.Observacion });
        await context.SaveChangesAsync();
        await transaction.CommitAsync();
    }

    public async Task PrepararSalidaVentaAsync(
        AppDbContext context,
        int userId,
        int idEmpresa,
        int idSucursal,
        IReadOnlyCollection<Detallefactura> detalles,
        string? referencia)
    {
        var codigos = detalles.Where(x => x.Codproducto > 0 && x.Cantproducto > 0)
            .Select(x => x.Codproducto).Distinct().ToList();
        if (codigos.Count == 0)
            return;

        var productos = await context.Productos
            .Where(x => x.Idempresa == idEmpresa && codigos.Contains(x.Codigo) && x.Estado == true)
            .ToDictionaryAsync(x => x.Codigo);
        var metodo = await ObtenerMetodoAsync(context, idEmpresa);
        var movimientosConLotes = new List<(EContaxInventarioMovimiento Movimiento, List<LoteSalida> Lotes)>();
        foreach (var grupo in detalles.Where(x => x.Codproducto > 0 && x.Cantproducto > 0).GroupBy(x => x.Codproducto).OrderBy(x => x.Key))
        {
            if (!productos.TryGetValue(grupo.Key, out var producto) ||
                string.Equals(producto.Tipocompravena, "SERVICIO", StringComparison.OrdinalIgnoreCase))
                continue;

            var existencia = await ObtenerExistenciaAsync(context, idEmpresa, idSucursal, grupo.Key);
            var factor = ObtenerFactorConversion(producto.FactorConversionInventario);
            var cantidadBase = grupo.Sum(x => x.Cantproducto) * factor;
            var lotesSalida = new List<LoteSalida>();
            var costo = await AplicarSalidaAsync(context, existencia, cantidadBase, metodo, lotesSalida);
            foreach (var detalle in grupo)
                detalle.Costo = Math.Round(costo * factor, 2, MidpointRounding.AwayFromZero);
            var movimiento = CrearMovimiento(
                existencia, userId, metodo, "SALIDA", cantidadBase, "Venta de producto", "FACTURA", costo, referencia);
            context.EContaxInventarioMovimientos.Add(movimiento);
            movimientosConLotes.Add((movimiento, lotesSalida));
        }
        await context.SaveChangesAsync();
        foreach (var asignacion in movimientosConLotes)
        {
            foreach (var lote in asignacion.Lotes.Where(x => x.IdLote > 0 && x.Cantidad > 0))
            {
                context.EContaxInventarioMovimientosLotes.Add(new EContaxInventarioMovimientoLote
                {
                    IdMovimiento = asignacion.Movimiento.IdMovimiento,
                    IdLote = lote.IdLote,
                    Cantidad = lote.Cantidad,
                    CostoUnitario = lote.CostoUnitario
                });
            }
        }
        if (movimientosConLotes.Any(x => x.Lotes.Any(y => y.IdLote > 0)))
            await context.SaveChangesAsync();
    }

    public async Task RegistrarEntradaCompraAsync(
        AppDbContext context,
        int userId,
        int idEmpresa,
        int idSucursal,
        IReadOnlyCollection<(int CodigoProducto, decimal Cantidad, decimal CostoUnitarioSinImpuestos, string? NumeroLote, DateTime? FechaVencimiento)> items,
        string? referencia)
    {
        if (items.Any(x => x.CodigoProducto <= 0 || x.Cantidad < 0 || x.CostoUnitarioSinImpuestos < 0))
            throw new InvalidOperationException("La recepción contiene productos, cantidades o costos no válidos.");
        if (items.Any(x => x.Cantidad > 0 && x.FechaVencimiento is { } fecha && fecha.Date <= DateTime.UtcNow.Date))
            throw new InvalidOperationException("No se puede ingresar inventario con lotes vencidos o que vencen hoy.");

        var metodo = await ObtenerMetodoAsync(context, idEmpresa);
        foreach (var grupo in items.Where(x => x.CodigoProducto > 0 && x.Cantidad > 0).GroupBy(x => x.CodigoProducto))
        {
            var producto = await context.Productos.FirstOrDefaultAsync(x =>
                x.Codigo == grupo.Key && x.Idempresa == idEmpresa && x.Estado == true);
            if (producto is null)
                throw new InvalidOperationException($"El producto {grupo.Key} no existe o está inactivo en la empresa.");
            if (string.Equals(producto.Tipocompravena, "SERVICIO", StringComparison.OrdinalIgnoreCase))
                continue;
            if (producto.Idsucursal != idSucursal && !await context.EContaxProductosSucursales.AnyAsync(x =>
                    x.CodigoProducto == grupo.Key && x.IdEmpresa == idEmpresa && x.IdSucursal == idSucursal))
                throw new InvalidOperationException($"El producto '{producto.Nombre}' no está asignado a la sucursal de la recepción.");

            var existencia = await ObtenerExistenciaAsync(context, idEmpresa, idSucursal, grupo.Key);
            var factor = ObtenerFactorConversion(producto.FactorConversionInventario);
            foreach (var item in grupo.Where(x => x.Cantidad > 0))
            {
                var cantidadBase = item.Cantidad * factor;
                var costoBase = item.CostoUnitarioSinImpuestos / factor;
                await ValidarEntradaConLoteAsync(context, existencia, cantidadBase, item.NumeroLote, item.FechaVencimiento);
                AgregarEntrada(context, existencia, cantidadBase, costoBase, metodo, item.NumeroLote, item.FechaVencimiento);
                context.EContaxInventarioMovimientos.Add(CrearMovimiento(
                    existencia, userId, metodo, "ENTRADA", cantidadBase, "Recepción de compra", "RECEPCION", costoBase, referencia,
                    item.NumeroLote, item.FechaVencimiento));
            }
        }
    }

    public async Task ReintegrarNotaCreditoVentaAsync(int userId, int secNotaCredito)
    {
        await using var context = await _dbFactory.CreateDbContextAsync();
        await using var transaction = await context.Database.BeginTransactionAsync(IsolationLevel.Serializable);

        var nota = await context.NotaCreditos.AsNoTracking()
            .FirstOrDefaultAsync(x => x.Sec == secNotaCredito && x.Estado == true);
        if (nota is null || !Simetric.Services.DocumentoAutorizacionHelper.EstaAutorizado(nota.Autorizado) || nota.IdDocModificado is not > 0)
            return;

        var factura = await context.Facturas.AsNoTracking()
            .FirstOrDefaultAsync(x => x.Codfactura == nota.IdDocModificado.Value && x.Estado == true);
        if (factura?.Idempresa is not > 0)
            return;

        if (!await context.EContaxEmpresas.AnyAsync(x => x.IdEmpresa == factura.Idempresa.Value && x.Estado))
            return;

        var actor = await _tenant.GetContextAsync(context, userId);
        if (actor.IdEmpresa != factura.Idempresa.Value)
            throw new InvalidOperationException("La nota de crédito no pertenece a la empresa activa de E-Contax.");

        var idSucursal = factura.Idsucursal ?? actor.IdSucursal;
        if (idSucursal is not > 0)
            throw new InvalidOperationException("La factura no tiene una sucursal de inventario asociada.");

        var referencia = $"NC:{secNotaCredito}";
        if (await context.EContaxInventarioMovimientos.AnyAsync(x =>
                x.IdEmpresa == actor.IdEmpresa && x.ReferenciaTipo == "NOTA_CREDITO" && x.Referencia == referencia))
        {
            await transaction.CommitAsync();
            return;
        }

        var detalles = await context.DetallesNotaCredito.AsNoTracking()
            .Where(x => x.CodNotaCredito == secNotaCredito && x.CantProducto > 0)
            .ToListAsync();
        var originales = await context.Detallefacturas.AsNoTracking()
            .Where(x => x.Codfactura == factura.Codfactura && x.Costo > 0)
            .ToListAsync();
        var referenciaFactura = factura.Codfactura.ToString(System.Globalization.CultureInfo.InvariantCulture);
        var lotesVenta = await (
            from asignacion in context.EContaxInventarioMovimientosLotes.AsNoTracking()
            join movimiento in context.EContaxInventarioMovimientos.AsNoTracking()
                on asignacion.IdMovimiento equals movimiento.IdMovimiento
            join lote in context.EContaxInventarioLotes.AsNoTracking()
                on asignacion.IdLote equals lote.IdLote
            where movimiento.IdEmpresa == actor.IdEmpresa &&
                  movimiento.IdSucursal == idSucursal.Value &&
                  movimiento.ReferenciaTipo == "FACTURA" &&
                  movimiento.Referencia == referenciaFactura &&
                  movimiento.TipoMovimiento == "SALIDA"
            select new
            {
                movimiento.CodigoProducto,
                asignacion.Cantidad,
                asignacion.CostoUnitario,
                lote.NumeroLote,
                lote.FechaVencimiento
            }).ToListAsync();
        var metodo = await ObtenerMetodoAsync(context, actor.IdEmpresa);

        foreach (var grupo in detalles.GroupBy(x => x.CodProducto))
        {
            var producto = await ObtenerProductoInventariableAsync(context, actor.IdEmpresa, idSucursal.Value, grupo.Key);
            if (producto is null)
                continue;

            var existencia = await ObtenerExistenciaAsync(context, actor.IdEmpresa, idSucursal.Value, grupo.Key);
            var factor = ObtenerFactorConversion(producto.FactorConversionInventario);
            var cantidadBase = grupo.Sum(x => x.CantProducto ?? 0m) * factor;
            if (cantidadBase <= 0)
                continue;

            var restante = cantidadBase;
            foreach (var loteVenta in lotesVenta.Where(x => x.CodigoProducto == grupo.Key))
            {
                if (restante <= 0)
                    break;

                var cantidadLote = Math.Min(restante, loteVenta.Cantidad);
                if (cantidadLote <= 0)
                    continue;

                AgregarEntrada(context, existencia, cantidadLote, loteVenta.CostoUnitario, metodo, loteVenta.NumeroLote, loteVenta.FechaVencimiento);
                context.EContaxInventarioMovimientos.Add(CrearMovimiento(
                    existencia,
                    userId,
                    metodo,
                    "DEVOLUCION_VENTA",
                    cantidadLote,
                    "Reintegro por nota de crédito autorizada",
                    "NOTA_CREDITO",
                    loteVenta.CostoUnitario,
                    referencia,
                    loteVenta.NumeroLote,
                    loteVenta.FechaVencimiento));
                restante -= cantidadLote;
            }

            if (restante > 0)
            {
                var costoComercial = originales.Where(x => x.Codproducto == grupo.Key).Sum(x => x.Costo ?? 0m);
                var cantidadOriginal = originales.Where(x => x.Codproducto == grupo.Key).Sum(x => x.Cantproducto);
                if (cantidadOriginal > 0)
                    costoComercial /= cantidadOriginal;
                if (costoComercial <= 0)
                    costoComercial = existencia.CostoPromedio * factor;

                var costoBase = Math.Max(0, costoComercial / factor);
                AgregarEntrada(context, existencia, restante, costoBase, metodo);
                context.EContaxInventarioMovimientos.Add(CrearMovimiento(
                    existencia,
                    userId,
                    metodo,
                    "DEVOLUCION_VENTA",
                    restante,
                    "Reintegro por nota de crédito autorizada",
                    "NOTA_CREDITO",
                    costoBase,
                    referencia));
            }
        }

        await context.SaveChangesAsync();
        await transaction.CommitAsync();
    }

    public async Task RevertirNotaCreditoVentaAsync(int userId, int secNotaCredito)
    {
        await using var context = await _dbFactory.CreateDbContextAsync();
        await using var transaction = await context.Database.BeginTransactionAsync(IsolationLevel.Serializable);

        var nota = await context.NotaCreditos.AsNoTracking()
            .FirstOrDefaultAsync(x => x.Sec == secNotaCredito);
        if (nota?.IdDocModificado is not > 0)
            return;

        var factura = await context.Facturas.AsNoTracking()
            .FirstOrDefaultAsync(x => x.Codfactura == nota.IdDocModificado.Value);
        if (factura?.Idempresa is not > 0 || !await context.EContaxEmpresas.AnyAsync(x => x.IdEmpresa == factura.Idempresa.Value && x.Estado))
            return;

        var actor = await _tenant.GetContextAsync(context, userId);
        if (actor.IdEmpresa != factura.Idempresa.Value)
            throw new InvalidOperationException("La nota de crédito no pertenece a la empresa activa de E-Contax.");

        var referencia = $"NC:{secNotaCredito}";
        if (!await context.EContaxInventarioMovimientos.AnyAsync(x =>
                x.IdEmpresa == actor.IdEmpresa && x.ReferenciaTipo == "NOTA_CREDITO" && x.Referencia == referencia))
        {
            await transaction.CommitAsync();
            return;
        }

        var referenciaReversa = $"NC_REVERSA:{secNotaCredito}";
        if (await context.EContaxInventarioMovimientos.AnyAsync(x =>
                x.IdEmpresa == actor.IdEmpresa && x.ReferenciaTipo == "NOTA_CREDITO_REVERSA" && x.Referencia == referenciaReversa))
        {
            await transaction.CommitAsync();
            return;
        }

        var idSucursal = factura.Idsucursal ?? actor.IdSucursal;
        if (idSucursal is not > 0)
            throw new InvalidOperationException("La factura no tiene una sucursal de inventario asociada.");

        var movimientos = await context.EContaxInventarioMovimientos.AsNoTracking()
            .Where(x => x.IdEmpresa == actor.IdEmpresa && x.IdSucursal == idSucursal.Value &&
                        x.ReferenciaTipo == "NOTA_CREDITO" && x.Referencia == referencia)
            .GroupBy(x => x.CodigoProducto)
            .Select(x => new { CodigoProducto = x.Key, Cantidad = x.Sum(y => y.Cantidad) })
            .ToListAsync();
        var metodo = await ObtenerMetodoAsync(context, actor.IdEmpresa);

        foreach (var movimiento in movimientos)
        {
            var existencia = await ObtenerExistenciaAsync(context, actor.IdEmpresa, idSucursal.Value, movimiento.CodigoProducto);
            var costo = await AplicarSalidaAsync(context, existencia, movimiento.Cantidad, metodo);
            context.EContaxInventarioMovimientos.Add(CrearMovimiento(
                existencia,
                userId,
                metodo,
                "DEVOLUCION_VENTA_REVERSA",
                movimiento.Cantidad,
                "Reverso de nota de crédito anulada",
                "NOTA_CREDITO_REVERSA",
                costo,
                referenciaReversa));
        }

        await context.SaveChangesAsync();
        await transaction.CommitAsync();
    }

    public async Task<EContaxInventarioReporteDto> GetReporteAsync(int userId, EContaxInventarioReporteFiltroDto filtro)
    {
        await _seguridad.ExigirAccionAsync(userId, EContaxRoutes.Inventarios, EContaxAccion.Ver);
        await using var context = await _dbFactory.CreateDbContextAsync();
        var actor = await _tenant.GetContextAsync(context, userId);
        var sucursales = await _tenant.GetSucursalesEmpresaAsync(context, actor.IdEmpresa);

        var query = from movimiento in context.EContaxInventarioMovimientos.AsNoTracking()
                    join producto in context.Productos.AsNoTracking() on movimiento.CodigoProducto equals producto.Codigo
                    join sucursal in context.EContaxSucursales.AsNoTracking() on movimiento.IdSucursal equals sucursal.IdSucursal
                    join usuario in context.Usuarios.AsNoTracking() on movimiento.IdUsuario equals usuario.IdUsuario into usuarios
                    from usuario in usuarios.DefaultIfEmpty()
                    where movimiento.IdEmpresa == actor.IdEmpresa && producto.Idempresa == actor.IdEmpresa &&
                          sucursal.IdEmpresa == actor.IdEmpresa
                    select new { movimiento, producto, sucursal, usuario };

        if (!actor.EsJefeEmpresa)
            query = query.Where(x => x.movimiento.IdSucursal == actor.IdSucursal);
        else if (filtro.IdSucursal is > 0)
            query = query.Where(x => x.movimiento.IdSucursal == filtro.IdSucursal.Value);
        if (filtro.FechaDesde.HasValue)
            query = query.Where(x => x.movimiento.FechaMovimiento >= filtro.FechaDesde.Value.Date);
        if (filtro.FechaHasta.HasValue)
            query = query.Where(x => x.movimiento.FechaMovimiento < filtro.FechaHasta.Value.Date.AddDays(1));
        if (filtro.CodigoProducto is > 0)
            query = query.Where(x => x.movimiento.CodigoProducto == filtro.CodigoProducto.Value);
        if (!string.IsNullOrWhiteSpace(filtro.Producto))
        {
            var productoFiltro = filtro.Producto.Trim();
            query = query.Where(x => (x.producto.Nombre ?? "").Contains(productoFiltro) ||
                                     (x.producto.CodigoPrincipal ?? "").Contains(productoFiltro));
        }
        if (!string.IsNullOrWhiteSpace(filtro.TipoMovimiento))
            query = query.Where(x => x.movimiento.TipoMovimiento == filtro.TipoMovimiento.Trim().ToUpper());

        var movimientos = await query
            .OrderByDescending(x => x.movimiento.FechaMovimiento)
            .ThenByDescending(x => x.movimiento.IdMovimiento)
            .Take(10000)
            .Select(x => new EContaxInventarioReporteMovimientoDto
            {
                FechaMovimiento = x.movimiento.FechaMovimiento,
                IdUsuario = x.movimiento.IdUsuario,
                NombreUsuario = x.usuario == null ? null : (x.usuario.Nombres + " " + x.usuario.Apellidos),
                CodigoProducto = x.movimiento.CodigoProducto,
                CodigoPrincipal = x.producto.CodigoPrincipal,
                NombreProducto = x.producto.Nombre,
                IdSucursal = x.movimiento.IdSucursal,
                NombreSucursal = x.sucursal.Nombre,
                TipoMovimiento = x.movimiento.TipoMovimiento,
                Cantidad = x.movimiento.Cantidad,
                CostoUnitario = x.movimiento.CostoUnitario,
                CostoTotal = x.movimiento.CostoTotal,
                SaldoCantidad = x.movimiento.SaldoCantidad,
                SaldoCosto = x.movimiento.SaldoCosto,
                NumeroLote = x.movimiento.NumeroLote,
                FechaVencimiento = x.movimiento.FechaVencimiento,
                Referencia = x.movimiento.Referencia,
                Observacion = x.movimiento.Observacion
            })
            .ToListAsync();

        return new EContaxInventarioReporteDto
        {
            Sucursales = actor.EsJefeEmpresa ? sucursales : sucursales.Where(x => x.IdSucursal == actor.IdSucursal).ToList(),
            Movimientos = movimientos,
            TotalEntradas = movimientos.Where(x => EsEntradaReporte(x.TipoMovimiento)).Sum(x => x.Cantidad),
            TotalSalidas = movimientos.Where(x => EsSalidaReporte(x.TipoMovimiento)).Sum(x => x.Cantidad),
            ValorEntradas = movimientos.Where(x => EsEntradaReporte(x.TipoMovimiento)).Sum(x => x.CostoTotal),
            ValorSalidas = movimientos.Where(x => EsSalidaReporte(x.TipoMovimiento)).Sum(x => x.CostoTotal)
        };
    }

    public async Task<EContaxInventarioOperacionResultadoDto> CrearTransferenciaAsync(int userId, EContaxInventarioTransferenciaDto dto)
    {
        await _seguridad.ExigirAccionAsync(userId, EContaxRoutes.Inventarios, EContaxAccion.Editar);
        await using var context = await _dbFactory.CreateDbContextAsync();
        await using var transaction = await context.Database.BeginTransactionAsync(IsolationLevel.Serializable);
        var actor = await _tenant.GetContextAsync(context, userId);
        ExigirAdminEmpresa(actor);
        var sucursales = await _tenant.GetSucursalesEmpresaAsync(context, actor.IdEmpresa);
        var origen = ResolverSucursalUnica(actor, sucursales, dto.IdSucursalOrigen ?? actor.IdSucursal);
        var destino = sucursales.FirstOrDefault(x => x.IdSucursal == dto.IdSucursalDestino);
        if (destino is null || destino.IdSucursal == origen.IdSucursal)
            throw new InvalidOperationException("La sucursal destino no es válida.");
        ValidarDetallesOperacion(dto.Detalles);

        var transferencia = new EContaxInventarioTransferencia
        {
            IdEmpresa = actor.IdEmpresa,
            IdSucursalOrigen = origen.IdSucursal,
            IdSucursalDestino = destino.IdSucursal,
            FechaTransferencia = DateTime.UtcNow,
            IdUsuario = userId,
            Observacion = Normalizar(dto.Observacion)
        };
        context.EContaxInventarioTransferencias.Add(transferencia);
        await context.SaveChangesAsync();
        transferencia.NumeroTransferencia = $"TR-{transferencia.IdTransferencia:D6}";
        var metodo = await ObtenerMetodoAsync(context, actor.IdEmpresa);

        foreach (var grupo in dto.Detalles.GroupBy(x => x.CodigoProducto))
        {
            var producto = await ObtenerProductoInventariableAsync(context, actor.IdEmpresa, origen.IdSucursal, grupo.Key);
            if (producto is null)
                throw new InvalidOperationException($"El producto {grupo.Key} no está disponible en la sucursal de origen.");
            var cantidad = grupo.Sum(x => x.Cantidad);
            var existenciaOrigen = await ObtenerExistenciaAsync(context, actor.IdEmpresa, origen.IdSucursal, grupo.Key);
            var lotesSalida = new List<LoteSalida>();
            var costo = await AplicarSalidaAsync(context, existenciaOrigen, cantidad, metodo, lotesSalida);
            var existenciaDestino = await ObtenerExistenciaAsync(context, actor.IdEmpresa, destino.IdSucursal, grupo.Key);
            foreach (var lote in lotesSalida)
                AgregarEntrada(context, existenciaDestino, lote.Cantidad, lote.CostoUnitario, metodo, lote.NumeroLote, lote.FechaVencimiento);
            transferencia.Detalles.Add(new EContaxInventarioTransferenciaDetalle { CodigoProducto = grupo.Key, Cantidad = cantidad, CostoUnitario = costo });
            var loteMovimiento = lotesSalida.Count == 1 ? lotesSalida[0] : null;
            context.EContaxInventarioMovimientos.Add(CrearMovimiento(existenciaOrigen, userId, metodo, "TRANSFER_SALIDA", cantidad, dto.Observacion, "TRANSFERENCIA", costo, transferencia.NumeroTransferencia, loteMovimiento?.NumeroLote, loteMovimiento?.FechaVencimiento));
            context.EContaxInventarioMovimientos.Add(CrearMovimiento(existenciaDestino, userId, metodo, "TRANSFER_ENTRADA", cantidad, dto.Observacion, "TRANSFERENCIA", costo, transferencia.NumeroTransferencia, loteMovimiento?.NumeroLote, loteMovimiento?.FechaVencimiento));
        }
        RegistrarAuditoria(context, userId, "Transferencia de inventario", null, new { Numero = transferencia.NumeroTransferencia, Origen = origen.IdSucursal, Destino = destino.IdSucursal }, new { Modulo = "Inventarios", Productos = dto.Detalles.Select(x => new { x.CodigoProducto, x.Cantidad }), Observacion = dto.Observacion });
        await context.SaveChangesAsync();
        await transaction.CommitAsync();
        return new EContaxInventarioOperacionResultadoDto { Numero = transferencia.NumeroTransferencia, Mensaje = "Transferencia aplicada correctamente." };
    }

    public async Task<EContaxInventarioOperacionResultadoDto> RegistrarDevolucionCompraAsync(int userId, EContaxInventarioDevolucionDto dto)
    {
        await _seguridad.ExigirAccionAsync(userId, EContaxRoutes.ComprasRecepciones, EContaxAccion.Editar);
        await using var context = await _dbFactory.CreateDbContextAsync();
        await using var transaction = await context.Database.BeginTransactionAsync(IsolationLevel.Serializable);
        var actor = await _tenant.GetContextAsync(context, userId);
        ExigirAdminEmpresa(actor);
        var recepcion = await context.EContaxRecepcionesCompra.Include(x => x.Detalles)
            .FirstOrDefaultAsync(x => x.IdRecepcion == dto.IdRecepcion && x.IdEmpresa == actor.IdEmpresa && x.Estado.StartsWith("RECIBIDA"));
        if (recepcion is null)
            throw new InvalidOperationException("La recepción no existe o no está confirmada.");
        ValidarDetallesOperacion(dto.Detalles);
        var cantidadesDevueltas = await context.EContaxInventarioDevolucionesDetalles.AsNoTracking()
            .Where(x => x.Devolucion!.IdRecepcion == dto.IdRecepcion && x.Devolucion.Estado != "ANULADA")
            .GroupBy(x => x.CodigoProducto).Select(x => new { x.Key, Cantidad = x.Sum(y => y.Cantidad) }).ToDictionaryAsync(x => x.Key, x => x.Cantidad);
        var metodo = await ObtenerMetodoAsync(context, actor.IdEmpresa);
        var devolucion = new EContaxInventarioDevolucion { IdEmpresa = actor.IdEmpresa, IdSucursal = recepcion.IdSucursal, IdRecepcion = recepcion.IdRecepcion, FechaDevolucion = DateTime.UtcNow, IdUsuario = userId, Observacion = Normalizar(dto.Observacion) };
        context.EContaxInventarioDevoluciones.Add(devolucion);
        await context.SaveChangesAsync();
        devolucion.NumeroDevolucion = $"DV-{devolucion.IdDevolucion:D6}";
        foreach (var grupo in dto.Detalles.GroupBy(x => x.CodigoProducto))
        {
            var recibido = recepcion.Detalles.Where(x => x.CodigoProducto == grupo.Key).Sum(x => x.CantidadRecibida);
            var cantidad = grupo.Sum(x => x.Cantidad);
            if (cantidad > recibido - cantidadesDevueltas.GetValueOrDefault(grupo.Key))
                throw new InvalidOperationException($"La devolución del producto {grupo.Key} supera la cantidad recibida disponible.");
            var existencia = await ObtenerExistenciaAsync(context, actor.IdEmpresa, recepcion.IdSucursal, grupo.Key);
            var costo = await AplicarSalidaAsync(context, existencia, cantidad, metodo);
            devolucion.Detalles.Add(new EContaxInventarioDevolucionDetalle { CodigoProducto = grupo.Key, Cantidad = cantidad, CostoUnitario = costo });
            context.EContaxInventarioMovimientos.Add(CrearMovimiento(existencia, userId, metodo, "DEVOLUCION_COMPRA", cantidad, dto.Observacion, "DEVOLUCION_COMPRA", costo, devolucion.NumeroDevolucion));
        }
        RegistrarAuditoria(context, userId, "Devolución de compra", null, new { Numero = devolucion.NumeroDevolucion }, new { Modulo = "Inventarios", Recepcion = dto.IdRecepcion, Productos = dto.Detalles.Select(x => new { x.CodigoProducto, x.Cantidad }), Observacion = dto.Observacion });
        await context.SaveChangesAsync();
        await transaction.CommitAsync();
        return new EContaxInventarioOperacionResultadoDto { Numero = devolucion.NumeroDevolucion, Mensaje = "Devolución aplicada correctamente." };
    }

    public async Task<EContaxInventarioOperacionResultadoDto> AplicarTomaFisicaAsync(int userId, EContaxInventarioTomaFisicaDto dto)
    {
        await _seguridad.ExigirAccionAsync(userId, EContaxRoutes.Inventarios, EContaxAccion.Editar);
        await using var context = await _dbFactory.CreateDbContextAsync();
        await using var transaction = await context.Database.BeginTransactionAsync(IsolationLevel.Serializable);
        var actor = await _tenant.GetContextAsync(context, userId);
        ExigirAdminEmpresa(actor);
        var sucursales = await _tenant.GetSucursalesEmpresaAsync(context, actor.IdEmpresa);
        var sucursal = ResolverSucursalUnica(actor, sucursales, dto.IdSucursal);
        if (dto.Detalles.Count == 0 || dto.Detalles.Any(x => x.CodigoProducto <= 0 || x.StockContado < 0))
            throw new InvalidOperationException("La toma física debe contener productos con cantidades válidas.");
        var toma = new EContaxInventarioTomaFisica { IdEmpresa = actor.IdEmpresa, IdSucursal = sucursal.IdSucursal, FechaToma = DateTime.UtcNow, IdUsuario = userId, Observacion = Normalizar(dto.Observacion) };
        context.EContaxInventarioTomasFisicas.Add(toma);
        await context.SaveChangesAsync();
        toma.NumeroToma = $"TF-{toma.IdToma:D6}";
        var metodo = await ObtenerMetodoAsync(context, actor.IdEmpresa);
        foreach (var item in dto.Detalles.GroupBy(x => x.CodigoProducto).Select(x => new { x.Key, Stock = x.Last().StockContado }))
        {
            var producto = await ObtenerProductoInventariableAsync(context, actor.IdEmpresa, sucursal.IdSucursal, item.Key);
            if (producto is null) throw new InvalidOperationException($"El producto {item.Key} no está asignado a la sucursal.");
            var existencia = await ObtenerExistenciaAsync(context, actor.IdEmpresa, sucursal.IdSucursal, item.Key);
            var sistema = existencia.StockActual;
            var diferencia = item.Stock - sistema;
            var costo = existencia.CostoPromedio;
            var detalle = dto.Detalles.Last(x => x.CodigoProducto == item.Key);
            if (diferencia > 0)
            {
                await ValidarEntradaConLoteAsync(context, existencia, diferencia, detalle.NumeroLote, detalle.FechaVencimiento);
                AgregarEntrada(context, existencia, diferencia, costo, metodo, detalle.NumeroLote, detalle.FechaVencimiento);
            }
            else if (diferencia < 0) costo = await AplicarSalidaAsync(context, existencia, Math.Abs(diferencia), metodo);
            toma.Detalles.Add(new EContaxInventarioTomaFisicaDetalle { CodigoProducto = item.Key, StockSistema = sistema, StockContado = item.Stock, Diferencia = diferencia, CostoUnitario = costo });
            if (diferencia != 0) context.EContaxInventarioMovimientos.Add(CrearMovimiento(existencia, userId, metodo, diferencia > 0 ? "TOMA_FISICA_ENTRADA" : "TOMA_FISICA_SALIDA", Math.Abs(diferencia), dto.Observacion, "TOMA_FISICA", costo, toma.NumeroToma));
        }
        RegistrarAuditoria(context, userId, "Toma física de inventario", null, new { Numero = toma.NumeroToma }, new { Modulo = "Inventarios", Sucursal = sucursal.IdSucursal, Productos = dto.Detalles.Select(x => new { x.CodigoProducto, x.StockContado }), Observacion = dto.Observacion });
        await context.SaveChangesAsync();
        await transaction.CommitAsync();
        return new EContaxInventarioOperacionResultadoDto { Numero = toma.NumeroToma, Mensaje = "Toma física aplicada correctamente." };
    }

    public async Task ReservarAsync(int userId, EContaxInventarioReservaDto dto, bool liberar = false)
    {
        await _seguridad.ExigirAccionAsync(userId, EContaxRoutes.Inventarios, EContaxAccion.Editar);
        await using var context = await _dbFactory.CreateDbContextAsync();
        await using var transaction = await context.Database.BeginTransactionAsync(IsolationLevel.Serializable);
        var actor = await _tenant.GetContextAsync(context, userId);
        var sucursales = await _tenant.GetSucursalesEmpresaAsync(context, actor.IdEmpresa);
        var sucursal = ResolverSucursalUnica(actor, sucursales, dto.IdSucursal);
        var detalles = dto.Detalles.Count > 0
            ? dto.Detalles
            : [new EContaxInventarioOperacionDetalleDto { CodigoProducto = dto.CodigoProducto, Cantidad = dto.Cantidad }];
        ValidarDetallesOperacion(detalles);
        var metodo = await ObtenerMetodoAsync(context, actor.IdEmpresa);
        foreach (var grupo in detalles.GroupBy(x => x.CodigoProducto))
        {
            var producto = await ObtenerProductoInventariableAsync(context, actor.IdEmpresa, sucursal.IdSucursal, grupo.Key);
            if (producto is null)
                throw new InvalidOperationException($"El producto {grupo.Key} no está asignado a la sucursal.");

            var cantidad = grupo.Sum(x => x.Cantidad);
            var existencia = await ObtenerExistenciaAsync(context, actor.IdEmpresa, sucursal.IdSucursal, grupo.Key);
            var fechaHoy = DateTime.UtcNow.Date;
            var lotesVigentes = await context.EContaxInventarioLotes
                .Where(x => x.IdEmpresa == actor.IdEmpresa && x.IdSucursal == sucursal.IdSucursal &&
                            x.CodigoProducto == grupo.Key && x.CantidadDisponible > 0 &&
                            (!x.FechaVencimiento.HasValue || x.FechaVencimiento.Value.Date > fechaHoy))
                .Select(x => x.CantidadDisponible)
                .ToListAsync();
            var stockVigente = lotesVigentes.Count == 0
                ? existencia.StockActual
                : Math.Min(existencia.StockActual, lotesVigentes.Sum());
            var disponible = Math.Max(0, stockVigente - existencia.StockReservado);
            if (!liberar && cantidad > disponible) throw new InvalidOperationException($"La cantidad de '{producto.Nombre}' supera el stock disponible para reservar.");
            if (liberar && cantidad > existencia.StockReservado) throw new InvalidOperationException($"La cantidad de '{producto.Nombre}' supera el stock reservado.");
            existencia.StockReservado = Math.Round(existencia.StockReservado + (liberar ? -cantidad : cantidad), 4);
            existencia.FechaActualizacion = DateTime.UtcNow;
            context.EContaxInventarioMovimientos.Add(CrearMovimiento(existencia, userId, metodo, liberar ? "RESERVA_LIBERADA" : "RESERVA", cantidad, dto.Observacion, "RESERVA", existencia.CostoPromedio, dto.Referencia));
        }
        RegistrarAuditoria(context, userId, liberar ? "Liberación de reserva" : "Reserva de inventario", null, new { Liberada = liberar }, new { Modulo = "Inventarios", Sucursal = sucursal.IdSucursal, Productos = detalles.Select(x => new { x.CodigoProducto, x.Cantidad }), Referencia = dto.Referencia, Observacion = dto.Observacion });
        await context.SaveChangesAsync();
        await transaction.CommitAsync();
    }

    public async Task RevalorizarAsync(int userId, EContaxInventarioRevalorizacionDto dto)
    {
        await _seguridad.ExigirAccionAsync(userId, EContaxRoutes.Inventarios, EContaxAccion.Editar);
        await using var context = await _dbFactory.CreateDbContextAsync();
        await using var transaction = await context.Database.BeginTransactionAsync(IsolationLevel.Serializable);
        var actor = await _tenant.GetContextAsync(context, userId);
        ExigirAdminEmpresa(actor);
        var sucursales = await _tenant.GetSucursalesEmpresaAsync(context, actor.IdEmpresa);
        var sucursal = ResolverSucursalUnica(actor, sucursales, dto.IdSucursal);
        if (dto.CostoUnitario < 0) throw new InvalidOperationException("El costo no puede ser negativo.");
        var existencia = await ObtenerExistenciaAsync(context, actor.IdEmpresa, sucursal.IdSucursal, dto.CodigoProducto);
        var costoAnterior = existencia.CostoPromedio;
        existencia.CostoPromedio = Math.Round(dto.CostoUnitario, 4);
        var lotes = await context.EContaxInventarioLotes.Where(x => x.IdEmpresa == actor.IdEmpresa && x.IdSucursal == sucursal.IdSucursal && x.CodigoProducto == dto.CodigoProducto && x.CantidadDisponible > 0).ToListAsync();
        foreach (var lote in lotes) lote.CostoUnitario = existencia.CostoPromedio;
        context.EContaxInventarioMovimientos.Add(CrearMovimiento(existencia, userId, await ObtenerMetodoAsync(context, actor.IdEmpresa), "REVALORIZACION", 0, dto.Observacion, "REVALORIZACION", existencia.CostoPromedio));
        RegistrarAuditoria(context, userId, "Revalorización de inventario", new { Costo = costoAnterior }, new { Costo = existencia.CostoPromedio }, new { Modulo = "Inventarios", Producto = dto.CodigoProducto, Sucursal = sucursal.IdSucursal, Observacion = dto.Observacion });
        await context.SaveChangesAsync();
        await transaction.CommitAsync();
    }

    public async Task AnularEntradaCompraAsync(
        AppDbContext context,
        int userId,
        int idEmpresa,
        int idSucursal,
        IReadOnlyCollection<(int CodigoProducto, decimal Cantidad)> items,
        string? referencia)
    {
        var metodo = await ObtenerMetodoAsync(context, idEmpresa);
        foreach (var grupo in items.Where(x => x.CodigoProducto > 0 && x.Cantidad > 0).GroupBy(x => x.CodigoProducto))
        {
            var producto = await context.Productos.FirstOrDefaultAsync(x =>
                x.Codigo == grupo.Key && x.Idempresa == idEmpresa);
            if (producto is null)
                throw new InvalidOperationException($"No se puede anular la recepción: el producto {grupo.Key} ya no existe en la empresa.");
            if (string.Equals(producto.Tipocompravena, "SERVICIO", StringComparison.OrdinalIgnoreCase))
                continue;

            var existencia = await context.EContaxInventarioExistencias.FirstOrDefaultAsync(x =>
                x.IdEmpresa == idEmpresa && x.IdSucursal == idSucursal && x.CodigoProducto == grupo.Key);
            if (existencia is null)
                throw new InvalidOperationException($"No se puede anular la recepción: no existe inventario para '{producto.Nombre}'.");

            var cantidad = grupo.Sum(x => x.Cantidad) * ObtenerFactorConversion(producto.FactorConversionInventario);
            var costo = await AplicarSalidaAsync(context, existencia, cantidad, metodo);
            context.EContaxInventarioMovimientos.Add(CrearMovimiento(
                existencia, userId, metodo, "ANULACION_RECEPCION", cantidad,
                "Anulación de recepción de compra", "ANULACION_RECEPCION", costo, referencia));
        }
    }

    private static async Task<EContaxInventarioExistencia> ObtenerExistenciaAsync(AppDbContext context, int empresa, int sucursal, int producto)
    {
        var existencia = await context.EContaxInventarioExistencias
            .FromSqlInterpolated($@"
                SELECT [codigo_producto], [id_empresa], [id_sucursal], [stock_actual],
                       [stock_reservado], [stock_minimo], [stock_maximo], [costo_promedio], [fecha_actualizacion]
                FROM [dbo].[ECONTAX_INVENTARIO_EXISTENCIA] WITH (UPDLOCK, HOLDLOCK)
                WHERE [id_empresa] = {empresa}
                  AND [id_sucursal] = {sucursal}
                  AND [codigo_producto] = {producto}")
            .AsTracking()
            .FirstOrDefaultAsync();
        if (existencia is not null)
            return existencia;

        existencia = new EContaxInventarioExistencia { CodigoProducto = producto, IdEmpresa = empresa, IdSucursal = sucursal };
        context.EContaxInventarioExistencias.Add(existencia);
        return existencia;
    }

    private async Task<decimal> AplicarSalidaAsync(
        AppDbContext context,
        EContaxInventarioExistencia existencia,
        decimal cantidad,
        string metodo,
        List<LoteSalida>? lotesSalida = null,
        bool soloVencidos = false)
    {
        var fechaHoy = DateTime.UtcNow.Date;

        var lotesQuery = context.EContaxInventarioLotes
            .FromSqlInterpolated($@"
                SELECT [id_lote], [codigo_producto], [id_empresa], [id_sucursal],
                       [cantidad_original], [cantidad_disponible], [costo_unitario],
                       [numero_lote], [fecha_vencimiento], [fecha_ingreso], [id_movimiento_entrada]
                FROM [dbo].[ECONTAX_INVENTARIO_LOTE] WITH (UPDLOCK, HOLDLOCK)
                WHERE [id_empresa] = {existencia.IdEmpresa}
                  AND [id_sucursal] = {existencia.IdSucursal}
                  AND [codigo_producto] = {existencia.CodigoProducto}")
            .Where(x => x.CantidadDisponible > 0 &&
                        (soloVencidos
                            ? x.FechaVencimiento.HasValue && x.FechaVencimiento.Value.Date <= fechaHoy
                            : !x.FechaVencimiento.HasValue || x.FechaVencimiento.Value.Date > fechaHoy));
        var lotesDisponibles = await lotesQuery.ToListAsync();
        var stockLotesDisponible = lotesDisponibles.Sum(x => x.CantidadDisponible);
        var stockDisponible = Math.Max(0, Math.Min(existencia.StockActual, stockLotesDisponible) - existencia.StockReservado);
        if (cantidad > stockDisponible)
            throw new InvalidOperationException($"No se puede retirar el producto: stock disponible no vencido: {stockDisponible:N2}.");

        var lotes = metodo == "LIFO"
            ? lotesDisponibles.OrderBy(x => x.FechaVencimiento ?? DateTime.MaxValue).ThenByDescending(x => x.FechaIngreso).ThenByDescending(x => x.IdLote).ToList()
            : lotesDisponibles.OrderBy(x => x.FechaVencimiento ?? DateTime.MaxValue).ThenBy(x => x.FechaIngreso).ThenBy(x => x.IdLote).ToList();
        var costoPromedioSalida = existencia.CostoPromedio;
        var costo = metodo == "PROMEDIO" ? costoPromedioSalida : 0m;
        var restante = cantidad;
        foreach (var lote in lotes)
        {
            if (restante <= 0) break;
            var tomado = Math.Min(restante, lote.CantidadDisponible);
            if (metodo != "PROMEDIO") costo += tomado * lote.CostoUnitario;
            lotesSalida?.Add(new LoteSalida(lote.IdLote, tomado, lote.CostoUnitario, lote.NumeroLote, lote.FechaVencimiento));
            lote.CantidadDisponible -= tomado;
            restante -= tomado;
        }
        if (restante > 0)
            throw new InvalidOperationException("El inventario no tiene lotes suficientes para completar la salida.");

        existencia.StockActual = Math.Round(existencia.StockActual - cantidad, 4);
        if (existencia.StockActual <= 0)
        {
            existencia.StockActual = 0;
            existencia.CostoPromedio = 0;
        }
        else if (metodo != "PROMEDIO")
        {
            existencia.CostoPromedio = await CalcularCostoPromedioLotesAsync(context, existencia);
        }
        else
        {
            costo = existencia.CostoPromedio;
        }

        existencia.FechaActualizacion = DateTime.UtcNow;
        return cantidad == 0 ? 0 : metodo == "PROMEDIO" ? costoPromedioSalida : costo / cantidad;
    }

    private static async Task ValidarEntradaConLoteAsync(
        AppDbContext context,
        EContaxInventarioExistencia existencia,
        decimal cantidad,
        string? numeroLote,
        DateTime? fechaVencimiento)
    {
        if (cantidad <= 0)
            return;
        if (numeroLote?.Length > 80)
            throw new InvalidOperationException("El número de lote no puede superar 80 caracteres.");
        if (fechaVencimiento is { } fecha && fecha.Date <= DateTime.UtcNow.Date)
            throw new InvalidOperationException("No se puede ingresar stock vencido o que vence hoy.");

        var requiereLote = await context.EContaxInventarioLotes.AnyAsync(x =>
            x.IdEmpresa == existencia.IdEmpresa && x.IdSucursal == existencia.IdSucursal &&
            x.CodigoProducto == existencia.CodigoProducto && x.CantidadDisponible > 0 &&
            (!string.IsNullOrWhiteSpace(x.NumeroLote) || x.FechaVencimiento.HasValue));
        if (requiereLote && (string.IsNullOrWhiteSpace(numeroLote) || !fechaVencimiento.HasValue))
            throw new InvalidOperationException("Este producto se maneja por lote. Indica el número de lote y la fecha de vencimiento.");
    }

    private static void AgregarEntrada(AppDbContext context, EContaxInventarioExistencia existencia, decimal cantidad, decimal costo, string metodo, string? numeroLote = null, DateTime? fechaVencimiento = null)
    {
        var valorAnterior = existencia.StockActual * existencia.CostoPromedio;
        existencia.StockActual = Math.Round(existencia.StockActual + cantidad, 4);
        existencia.CostoPromedio = existencia.StockActual <= 0
            ? 0
            : Math.Round((valorAnterior + cantidad * costo) / existencia.StockActual, 4);
        existencia.FechaActualizacion = DateTime.UtcNow;
        context.EContaxInventarioLotes.Add(new EContaxInventarioLote
        {
            CodigoProducto = existencia.CodigoProducto,
            IdEmpresa = existencia.IdEmpresa,
            IdSucursal = existencia.IdSucursal,
            CantidadOriginal = cantidad,
            CantidadDisponible = cantidad,
            CostoUnitario = costo,
            FechaIngreso = DateTime.UtcNow,
            NumeroLote = string.IsNullOrWhiteSpace(numeroLote) ? null : numeroLote.Trim(),
            FechaVencimiento = fechaVencimiento?.Date
        });
    }

    private static async Task ConsolidarLotesPromedioAsync(AppDbContext context, EContaxInventarioExistencia existencia)
    {
        var lotes = await context.EContaxInventarioLotes.Where(x =>
            x.IdEmpresa == existencia.IdEmpresa && x.IdSucursal == existencia.IdSucursal && x.CodigoProducto == existencia.CodigoProducto).ToListAsync();
        context.EContaxInventarioLotes.RemoveRange(lotes);
        if (existencia.StockActual > 0)
            context.EContaxInventarioLotes.Add(new EContaxInventarioLote
            {
                CodigoProducto = existencia.CodigoProducto,
                IdEmpresa = existencia.IdEmpresa,
                IdSucursal = existencia.IdSucursal,
                CantidadOriginal = existencia.StockActual,
                CantidadDisponible = existencia.StockActual,
                CostoUnitario = existencia.CostoPromedio,
                FechaIngreso = DateTime.UtcNow
            });
    }

    private static async Task<decimal> CalcularCostoPromedioLotesAsync(AppDbContext context, EContaxInventarioExistencia existencia)
    {
        var lotes = await context.EContaxInventarioLotes.Where(x =>
            x.IdEmpresa == existencia.IdEmpresa && x.IdSucursal == existencia.IdSucursal &&
            x.CodigoProducto == existencia.CodigoProducto && x.CantidadDisponible > 0).ToListAsync();
        var cantidad = lotes.Sum(x => x.CantidadDisponible);
        return cantidad <= 0 ? 0 : lotes.Sum(x => x.CantidadDisponible * x.CostoUnitario) / cantidad;
    }

    private static EContaxInventarioMovimiento CrearMovimiento(
        EContaxInventarioExistencia existencia,
        int userId,
        string metodo,
        string tipo,
        decimal cantidad,
        string? observacion,
        string referenciaTipo,
        decimal? costoUnitario = null,
        string? referencia = null,
        string? numeroLote = null,
        DateTime? fechaVencimiento = null) => new()
        {
            CodigoProducto = existencia.CodigoProducto,
            IdEmpresa = existencia.IdEmpresa,
            IdSucursal = existencia.IdSucursal,
            TipoMovimiento = tipo,
            Cantidad = cantidad,
            CostoUnitario = costoUnitario ?? existencia.CostoPromedio,
            CostoTotal = Math.Round(cantidad * (costoUnitario ?? existencia.CostoPromedio), 4),
            NumeroLote = numeroLote,
            FechaVencimiento = fechaVencimiento,
            SaldoCantidad = existencia.StockActual,
            SaldoCosto = Math.Round(existencia.StockActual * existencia.CostoPromedio, 4),
            MetodoValoracion = metodo,
            ReferenciaTipo = referenciaTipo,
            Referencia = referencia,
            IdUsuario = userId,
            FechaMovimiento = DateTime.UtcNow,
            Observacion = observacion
        };

    private async Task<Producto?> ObtenerProductoInventariableAsync(AppDbContext context, int empresa, int sucursal, int codigo)
    {
        return await context.Productos.FirstOrDefaultAsync(p => p.Codigo == codigo && p.Idempresa == empresa && p.Estado == true &&
            (p.Tipocompravena == null || p.Tipocompravena.ToUpper() != "SERVICIO") &&
            (p.Idsucursal == sucursal || context.EContaxProductosSucursales.Any(x =>
                x.CodigoProducto == codigo && x.IdEmpresa == empresa && x.IdSucursal == sucursal)));
    }

    private async Task<string> ObtenerMetodoAsync(AppDbContext context, int empresa) =>
        NormalizarMetodo(await context.EContaxInventarioConfiguraciones.AsNoTracking()
            .Where(x => x.IdEmpresa == empresa).Select(x => x.MetodoValoracion).FirstOrDefaultAsync());

    private static string NormalizarMetodo(string? metodo)
    {
        var value = (metodo ?? string.Empty).Trim().ToUpperInvariant();
        return Metodos.Contains(value) ? value : "PROMEDIO";
    }

    private static bool EsEntradaReporte(string tipo) => tipo is
        "ENTRADA" or "TRANSFER_ENTRADA" or "TOMA_FISICA_ENTRADA" or "DEVOLUCION_VENTA";

    private static bool EsSalidaReporte(string tipo) => tipo is
        "SALIDA" or "BAJA_VENCIDO" or "TRANSFER_SALIDA" or "TOMA_FISICA_SALIDA" or "DEVOLUCION_COMPRA" or "DEVOLUCION_VENTA_REVERSA";

    private static void RegistrarAuditoria(AppDbContext context, int userId, string accion, object? valoresPrevios, object? valorNuevo, object detalles)
    {
        context.Auditorias.Add(new Auditoria
        {
            IdUsuario = userId,
            Fecha = DateTime.Now,
            Accion = $"Inventarios: {accion}",
            ValoresPrevios = valoresPrevios is null ? null : JsonSerializer.Serialize(valoresPrevios),
            ValorNuevo = valorNuevo is null ? null : JsonSerializer.Serialize(valorNuevo),
            Detalles = JsonSerializer.Serialize(detalles)
        });
    }

    private static void ExigirAdminEmpresa(EContaxUserContext context)
    {
        if (!context.EsJefeEmpresa)
            throw new InvalidOperationException("Solo el administrador de la empresa puede modificar el inventario.");
    }

    private static string? ObtenerAlerta(decimal stock, decimal minimo, decimal maximo) =>
        minimo > 0 && stock <= minimo ? "MINIMO" : maximo > 0 && stock > maximo ? "MAXIMO" : null;

    private static void ValidarDetallesOperacion(IReadOnlyCollection<EContaxInventarioOperacionDetalleDto> detalles)
    {
        if (detalles.Count == 0 || detalles.Any(x => x.CodigoProducto <= 0 || x.Cantidad <= 0))
            throw new InvalidOperationException("La operación debe contener productos con cantidades mayores que cero.");
    }

    private static string? Normalizar(string? valor) => string.IsNullOrWhiteSpace(valor) ? null : valor.Trim();

    private static decimal ObtenerFactorConversion(decimal factor) => factor <= 0 ? 1m : factor;

    private static List<EContaxSucursalDto> ResolverSucursalesConsulta(
        EContaxUserContext userContext,
        List<EContaxSucursalDto> sucursales,
        int? sucursalId)
    {
        if (!userContext.EsJefeEmpresa)
        {
            var propia = sucursales.FirstOrDefault(x => x.IdSucursal == userContext.IdSucursal);
            return propia is null ? throw new InvalidOperationException("El usuario no tiene una sucursal activa.") : [propia];
        }
        if (!sucursalId.HasValue || sucursalId == 0)
            return sucursales;
        var seleccionada = sucursales.FirstOrDefault(x => x.IdSucursal == (sucursalId ?? userContext.IdSucursal));
        return seleccionada is null
            ? throw new InvalidOperationException("La sucursal seleccionada no es válida.")
            : [seleccionada];
    }

    private static EContaxSucursalDto ResolverSucursalUnica(
        EContaxUserContext userContext,
        List<EContaxSucursalDto> sucursales,
        int? sucursalId)
    {
        if (!userContext.EsJefeEmpresa)
            sucursalId = userContext.IdSucursal;
        var sucursal = sucursales.FirstOrDefault(x => x.IdSucursal == (sucursalId ?? userContext.IdSucursal));
        return sucursal ?? throw new InvalidOperationException("La sucursal seleccionada no es válida.");
    }
}
