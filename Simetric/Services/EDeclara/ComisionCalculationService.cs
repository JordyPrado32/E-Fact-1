using Simetric.Models.EDeclara;

namespace Simetric.Services.EDeclara;

internal static class ComisionCalculationService
{
    public static ComisionCalculo Calcular(
        ComisionInvolucrado involucrado,
        ComisionConfiguracion configuracion,
        ComisionFacturaDetalle detalle,
        decimal subtotal,
        decimal total)
    {
        if (detalle.Base == BaseCalculoComision.Utilidad)
            throw new InvalidOperationException("La comisión sobre utilidad requiere una base de costos y todavía no puede calcularse en la factura.");

        var baseCalculo = detalle.Base == BaseCalculoComision.Total ? total : subtotal;
        var porcentaje = NormalizarPorcentaje(configuracion.PermitirModificarPorcentaje
            ? detalle.Porcentaje
            : involucrado.PorcentajePredeterminado);
        var valorFijo = configuracion.PermitirModificarPorcentaje
            ? detalle.ValorFijo
            : involucrado.ValorPredeterminado;
        var valorSinRedondear = involucrado.TipoCalculo == TipoCalculoComision.ValorFijo
            ? valorFijo
            : baseCalculo * porcentaje / 100m;
        var valor = configuracion.ReglaRedondeo.Equals("Sin decimales", StringComparison.OrdinalIgnoreCase)
            ? Math.Round(valorSinRedondear, 0, MidpointRounding.AwayFromZero)
            : Math.Round(valorSinRedondear, 2, MidpointRounding.AwayFromZero);
        var sobreLimite = configuracion.LimitePorFactura > 0 && valor > configuracion.LimitePorFactura;
        var estado = configuracion.MomentoPendiente.Equals("Al autorizar", StringComparison.OrdinalIgnoreCase) &&
                     !(sobreLimite && configuracion.RequiereAutorizacionSobreLimite)
            ? ComisionEstado.Pendiente
            : ComisionEstado.Generada;
        var motivo = sobreLimite && configuracion.RequiereAutorizacionSobreLimite
            ? "Supera el límite configurado; requiere autorización. " + detalle.Motivo
            : detalle.Motivo;

        return new ComisionCalculo(baseCalculo, porcentaje, valor, estado, motivo);
    }

    public static decimal NormalizarPorcentaje(decimal valor)
        => valor > 100m && valor <= 10000m ? valor / 1000m : valor;
}

internal readonly record struct ComisionCalculo(
    decimal Base,
    decimal Porcentaje,
    decimal Valor,
    ComisionEstado Estado,
    string? Motivo);
