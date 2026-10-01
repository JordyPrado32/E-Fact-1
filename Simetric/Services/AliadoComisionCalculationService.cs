namespace Simetric.Services;

internal static class AliadoComisionCalculationService
{
    public static decimal ObtenerBaseNeta(decimal? subtotal, decimal? subtotal0, decimal? subtotal12)
        => subtotal ?? ((subtotal0 ?? 0m) + (subtotal12 ?? 0m));

    public static decimal CalcularValor(decimal baseComisionable, decimal porcentaje, decimal? valorConfigurado = null)
        => valorConfigurado is > 0
            ? valorConfigurado.Value
            : decimal.Round(baseComisionable * porcentaje / 100m, 2, MidpointRounding.AwayFromZero);
}
