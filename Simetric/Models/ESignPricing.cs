namespace Simetric.Models;

/// <summary>
/// Precios públicos de eRúbrica. Los valores retornados son finales, con IVA incluido.
/// </summary>
public static class ESignPricing
{
    public const decimal IvaRate = 0.15m;

    public static decimal ObtenerPrecioFinal(string? vigencia) => (vigencia ?? string.Empty).Trim().ToUpperInvariant() switch
    {
        "7 DIAS" => 11.50m,
        "30 DIAS" => 12.65m,
        "1 ANIO" => 23.00m,
        "2 ANIOS" => 34.50m,
        "3 ANIOS" => 44.85m,
        "4 ANIOS" => 55.20m,
        "5 ANIOS" => 64.40m,
        _ => 0m
    };

    public static decimal ObtenerSubtotal(string? vigencia) =>
        decimal.Round(ObtenerPrecioFinal(vigencia) / (1m + IvaRate), 2, MidpointRounding.AwayFromZero);
}
