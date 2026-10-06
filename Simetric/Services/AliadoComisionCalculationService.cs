namespace Simetric.Services;

internal static class AliadoComisionCalculationService
{
    public static bool PuedeRecalcular(string estado, string tipo, int? liquidacionId, DateTime? fechaPago) =>
        liquidacionId is null && fechaPago is null && estado is "Generada" or "Pendiente" or "Aprobada" &&
        !tipo.StartsWith("Reversion-", StringComparison.OrdinalIgnoreCase);

    public static decimal ObtenerBaseNeta(decimal? subtotal, decimal? subtotal0, decimal? subtotal12)
        => subtotal ?? ((subtotal0 ?? 0m) + (subtotal12 ?? 0m));

    public static decimal ObtenerBaseComisionable(decimal ventaSinIva, IEnumerable<AliadoComisionDetalle> firmas)
    {
        var costo = firmas.Sum(x => x.Cantidad < 0m
            ? throw new InvalidOperationException("La cantidad de firmas no puede ser negativa.")
            : Simetric.Models.ESignPricing.ObtenerCostoProveedor(x.Producto) * x.Cantidad);
        return Math.Max(ventaSinIva - costo, 0m);
    }

    public static decimal CalcularValor(decimal baseComisionable, decimal porcentaje, decimal? valorConfigurado = null)
        => valorConfigurado is > 0
            ? valorConfigurado.Value
            : decimal.Round(baseComisionable * porcentaje / 100m, 2, MidpointRounding.AwayFromZero);
}

internal sealed record AliadoComisionDetalle(string? Producto, decimal Cantidad);
