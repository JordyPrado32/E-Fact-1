using Microsoft.EntityFrameworkCore;
using Simetric.Data;
using Simetric.Models;
using System.Data;

namespace Simetric.Services;

public sealed class AliadoComisionGenerationService
{
    private readonly IDbContextFactory<AppDbContext> _dbFactory;

    public AliadoComisionGenerationService(IDbContextFactory<AppDbContext> dbFactory)
    {
        _dbFactory = dbFactory;
    }

    public async Task SincronizarPortalAsync()
    {
        await using var db = await _dbFactory.CreateDbContextAsync();
        var vendedorIds = await db.VendedoresBackOffice.AsNoTracking()
            .Where(x => !x.EsSistema && x.Activo && db.Usuarios.Any(u =>
                u.IdVendedor == x.IdVendedor && u.Estado == true &&
                db.AliadoPortalUsuariosRoles.Any(ur => ur.IdUsuario == u.IdUsuario &&
                    db.AliadoPortalRoles.Any(r => r.IdRol == ur.IdRol && r.Nombre == AliadoPortalService.RoleName))))
            .Select(x => x.IdVendedor)
            .ToListAsync();

        foreach (var idVendedor in vendedorIds)
            await SincronizarAsync(idVendedor);
    }

    public async Task SincronizarAsync(int idVendedor)
    {
        if (idVendedor <= 0)
            return;

        await using var strategyContext = await _dbFactory.CreateDbContextAsync();
        var strategy = strategyContext.Database.CreateExecutionStrategy();
        await strategy.ExecuteAsync(async () =>
        {
        await using var db = await _dbFactory.CreateDbContextAsync();
        var executionStrategy = db.Database.CreateExecutionStrategy();
        await executionStrategy.ExecuteAsync(() => SincronizarEnTransaccionAsync(db, idVendedor));
    }

    private static async Task SincronizarEnTransaccionAsync(AppDbContext db, int idVendedor)
    {
        await using var transaction = await db.Database.BeginTransactionAsync(IsolationLevel.Serializable);
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
                        (x.Estado == true || x.Estado == null))
            .Select(x => new FacturaComisionRow
            {
                IdFactura = x.Codfactura,
                IdCliente = x.Codclientes,
                FechaVencimiento = x.Fechavence,
                Subtotal = x.Subtotal,
                Subtotal0 = x.Subtotal0,
                Subtotal12 = x.Subtotal12,
                Fecha = x.Fchautorizacion ?? x.Fechaentrega ?? DateTime.Now,
                Autorizado = x.Autorizado == true,
                EstadoPago = x.Estadopago
            })
            .ToListAsync();
        var existentes = await db.AliadoComisiones.AsNoTracking()
            .Where(x => x.IdVendedor == idVendedor)
            .Select(x => new { x.IdFactura, x.TipoComision })
            .ToHashSetAsync();
        var facturasPorId = facturas.ToDictionary(x => x.IdFactura);
        var ventasPorPeriodo = facturas
            .GroupBy(x => x.Fecha.ToString("yyyy-MM"))
            .ToDictionary(x => x.Key, x => x.Sum(y => AliadoComisionCalculationService.ObtenerBaseNeta(y.Subtotal, y.Subtotal0, y.Subtotal12)));
        var periodos = facturas.Select(x => x.Fecha.ToString("yyyy-MM")).Distinct().ToArray();
        var parametrosPorPeriodo = await db.AliadoComisionParametros.AsNoTracking()
            .Where(x => x.IdVendedor == idVendedor && periodos.Contains(x.Periodo))
            .ToDictionaryAsync(x => x.Periodo);
        var comisionesPendientes = await db.AliadoComisiones
            .Where(x => x.IdVendedor == idVendedor && (x.Estado == "Generada" || x.Estado == "Pendiente"))
            .ToListAsync();
        foreach (var comision in comisionesPendientes)
        {
            if (facturasPorId.TryGetValue(comision.IdFactura, out var factura) && factura.Autorizado && EsPagoConfirmado(factura.EstadoPago))
            {
                AliadoComisionStateMachine.Require(comision.Estado, AliadoComisionEstado.Aprobada);
                comision.Estado = AliadoComisionEstado.Aprobada.ToString();
                comision.FechaAprobacion = DateTime.Now;
            }
            else
            {
                AliadoComisionStateMachine.Require(comision.Estado, AliadoComisionEstado.Pendiente);
                comision.Estado = AliadoComisionEstado.Pendiente.ToString();
            }
        }
        if (comisionesPendientes.Count > 0)
            await db.SaveChangesAsync();

        var gestiones = await db.AliadoRenovacionGestiones.AsNoTracking()
            .Where(x => x.IdVendedor == idVendedor)
            .GroupBy(x => x.IdFactura)
            .Select(x => x.OrderByDescending(y => y.FechaGestion).First())
            .ToDictionaryAsync(x => x.IdFactura);
        var nuevas = new List<AliadoComision>();

        foreach (var factura in facturas)
        {
            var baseComisionable = AliadoComisionCalculationService.ObtenerBaseNeta(factura.Subtotal, factura.Subtotal0, factura.Subtotal12);
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

            var periodo = factura.Fecha.ToString("yyyy-MM");
            parametrosPorPeriodo.TryGetValue(periodo, out var parametro);
            var ventasPeriodo = ventasPorPeriodo.GetValueOrDefault(periodo);
            var porcentajePredeterminado = ventasPeriodo >= 1000m ? 10m : 5m;
            var porcentajeParametro = parametro is null ? (decimal?)null : ventasPeriodo >= 1000m
                ? (parametro.PorcentajeDesdeMil > 0m ? parametro.PorcentajeDesdeMil : parametro.Porcentaje)
                : (parametro.PorcentajeHastaMil > 0m ? parametro.PorcentajeHastaMil : parametro.Porcentaje);
            var porcentaje = porcentajeParametro ?? (tipo switch
            {
                "RenovacionNumerica" => configuracion.PorcentajeRenovacionNumerica,
                "RenovacionAliado" => aliado.PorcentajeBase > 0m
                    ? aliado.PorcentajeBase
                    : configuracion.PorcentajeRenovacionAliado,
                _ => aliado.PorcentajeBase > 0m
                    ? aliado.PorcentajeBase
                    : porcentajePredeterminado
            });
            nuevas.Add(new AliadoComision
            {
                IdVendedor = idVendedor,
                IdFactura = factura.IdFactura,
                IdCliente = factura.IdCliente,
                TipoComision = tipo,
                BaseComisionable = baseComisionable,
                Porcentaje = porcentaje,
                Valor = AliadoComisionCalculationService.CalcularValor(baseComisionable, porcentaje),
                Periodo = periodo,
                Estado = factura.Autorizado && EsPagoConfirmado(factura.EstadoPago) ? "Aprobada" : "Pendiente",
                FechaAprobacion = factura.Autorizado && EsPagoConfirmado(factura.EstadoPago) ? DateTime.Now : null,
                FechaGeneracion = factura.Fecha
            });
        }

        if (nuevas.Count > 0)
        {
            db.AliadoComisiones.AddRange(nuevas);
            await db.SaveChangesAsync();
        }
        await transaction.CommitAsync();
        });
    }

    public async Task CerrarPeriodosAsync()
    {
        await using var strategyContext = await _dbFactory.CreateDbContextAsync();
        var strategy = strategyContext.Database.CreateExecutionStrategy();
        await strategy.ExecuteAsync(async () =>
        {
        await using var db = await _dbFactory.CreateDbContextAsync();
        var executionStrategy = db.Database.CreateExecutionStrategy();
        await executionStrategy.ExecuteAsync(() => CerrarPeriodosEnTransaccionAsync(db));
    }

    private static async Task CerrarPeriodosEnTransaccionAsync(AppDbContext db)
    {
        await using var transaction = await db.Database.BeginTransactionAsync(IsolationLevel.Serializable);
        var inicioPeriodoActual = new DateTime(DateTime.Today.Year, DateTime.Today.Month, 1);
        var pendientes = await db.AliadoComisiones
            .Where(x => x.Estado == "Aprobada" && x.IdLiquidacion == null && x.FechaGeneracion < inicioPeriodoActual)
            .ToListAsync();

        foreach (var grupo in pendientes.GroupBy(x => new { x.IdVendedor, x.IdCliente, Periodo = x.FechaGeneracion.ToString("yyyy-MM") }))
        {
            var liquidacion = await db.AliadoLiquidaciones
                .SingleOrDefaultAsync(x => x.IdVendedor == grupo.Key.IdVendedor &&
                                           x.IdCliente == grupo.Key.IdCliente &&
                                           x.Periodo == grupo.Key.Periodo &&
                                           x.Estado == "Pendiente");
            if (liquidacion is null)
            {
                liquidacion = new AliadoLiquidacion
                {
                    IdVendedor = grupo.Key.IdVendedor,
                    IdCliente = grupo.Key.IdCliente,
                    Periodo = grupo.Key.Periodo,
                    Total = 0,
                    Fecha = DateTime.Now,
                    Estado = "Pendiente"
                };
                db.AliadoLiquidaciones.Add(liquidacion);
            }

            liquidacion.Total += grupo.Sum(x => x.Valor);
            await db.SaveChangesAsync();
            foreach (var comision in grupo)
                comision.IdLiquidacion = liquidacion.IdLiquidacion;
        }

        if (pendientes.Count > 0)
            await db.SaveChangesAsync();
        await transaction.CommitAsync();
        });
    }

    private static bool EsPagoConfirmado(string? estadoPago)
        => estadoPago?.Trim().ToUpperInvariant() is "PAGADA" or "PAGADO" or "CANCELADA" or "CANCELADO" or "COBRADA" or "COBRADO";
}
