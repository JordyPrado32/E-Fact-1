using Microsoft.EntityFrameworkCore;
using Simetric.Data;
using Simetric.Models;
using Simetric.ViewModels;

namespace Simetric.Services;

public sealed class CuentasPagarService
{
    private readonly IDbContextFactory<AppDbContext> _dbFactory;

    public CuentasPagarService(IDbContextFactory<AppDbContext> dbFactory)
    {
        _dbFactory = dbFactory;
    }

    public async Task<List<CuentaPorPagarVM>> ListarAsync(int idUsuario, bool incluirPagadas = false, int? codEmisor = null)
    {
        await using var context = await _dbFactory.CreateDbContextAsync();
        var compras = await CargarComprasAsync(context, idUsuario, codEmisor);
        var pagos = await CargarPagosAsync(context, compras.Select(x => x.Compra.CodFactura));
        return compras
            .Select(x => CrearCuenta(x, pagos.GetValueOrDefault(x.Compra.CodFactura) ?? new List<CuentaPorPagarPago>()))
            .Where(x => incluirPagadas || x.SaldoPendiente > 0m)
            .ToList();
    }

    public async Task<bool> RegistrarPagoAsync(
        int idUsuario,
        IReadOnlyDictionary<int, decimal> distribucion,
        string formaPago,
        string? referencia = null,
        string? observacion = null,
        int? codEmisor = null)
    {
        var importes = distribucion.Where(x => x.Value > 0m).ToDictionary(x => x.Key, x => decimal.Round(x.Value, 2));
        if (importes.Count == 0)
            return false;

        await using var context = await _dbFactory.CreateDbContextAsync();
        var compras = await context.ComprasFacturas
            .Where(x => importes.Keys.Contains(x.CodFactura) && x.Estado == true &&
                ((x.CodDocumento == "03" && x.TipoDocumento == "LIQ") || (x.CodDocumento == "01" && x.TipoDocumento == "COMPRA")) &&
                (x.TipoPago == "19" || (x.TiempoCredito ?? 0) > 0) &&
                (codEmisor.HasValue ? x.CodEmisor == codEmisor.Value : x.Usuario == idUsuario))
            .ToListAsync();

        var nuevosPagos = new List<(ComprasFactura Compra, decimal Monto)>();
        foreach (var item in importes)
        {
            var compra = compras.FirstOrDefault(x => x.CodFactura == item.Key);
            if (compra == null)
                throw new InvalidOperationException("La cuenta por pagar no está disponible para este usuario.");

            if (item.Value > ResolverSaldo(compra) + 0.01m)
                throw new InvalidOperationException($"El pago de la liquidación {FormatearDocumento(compra)} supera el saldo pendiente.");

            nuevosPagos.Add((compra, item.Value));
        }

        await using var transaction = await context.Database.BeginTransactionAsync();
        foreach (var item in nuevosPagos)
        {
            var saldoNuevo = decimal.Round(ResolverSaldo(item.Compra) - item.Monto, 2);
            item.Compra.ValorApagar = saldoNuevo;
            item.Compra.EstadoPago = saldoNuevo <= 0m ? "PAGADO" : "ABONO";
            item.Compra.FechaCancelado = saldoNuevo <= 0m ? DateOnly.FromDateTime(DateTime.Today) : null;
            context.CuentasPorPagarPagos.Add(new CuentaPorPagarPago
            {
                CodFactura = item.Compra.CodFactura,
                Monto = item.Monto,
                FormaPago = formaPago,
                Referencia = referencia,
                FechaPago = DateTime.Now,
                IdUsuario = idUsuario,
                Observacion = observacion,
                Estado = true
            });
        }

        await context.SaveChangesAsync();
        await transaction.CommitAsync();
        return true;
    }

    public async Task<List<EstadoCuentaProveedorResumenVM>> ObtenerResumenProveedoresAsync(int idUsuario, int? codEmisor = null)
    {
        var cuentas = await ListarAsync(idUsuario, incluirPagadas: true, codEmisor);
        return cuentas
            .GroupBy(x => x.IdentificacionProveedor, StringComparer.OrdinalIgnoreCase)
            .Select(g => new EstadoCuentaProveedorResumenVM
            {
                IdentificacionProveedor = g.Key,
                Proveedor = g.First().Proveedor,
                TotalFacturado = g.Sum(x => x.Total),
                TotalPagado = g.Sum(x => x.TotalPagado),
                SaldoActual = g.Sum(x => x.SaldoPendiente),
                CuentasPendientes = g.Count(x => x.SaldoPendiente > 0m),
                CuentasVencidas = g.Count(x => x.EstaVencida),
                FechaUltimoPago = g.Max(x => x.FechaUltimoPago)
            })
            .OrderByDescending(x => x.SaldoActual)
            .ThenBy(x => x.Proveedor)
            .ToList();
    }

