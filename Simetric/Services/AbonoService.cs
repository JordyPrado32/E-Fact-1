using Microsoft.EntityFrameworkCore;
using Simetric.Data;
using Simetric.Models;
using Simetric.ViewModels;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Simetric.Services
{
    public class AbonoService
    {
        private readonly IDbContextFactory<AppDbContext> _dbFactory;

        public AbonoService(IDbContextFactory<AppDbContext> dbFactory)
        {
            _dbFactory = dbFactory;
        }

        /// <summary>
        /// Obtiene las facturas a crédito (TipoPago = 19) que aún tienen saldo pendiente.
        /// </summary>
        public async Task<List<FacturaPendienteVM>> GetFacturasCreditoPendientes(int idUsuario, int? idCliente = null)
        {
            await using var context = await _dbFactory.CreateDbContextAsync();
            return await GetFacturasCreditoPendientesCoreAsync(context, idUsuario, idCliente);
        }

        /// <summary>
        /// Obtiene el saldo a favor disponible de un cliente (AbonoMultiples con saldo > 0 y estado activo).
        /// </summary>
        public async Task<decimal> GetSaldoAFavor(int idUsuario, int idCliente)
        {
            await using var context = await _dbFactory.CreateDbContextAsync();

            if (!await ClientePerteneceUsuarioAsync(context, idUsuario, idCliente))
                return 0m;

            return await context.AbonoMultiples
                .AsNoTracking()
                .Where(am => am.idCliente == idCliente && am.estado == true && am.saldo > 0)
                .SumAsync(am => (decimal?)am.saldo) ?? 0m;
        }

        /// <summary>
        /// Obtiene el detalle de cada AbonoMultiple que tiene saldo a favor disponible.
        /// </summary>
        public async Task<List<SaldoAFavorVM>> GetDetalleSaldoAFavor(int idUsuario, int idCliente)
        {
            await using var context = await _dbFactory.CreateDbContextAsync();

            if (!await ClientePerteneceUsuarioAsync(context, idUsuario, idCliente))
                return new List<SaldoAFavorVM>();

            return await context.AbonoMultiples
                .AsNoTracking()
                .Where(am => am.idCliente == idCliente && am.estado == true && am.saldo > 0)
                .OrderByDescending(am => am.fechaPago)
                .Select(am => new SaldoAFavorVM
                {
                    IdAbonoMultiple = am.sec,
                    FechaPago = am.fechaPago ?? DateTime.MinValue,
                    ValorOriginal = am.valor ?? 0m,
                    SaldoDisponible = am.saldo ?? 0m,
                    Observacion = am.observacion
                })
                .ToListAsync();
        }

        /// <summary>
        /// Obtiene el historial de facturas completamente saldadas del cliente,
        /// ordenadas por la fecha del último abono (más reciente primero).
        /// </summary>
        public async Task<List<FacturaHistorialVM>> GetHistorialFacturasSaldadas(int idUsuario, int idCliente)
        {
            await using var context = await _dbFactory.CreateDbContextAsync();

            var facturas = await context.Facturas
                .AsNoTracking()
                .Where(f => f.Tipopago == "19"
                         && f.Idusuario == idUsuario
                         && f.Codclientes == idCliente
                         && (f.Estado == true || f.Estado == null))
                .Select(f => new
                {
                    f.Codfactura,
                    f.Numfactura,
                    TotalFactura = f.Valortotal ?? 0,
                    TotalAbonado = context.Abonos
                        .Where(a => a.codFactura == f.Codfactura && a.estado == true)
                        .Sum(a => (decimal?)a.abono) ?? 0,
                    UltimoAbono = context.Abonos
                        .Where(a => a.codFactura == f.Codfactura && a.estado == true)
                        .Max(a => (DateTime?)a.fechaPago)
                })
                .ToListAsync();

            return facturas
                .Where(f => f.TotalFactura > 0 && f.TotalAbonado >= f.TotalFactura)
                .OrderByDescending(f => f.UltimoAbono)
                .Select(f => new FacturaHistorialVM
                {
                    IdFactura = f.Codfactura,
                    NumFactura = f.Numfactura,
                    TotalFactura = f.TotalFactura,
                    TotalAbonado = f.TotalAbonado,
                    FechaSaldada = f.UltimoAbono ?? DateTime.MinValue
                })
                .ToList();
        }

        public async Task<List<EstadoCuentaClienteResumenVM>> GetEstadoCuentaClientesAsync(int idUsuario)
        {
            await using var context = await _dbFactory.CreateDbContextAsync();

            var facturas = await BuildFacturasCreditoBaseQuery(context, idUsuario)
                .Select(f => new
                {
                    f.Codfactura,
                    NumeroFactura = f.Numfactura ?? string.Empty,
                    IdCliente = f.Codclientes ?? 0,
                    NombreCliente = f.CodclientesNavigation != null
                        ? (f.CodclientesNavigation.Nombrerazonsocial
                            ?? f.CodclientesNavigation.Nombrecomercial
                            ?? ((f.CodclientesNavigation.Nombres ?? string.Empty) + " " + (f.CodclientesNavigation.Apellidos ?? string.Empty))).Trim()
                        : string.Empty,
                    Identificacion = f.CodclientesNavigation != null ? (f.CodclientesNavigation.Numeroidentificacion ?? string.Empty) : string.Empty,
                    FechaEmision = f.Fchautorizacion ?? f.Fechaentrega,
                    FechaVencimiento = f.Fechavence
                        ?? ((f.Fchautorizacion ?? f.Fechaentrega).HasValue
                            ? (f.CodclientesNavigation != null && f.CodclientesNavigation.DiasCredito.HasValue
                                ? (f.Fchautorizacion ?? f.Fechaentrega)!.Value.AddDays(f.CodclientesNavigation.DiasCredito.Value)
                                : (DateTime?)null)
                            : null),
                    ValorFacturado = f.Valortotal ?? 0m,
                    TotalAbonos = context.Abonos
                        .Where(a => a.codFactura == f.Codfactura && a.estado == true)
                        .Sum(a => (decimal?)a.abono) ?? 0m
                })
                .ToListAsync();

            var pagosCliente = await context.Abonos
                .AsNoTracking()
                .Where(a => a.estado == true && a.idCliente != null)
                .GroupBy(a => a.idCliente!.Value)
                .Select(g => new
                {
                    IdCliente = g.Key,
                    FechaUltimoAbono = g.Max(x => x.fechaPago),
                    MontoUltimoAbono = g.OrderByDescending(x => x.fechaPago ?? DateTime.MinValue)
                        .ThenByDescending(x => x.sec)
                        .Select(x => x.abono ?? 0m)
                        .FirstOrDefault()
                })
                .ToDictionaryAsync(x => x.IdCliente);

            var resumenCliente = facturas
                .GroupBy(f => f.IdCliente)
                .ToDictionary(
                    g => g.Key,
                    g => new
                    {
                        FacturasPendientes = g.Count(x => x.ValorFacturado - x.TotalAbonos > 0),
                        SaldoTotal = g.Sum(x => Math.Max(x.ValorFacturado - x.TotalAbonos, 0m)),
                        DiasVencidosMaximos = g
                            .Where(x => x.FechaVencimiento.HasValue && x.ValorFacturado - x.TotalAbonos > 0)
                            .Select(x => Math.Max((DateTime.Today - x.FechaVencimiento!.Value.Date).Days, 0))
                            .DefaultIfEmpty(0)
                            .Max()
                    });

            return facturas
                .Select(f =>
                {
                    var resumen = resumenCliente.GetValueOrDefault(f.IdCliente);
                    var pago = pagosCliente.GetValueOrDefault(f.IdCliente);
                    var saldoActual = Math.Max(f.ValorFacturado - f.TotalAbonos, 0m);

                    return new EstadoCuentaClienteResumenVM
                    {
                        IdCliente = f.IdCliente,
                        NombreCliente = f.NombreCliente,
                        NumeroIdentificacion = f.Identificacion,
                        IdFactura = f.Codfactura,
                        NumeroFactura = f.NumeroFactura,
                        ValorFacturado = f.ValorFacturado,
                        TotalAbonos = f.TotalAbonos,
                        SaldoActual = saldoActual,
                        FechaEmision = f.FechaEmision,
                        FechaVencimiento = f.FechaVencimiento,
                        FacturasPendientes = resumen?.FacturasPendientes ?? 0,
                        SaldoTotalCliente = resumen?.SaldoTotal ?? 0m,
                        MontoUltimoAbono = pago?.MontoUltimoAbono ?? 0m,
                        FechaUltimoAbono = pago?.FechaUltimoAbono,
                        DiasVencidosMaximos = resumen?.DiasVencidosMaximos ?? 0
                    };
                })
                .OrderByDescending(x => x.SaldoTotalCliente > 0)
                .ThenByDescending(x => x.DiasVencidosMaximos)
                .ThenBy(x => x.NombreCliente)
                .ThenByDescending(x => x.FechaEmision ?? DateTime.MinValue)
                .ThenByDescending(x => x.IdFactura)
                .ToList();
        }

        public async Task<EstadoCuentaDetalleVM?> GetEstadoCuentaDetalleAsync(int idUsuario, int idCliente)
        {
            await using var context = await _dbFactory.CreateDbContextAsync();

            if (!await ClientePerteneceUsuarioAsync(context, idUsuario, idCliente))
                return null;

            var facturas = await BuildFacturasCreditoBaseQuery(context, idUsuario)
                .Where(f => f.Codclientes == idCliente)
                .Select(f => new EstadoCuentaFacturaVM
                {
                    IdFactura = f.Codfactura,
                    NumeroFactura = f.Numfactura ?? string.Empty,
                    FechaEmision = f.Fchautorizacion ?? f.Fechaentrega,
                    FechaVencimiento = f.Fechavence
                        ?? ((f.Fchautorizacion ?? f.Fechaentrega).HasValue
                            ? (f.CodclientesNavigation != null && f.CodclientesNavigation.DiasCredito.HasValue
                                ? (f.Fchautorizacion ?? f.Fechaentrega)!.Value.AddDays(f.CodclientesNavigation.DiasCredito.Value)
                                : (DateTime?)null)
                            : null),
                    ValorFacturado = f.Valortotal ?? 0m,
                    TotalAbonos = context.Abonos
                        .Where(a => a.codFactura == f.Codfactura && a.estado == true)
                        .Sum(a => (decimal?)a.abono) ?? 0m
                })
                .ToListAsync();

            foreach (var factura in facturas)
            {
                factura.SaldoActual = Math.Max(factura.ValorFacturado - factura.TotalAbonos, 0m);
                factura.DiasVencidos = factura.SaldoActual <= 0 || !factura.FechaVencimiento.HasValue
                    ? 0
                    : Math.Max((DateTime.Today - factura.FechaVencimiento.Value.Date).Days, 0);
                factura.Estado = ResolverEstadoFactura(factura.SaldoActual, factura.FechaVencimiento);
            }

            var facturaLookup = facturas.ToDictionary(f => f.IdFactura);

            var abonosRaw = await context.Abonos
                .AsNoTracking()
                .Where(a => a.estado == true && a.idCliente == idCliente)
                .OrderByDescending(a => a.fechaPago)
                .ThenByDescending(a => a.sec)
                .Select(a => new
                {
                    a.sec,
                    a.idAbonoMultiple,
                    a.codFactura,
                    a.fechaPago,
                    Monto = a.abono ?? 0m,
                    a.observacion,
                    a.formaPago
                })
                .ToListAsync();

            var abonos = abonosRaw
                .Select(a => new EstadoCuentaAbonoVM
                {
                    IdAbono = a.sec,
                    IdAbonoMultiple = a.idAbonoMultiple,
                    IdFactura = a.codFactura,
                    NumeroFactura = a.codFactura.HasValue && facturaLookup.TryGetValue(a.codFactura.Value, out var factura)
                        ? factura.NumeroFactura
                        : string.Empty,
                    FechaPago = a.fechaPago,
                    Monto = a.Monto,
                    Concepto = string.IsNullOrWhiteSpace(a.observacion) ? "Abono" : a.observacion!,
                    FormaPago = a.formaPago ?? string.Empty,
                    Observacion = a.observacion ?? string.Empty
                })
                .ToList();

            var cliente = await context.Clientes
                .AsNoTracking()
                .FirstOrDefaultAsync(c => c.Codcliente == idCliente && c.Usuario == idUsuario);

            var saldoAFavor = await context.AbonoMultiples
                .AsNoTracking()
                .Where(am => am.idCliente == idCliente && am.estado == true && am.saldo > 0)
                .SumAsync(am => (decimal?)am.saldo) ?? 0m;

            var movimientos = BuildEstadoCuentaMovimientos(facturas, abonos);
            var ultimoAbono = abonos.FirstOrDefault();
            var saldoTotal = facturas.Sum(f => f.SaldoActual);
            var facturasPendientes = facturas.Count(f => f.SaldoActual > 0);

            return new EstadoCuentaDetalleVM
            {
                IdCliente = idCliente,
                NombreCliente = ObtenerNombreCliente(cliente),
                NumeroIdentificacion = cliente?.Numeroidentificacion ?? string.Empty,
                Correo = cliente?.Correo ?? string.Empty,
                SaldoTotal = saldoTotal,
                FacturasPendientes = facturasPendientes,
                MontoUltimoAbono = ultimoAbono?.Monto ?? 0m,
                FechaUltimoAbono = ultimoAbono?.FechaPago,
                DiasVencidosMaximos = facturas
                    .Where(f => f.SaldoActual > 0)
                    .Select(f => f.DiasVencidos)
                    .DefaultIfEmpty(0)
                    .Max(),
                SaldoAFavorDisponible = saldoAFavor,
                Facturas = facturas
                    .OrderByDescending(f => f.FechaEmision ?? DateTime.MinValue)
                    .ThenByDescending(f => f.IdFactura)
                    .ToList(),
                Abonos = abonos,
                Movimientos = movimientos
            };
        }

        /// <summary>
        /// Registra un pago y distribuye el monto entre las facturas pendientes del cliente.
        /// Si sobra saldo, queda registrado en AbonoMultiples para uso posterior.
        /// </summary>
        public async Task<bool> RegistrarPagoGeneral(int idUsuario, int idCliente, decimal montoRecibido, string observacion)
        {
            await using var strategyContext = await _dbFactory.CreateDbContextAsync();
            var strategy = strategyContext.Database.CreateExecutionStrategy();

            return await strategy.ExecuteAsync(async () =>
            {
                await using var context = await _dbFactory.CreateDbContextAsync();

                if (!await ClientePerteneceUsuarioAsync(context, idUsuario, idCliente))
                    return false;

                await using var transaction = await context.Database.BeginTransactionAsync();
                try
                {
                    var maestro = new AbonoMultiple
                    {
                        idCliente = idCliente,
                        valor = montoRecibido,
                        fechaPago = DateTime.Now,
                        fechaTrs = DateTime.Now,
                        observacion = observacion,
                        estado = true,
                        saldo = montoRecibido,
                        saldoUtilizado = 0
                    };

                    context.AbonoMultiples.Add(maestro);
                    await context.SaveChangesAsync();

                    var pendientes = await GetFacturasCreditoPendientesCoreAsync(context, idUsuario, idCliente);
                    decimal saldoPorRepartir = montoRecibido;

                    foreach (var fac in pendientes)
                    {
                        if (saldoPorRepartir <= 0) break;

                        decimal abonoParaEstaFactura = (saldoPorRepartir >= fac.SaldoPendiente)
                                                       ? fac.SaldoPendiente
                                                       : saldoPorRepartir;

                        var detalle = new Abonos
                        {
                            codFactura = fac.IdFactura,
                            abono = abonoParaEstaFactura,
                            idCliente = idCliente,
                            idAbonoMultiple = maestro.sec,
                            fechaPago = DateTime.Now,
                            fechaTrs = DateTime.Now,
                            estado = true,
                            observacion = "Abono automático"
                        };

                        context.Abonos.Add(detalle);
                        saldoPorRepartir -= abonoParaEstaFactura;
                        maestro.saldoUtilizado += abonoParaEstaFactura;
                        maestro.saldo = montoRecibido - maestro.saldoUtilizado;
                    }

                    await context.SaveChangesAsync();
                    await transaction.CommitAsync();
                    return true;
                }
                catch (Exception)
                {
                    await transaction.RollbackAsync();
                    return false;
                }
            });
        }

        /// <summary>
        /// Registra abonos manuales con distribución libre por factura.
        /// Opcionalmente consume saldo a favor previo del cliente.
        /// El saldo sobrante (montoRecibido - totalDistribuido) queda disponible para el próximo pago.
        /// </summary>
        public async Task<bool> RegistrarPagoManual(
            int idUsuario,
            int idCliente,
            decimal montoRecibido,
            Dictionary<int, decimal> distribucion,
            string observacion,
            bool usarSaldoAFavor = false)
        {
            await using var strategyContext = await _dbFactory.CreateDbContextAsync();
            var strategy = strategyContext.Database.CreateExecutionStrategy();

            return await strategy.ExecuteAsync(async () =>
            {
                await using var context = await _dbFactory.CreateDbContextAsync();

                if (!await ClientePerteneceUsuarioAsync(context, idUsuario, idCliente))
                    return false;

                await using var transaction = await context.Database.BeginTransactionAsync();
                try
                {
                    decimal totalDistribuido = distribucion.Values.Sum();
                    decimal montoTotal = montoRecibido;

                    // Si se decide usar el saldo a favor, consumirlo primero
                    if (usarSaldoAFavor)
                    {
                        var saldosAFavor = await context.AbonoMultiples
                            .Where(am => am.idCliente == idCliente && am.estado == true && am.saldo > 0)
                            .OrderBy(am => am.fechaPago)  // FIFO: consumir el más antiguo primero
                            .ToListAsync();

                        decimal necesario = Math.Max(0, totalDistribuido - montoRecibido);

                        foreach (var saf in saldosAFavor)
                        {
                            if (necesario <= 0) break;
                            decimal consumir = Math.Min(saf.saldo ?? 0m, necesario);
                            saf.saldo = (saf.saldo ?? 0m) - consumir;
                            saf.saldoUtilizado = (saf.saldoUtilizado ?? 0m) + consumir;
                            montoTotal += consumir;
                            necesario -= consumir;
                        }
                    }

                    var maestro = new AbonoMultiple
                    {
                        idCliente = idCliente,
                        valor = montoTotal,
                        fechaPago = DateTime.Now,
                        fechaTrs = DateTime.Now,
                        observacion = string.IsNullOrWhiteSpace(observacion) ? "Abono manual" : observacion,
                        estado = true,
                        saldoUtilizado = totalDistribuido,
                        // El saldo sobrante queda disponible para el próximo pago
                        saldo = montoTotal - totalDistribuido
                    };

                    context.AbonoMultiples.Add(maestro);
                    await context.SaveChangesAsync();

                    var facturasValidas = await context.Facturas
                        .Where(f =>
                            f.Idusuario == idUsuario &&
                            f.Codclientes == idCliente &&
                            distribucion.Keys.Contains(f.Codfactura))
                        .Select(f => f.Codfactura)
                        .ToListAsync();

                    foreach (var (codFactura, monto) in distribucion.Where(x => facturasValidas.Contains(x.Key)))
                    {
                        if (monto <= 0) continue;

                        var detalle = new Abonos
                        {
                            codFactura = codFactura,
                            abono = monto,
                            idCliente = idCliente,
                            idAbonoMultiple = maestro.sec,
                            fechaPago = DateTime.Now,
                            fechaTrs = DateTime.Now,
                            estado = true,
                            observacion = string.IsNullOrWhiteSpace(observacion) ? "Abono manual" : observacion
                        };
                        context.Abonos.Add(detalle);
                    }

                    await context.SaveChangesAsync();
                    await transaction.CommitAsync();
                    return true;
                }
                catch
                {
                    await transaction.RollbackAsync();
                    return false;
                }
            });
        }

        // Búsqueda mejorada por Cédula o Nombre
        public async Task<List<FacturaPendienteVM>> GetFacturasCreditoPendientes(int idUsuario, string filtro)
        {
            await using var context = await _dbFactory.CreateDbContextAsync();
            return await GetFacturasCreditoPendientesCoreAsync(context, idUsuario, filtro);
        }

        private static async Task<List<FacturaPendienteVM>> GetFacturasCreditoPendientesCoreAsync(AppDbContext context, int idUsuario, int? idCliente = null)
        {
            var query = BuildFacturasCreditoBaseQuery(context, idUsuario);

            if (idCliente.HasValue)
            {
                query = query.Where(f => f.Codclientes == idCliente);
            }

            var facturas = await query.Select(f => new FacturaPendienteVM
            {
                IdFactura = f.Codfactura,
                NumFactura = f.Numfactura,
                IdCliente = f.Codclientes ?? 0,
                NumeroIdentificacion = f.CodclientesNavigation != null ? (f.CodclientesNavigation.Numeroidentificacion ?? string.Empty) : string.Empty,
                NombreCliente = f.CodclientesNavigation != null
                                ? (f.CodclientesNavigation.Nombrerazonsocial ?? f.CodclientesNavigation.Nombrecomercial ?? ((f.CodclientesNavigation.Nombres ?? "") + " " + (f.CodclientesNavigation.Apellidos ?? ""))).Trim()
                                : string.Empty,
                FechaEmision = f.Fchautorizacion ?? f.Fechaentrega,
                FechaVencimiento = f.Fechavence
                    ?? ((f.Fchautorizacion ?? f.Fechaentrega).HasValue
                        ? (f.CodclientesNavigation != null && f.CodclientesNavigation.DiasCredito.HasValue
                            ? (f.Fchautorizacion ?? f.Fechaentrega)!.Value.AddDays(f.CodclientesNavigation.DiasCredito.Value)
                            : (DateTime?)null)
                        : null),
                TotalFactura = f.Valortotal ?? 0,
                TotalAbonado = context.Abonos
                    .Where(a => a.codFactura == f.Codfactura && a.estado == true)
                    .Sum(a => (decimal?)a.abono) ?? 0
            }).ToListAsync();

            return facturas
                .Where(x => x.SaldoPendiente > 0)
                .OrderBy(ObtenerFechaUrgencia)
                .ThenBy(x => x.NombreCliente)
                .ThenBy(x => x.NumFactura)
                .ToList();
        }

        private static async Task<List<FacturaPendienteVM>> GetFacturasCreditoPendientesCoreAsync(AppDbContext context, int idUsuario, string filtro)
        {
            if (string.IsNullOrWhiteSpace(filtro))
                return new List<FacturaPendienteVM>();

            filtro = filtro.Trim();

            var facturas = await BuildFacturasCreditoBaseQuery(context, idUsuario)
                .Select(f => new FacturaPendienteVM
            {
                IdFactura = f.Codfactura,
                NumFactura = f.Numfactura,
                IdCliente = f.Codclientes ?? 0,
                NumeroIdentificacion = f.CodclientesNavigation != null ? (f.CodclientesNavigation.Numeroidentificacion ?? string.Empty) : string.Empty,
                NombreCliente = f.CodclientesNavigation != null
                                ? (f.CodclientesNavigation.Nombrerazonsocial ?? f.CodclientesNavigation.Nombrecomercial ?? ((f.CodclientesNavigation.Nombres ?? "") + " " + (f.CodclientesNavigation.Apellidos ?? ""))).Trim()
                                : string.Empty,
                FechaEmision = f.Fchautorizacion ?? f.Fechaentrega,
                FechaVencimiento = f.Fechavence
                    ?? ((f.Fchautorizacion ?? f.Fechaentrega).HasValue
                        ? (f.CodclientesNavigation != null && f.CodclientesNavigation.DiasCredito.HasValue
                            ? (f.Fchautorizacion ?? f.Fechaentrega)!.Value.AddDays(f.CodclientesNavigation.DiasCredito.Value)
                            : (DateTime?)null)
                        : null),
                TotalFactura = f.Valortotal ?? 0,
                TotalAbonado = context.Abonos
                    .Where(a => a.codFactura == f.Codfactura && a.estado == true)
                    .Sum(a => (decimal?)a.abono) ?? 0
            }).ToListAsync();

            return facturas
                .Where(x => x.SaldoPendiente > 0)
                .Where(x => MatchesSearch(x, filtro))
                .GroupBy(x => x.IdCliente)
                .Select(g => g
                    .OrderBy(x => GetSearchRank(x, filtro))
                    .ThenBy(ObtenerFechaUrgencia)
                    .ThenBy(x => x.NombreCliente)
                    .ThenBy(x => x.NumFactura)
                    .First())
                .OrderBy(x => GetSearchRank(x, filtro))
                .ThenBy(ObtenerFechaUrgencia)
                .ThenBy(x => x.NombreCliente)
                .ThenBy(x => x.NumFactura)
                .ToList();
        }

        private static DateTime ObtenerFechaUrgencia(FacturaPendienteVM factura) =>
            factura.FechaVencimiento?.Date
            ?? factura.FechaEmision?.Date
            ?? DateTime.MaxValue.Date;

        private static int GetSearchRank(FacturaPendienteVM factura, string filtro)
        {
            var term = NormalizeSearchText(filtro);
            if (string.IsNullOrEmpty(term))
                return 99;

            var identificacion = NormalizeSearchText(factura.NumeroIdentificacion);
            var nombre = NormalizeSearchText(factura.NombreCliente);
            var tokens = term.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

            if (identificacion == term || nombre == term)
                return 0;

            if (identificacion.StartsWith(term) || nombre.StartsWith(term))
                return 1;

            if (identificacion.Contains(term) || nombre.Contains(term))
                return 2;

            if (tokens.Length > 0 && tokens.All(token =>
                    identificacion.Contains(token, StringComparison.Ordinal) ||
                    nombre.Contains(token, StringComparison.Ordinal)))
            {
                return 3;
            }

            return 4;
        }

        private static IQueryable<Factura> BuildFacturasCreditoBaseQuery(AppDbContext context, int idUsuario) =>
            context.Facturas
                .AsNoTracking()
                .Where(f =>
                    f.Idusuario == idUsuario &&
                    f.Tipopago == "19" &&
                    (f.Estado == true || f.Estado == null));

        private static async Task<bool> ClientePerteneceUsuarioAsync(AppDbContext context, int idUsuario, int idCliente)
        {
            if (idUsuario <= 0 || idCliente <= 0)
                return false;

            return await context.Clientes
                .AsNoTracking()
                .AnyAsync(c => c.Codcliente == idCliente && c.Usuario == idUsuario);
        }

        private static bool MatchesSearch(FacturaPendienteVM factura, string filtro)
        {
            var term = NormalizeSearchText(filtro);
            if (string.IsNullOrWhiteSpace(term))
                return false;

            var searchableValues = new[]
            {
                NormalizeSearchText(factura.NombreCliente),
                NormalizeSearchText(factura.NumeroIdentificacion)
            };

            if (searchableValues.Any(value =>
                    value == term ||
                    value.Contains(term, StringComparison.Ordinal) ||
                    term.Contains(value, StringComparison.Ordinal)))
            {
                return true;
            }

            var tokens = term.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            if (tokens.Length == 0)
                return false;

            return searchableValues.Any(value => tokens.All(token => value.Contains(token, StringComparison.Ordinal)));
        }

        private static string NormalizeSearchText(string? value)
        {
            if (string.IsNullOrWhiteSpace(value))
                return string.Empty;

            var normalized = value.Trim().Normalize(NormalizationForm.FormD);
            var builder = new StringBuilder(normalized.Length);

            foreach (var character in normalized)
            {
                var category = CharUnicodeInfo.GetUnicodeCategory(character);
                if (category == UnicodeCategory.NonSpacingMark)
                    continue;

                builder.Append(char.IsLetterOrDigit(character) ? char.ToUpperInvariant(character) : ' ');
            }

            return string.Join(" ",
                builder.ToString()
                    .Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries));
        }

        private static string ObtenerNombreCliente(Cliente? cliente)
        {
            if (cliente is null)
                return string.Empty;

            return (cliente.Nombrerazonsocial
                    ?? cliente.Nombrecomercial
                    ?? $"{cliente.Nombres ?? string.Empty} {cliente.Apellidos ?? string.Empty}")
                .Trim();
        }

        private static string ResolverEstadoFactura(decimal saldoActual, DateTime? fechaVencimiento)
        {
            if (saldoActual <= 0)
                return "Pagado";

            if (fechaVencimiento.HasValue && fechaVencimiento.Value.Date < DateTime.Today)
                return "Vencido";

            return "Pendiente";
        }

        private static List<EstadoCuentaMovimientoVM> BuildEstadoCuentaMovimientos(
            IEnumerable<EstadoCuentaFacturaVM> facturas,
            IEnumerable<EstadoCuentaAbonoVM> abonos)
        {
            var baseMovimientos = facturas
                .Select(f => new EstadoCuentaMovimientoVM
                {
                    Fecha = (f.FechaEmision ?? f.FechaVencimiento ?? DateTime.Today).Date,
                    Documento = f.NumeroFactura,
                    Concepto = "Factura",
                    Debito = f.ValorFacturado,
                    Credito = 0m,
                    Tipo = "FACTURA"
                })
                .Concat(abonos.Select(a => new EstadoCuentaMovimientoVM
                {
                    Fecha = (a.FechaPago ?? DateTime.Today).Date,
                    Documento = string.IsNullOrWhiteSpace(a.NumeroFactura) ? $"ABO-{a.IdAbono:000000}" : a.NumeroFactura,
                    Concepto = string.IsNullOrWhiteSpace(a.Concepto) ? "Abono" : a.Concepto,
                    Debito = 0m,
                    Credito = a.Monto,
                    Tipo = "ABONO"
                }))
                .OrderBy(m => m.Fecha)
                .ThenBy(m => m.Tipo == "FACTURA" ? 0 : 1)
                .ThenBy(m => m.Documento)
                .ToList();

            decimal saldo = 0m;
            foreach (var movimiento in baseMovimientos)
            {
                saldo += movimiento.Debito;
                saldo -= movimiento.Credito;
                movimiento.Saldo = saldo;
            }

            return baseMovimientos
                .OrderByDescending(m => m.Fecha)
                .ThenByDescending(m => m.Tipo == "ABONO" ? 1 : 0)
                .ThenByDescending(m => m.Documento)
                .ToList();
        }
    }
}