    public async Task<EstadoCuentaProveedorDetalleVM?> ObtenerDetalleProveedorAsync(int idUsuario, string identificacion, int? codEmisor = null)
    {
        var cuentas = (await ListarAsync(idUsuario, incluirPagadas: true, codEmisor))
            .Where(x => string.Equals(x.IdentificacionProveedor, identificacion, StringComparison.OrdinalIgnoreCase))
            .ToList();
        if (cuentas.Count == 0)
            return null;

        await using var context = await _dbFactory.CreateDbContextAsync();
        var ids = cuentas.Select(x => x.IdFactura).ToList();
        var pagos = await context.CuentasPorPagarPagos.AsNoTracking()
            .Where(x => x.Estado && ids.Contains(x.CodFactura))
            .OrderBy(x => x.FechaPago)
            .ToListAsync();
        var documentos = cuentas.ToDictionary(x => x.IdFactura);
        var pagosVm = pagos.Select(x => new CuentaPorPagarPagoVM
        {
            IdPago = x.IdPago,
            IdFactura = x.CodFactura,
            NumeroDocumento = documentos[x.CodFactura].NumeroDocumento,
            FechaPago = x.FechaPago,
            Monto = x.Monto,
            FormaPago = x.FormaPago ?? "",
            Referencia = x.Referencia ?? "",
            Observacion = x.Observacion ?? ""
        }).ToList();

        var movimientos = cuentas.Select(x => new EstadoCuentaMovimientoVM
        {
            Fecha = x.FechaEmision ?? x.FechaVencimiento ?? DateTime.Today,
            Documento = x.NumeroDocumento,
            Concepto = x.NumeroDocumento.StartsWith("MANUAL", StringComparison.OrdinalIgnoreCase)
                ? "Factura de compra manual"
                : "Liquidación de compra",
            Debito = x.Total,
            Tipo = "CARGO"
        }).ToList();

        movimientos.AddRange(pagosVm.Select(x => new EstadoCuentaMovimientoVM
        {
            Fecha = x.FechaPago,
            Documento = x.NumeroDocumento,
            Concepto = string.IsNullOrWhiteSpace(x.Observacion) ? "Pago a proveedor" : x.Observacion,
            Credito = x.Monto,
            Tipo = "PAGO"
        }));

        foreach (var cuenta in cuentas.Where(x => x.TotalPagado > 0m && !pagos.Any(p => p.CodFactura == x.IdFactura)))
        {
            pagosVm.Add(new CuentaPorPagarPagoVM
            {
                IdFactura = cuenta.IdFactura,
                NumeroDocumento = cuenta.NumeroDocumento,
                FechaPago = cuenta.FechaUltimoPago ?? cuenta.FechaVencimiento ?? DateTime.Today,
                Monto = cuenta.TotalPagado,
                FormaPago = "REGISTRO HISTÓRICO",
                Observacion = "Pago registrado previamente"
            });
            movimientos.Add(new EstadoCuentaMovimientoVM
            {
                Fecha = cuenta.FechaUltimoPago ?? cuenta.FechaVencimiento ?? DateTime.Today,
                Documento = cuenta.NumeroDocumento,
                Concepto = "Pago registrado previamente",
                Credito = cuenta.TotalPagado,
                Tipo = "PAGO"
            });
        }

        var saldo = 0m;
        foreach (var movimiento in movimientos.OrderBy(x => x.Fecha).ThenBy(x => x.Tipo == "PAGO" ? 1 : 0))
        {
            saldo += movimiento.Debito - movimiento.Credito;
            movimiento.Saldo = Math.Max(saldo, 0m);
        }

        return new EstadoCuentaProveedorDetalleVM
        {
            IdentificacionProveedor = identificacion,
            Proveedor = cuentas[0].Proveedor,
            SaldoActual = cuentas.Sum(x => x.SaldoPendiente),
            Cuentas = cuentas,
            Pagos = pagosVm.OrderByDescending(x => x.FechaPago).ToList(),
            Movimientos = movimientos.OrderByDescending(x => x.Fecha).ToList()
        };
    }

    public async Task<bool> MarcarPagadaAsync(int idUsuario, int idFactura, int? codEmisor = null)
    {
        var cuenta = (await ListarAsync(idUsuario, incluirPagadas: false, codEmisor))
            .FirstOrDefault(x => x.IdFactura == idFactura);
        return cuenta != null && await RegistrarPagoAsync(
            idUsuario,
            new Dictionary<int, decimal> { [idFactura] = cuenta.SaldoPendiente },
            "PAGO TOTAL",
            observacion: "Pago total registrado desde cuentas por pagar",
            codEmisor: codEmisor);
    }

    private static async Task<List<CuentaPorPagarLookup>> CargarComprasAsync(AppDbContext context, int idUsuario, int? codEmisor)
    {
        var comprasQuery = context.ComprasFacturas
            .AsNoTracking()
            .Where(x => x.Estado == true &&
                ((x.CodDocumento == "03" && x.TipoDocumento == "LIQ") || (x.CodDocumento == "01" && x.TipoDocumento == "COMPRA")) &&
                (x.TipoPago == "19" || (x.TiempoCredito ?? 0) > 0));

        comprasQuery = codEmisor.HasValue
            ? comprasQuery.Where(x => x.CodEmisor == codEmisor.Value)
            : comprasQuery.Where(x => x.Usuario == idUsuario);

        var compras = await (
            from compra in comprasQuery
            join cliente in context.Clientes.AsNoTracking()
                on compra.CodClientes equals cliente.Codcliente into clientes
            from cliente in clientes.DefaultIfEmpty()
            orderby compra.FechaVence, compra.CodFactura
            select new
            {
                Compra = compra,
                Identificacion = cliente == null ? "" : cliente.Numeroidentificacion ?? "",
                NombreCliente = cliente == null ? "" : (cliente.Nombrerazonsocial ?? cliente.Nombrecomercial ??
                    ((cliente.Nombres ?? "") + " " + (cliente.Apellidos ?? "")).Trim())
            }).ToListAsync();

        var identificaciones = compras.Select(x => x.Identificacion).Where(x => !string.IsNullOrWhiteSpace(x)).Distinct().ToList();
        var proveedores = await context.Proveedores.AsNoTracking()
            .Where(x => identificaciones.Contains(x.ruc ?? ""))
            .ToDictionaryAsync(x => x.ruc ?? "", StringComparer.OrdinalIgnoreCase);

        return compras.Select(x =>
        {
            proveedores.TryGetValue(x.Identificacion, out var proveedor);
            return new CuentaPorPagarLookup
            {
                Compra = x.Compra,
                Identificacion = proveedor?.ruc ?? x.Identificacion,
                Nombre = string.IsNullOrWhiteSpace(proveedor?.nombre ?? proveedor?.nombreComercial)
                    ? x.NombreCliente
                    : (proveedor?.nombre ?? proveedor?.nombreComercial ?? "").Trim()
            };
        }).ToList();
    }

    private static async Task<Dictionary<int, List<CuentaPorPagarPago>>> CargarPagosAsync(AppDbContext context, IEnumerable<int> facturaIds)
    {
        var ids = facturaIds.ToList();
        if (ids.Count == 0)
            return new();

        var pagos = await context.CuentasPorPagarPagos.AsNoTracking()
            .Where(x => x.Estado && ids.Contains(x.CodFactura))
            .ToListAsync();
        return pagos.GroupBy(x => x.CodFactura).ToDictionary(x => x.Key, x => x.ToList());
    }

    private static CuentaPorPagarVM CrearCuenta(CuentaPorPagarLookup item, IReadOnlyCollection<CuentaPorPagarPago> pagos)
    {
        var compra = item.Compra;
        var total = compra.ValorTotal ?? 0m;
        var saldo = ResolverSaldo(compra);
        var totalPagado = EsPagado(compra) ? total : Math.Max(total - saldo, 0m);
        var estado = saldo <= 0m || EsPagado(compra) ? "PAGADO" : compra.FechaVence?.Date < DateTime.Today ? "VENCIDO" : "PENDIENTE";

        return new CuentaPorPagarVM
        {
            IdFactura = compra.CodFactura,
            NumeroDocumento = FormatearDocumento(compra),
            Proveedor = string.IsNullOrWhiteSpace(item.Nombre) ? "Proveedor sin nombre" : item.Nombre.Trim(),
            IdentificacionProveedor = item.Identificacion,
            FechaEmision = compra.FchAutorizacion,
            FechaVencimiento = compra.FechaVence,
            Total = total,
            TotalPagado = totalPagado,
            SaldoPendiente = saldo,
            Estado = estado,
            FechaUltimoPago = EsPagado(compra) ? compra.FechaCancelado?.ToDateTime(TimeOnly.MinValue) : pagos.Max(x => (DateTime?)x.FechaPago)
        };
    }

    private static decimal ResolverSaldo(ComprasFactura compra)
    {
        if (EsPagado(compra))
            return 0m;

        return Math.Max(compra.ValorApagar ?? compra.ValorTotal ?? 0m, 0m);
    }

    private static bool EsPagado(ComprasFactura compra) =>
        compra.FechaCancelado.HasValue ||
        (compra.EstadoPago?.Contains("PAG", StringComparison.OrdinalIgnoreCase) ?? false) ||
        (compra.EstadoPago?.Contains("CANCEL", StringComparison.OrdinalIgnoreCase) ?? false);

    private static string FormatearDocumento(ComprasFactura compra)
    {
        var serie = (compra.Serie ?? "").Replace("-", "").Trim();
        var serieFormateada = serie.Length == 6 ? $"{serie[..3]}-{serie[3..]}" : serie;
        return $"{serieFormateada}-{(compra.NumFactura ?? "").PadLeft(9, '0')}";
    }

    private sealed class CuentaPorPagarLookup
    {
        public ComprasFactura Compra { get; init; } = null!;
        public string Identificacion { get; init; } = "";
        public string Nombre { get; init; } = "";
    }
}
