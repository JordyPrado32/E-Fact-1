namespace Simetric.Models;

/// <summary>
/// Precios públicos de eRúbrica. Los valores retornados son subtotales, sin IVA.
/// </summary>
public static class ESignPricing
{
    public const decimal IvaRate = 0.15m;

    public static decimal ObtenerPrecioFinal(string? vigencia) => (vigencia ?? string.Empty).Trim().ToUpperInvariant() switch
    {
        "7 DIAS" => 9.00m,
        "30 DIAS" => 12.00m,
        "1 ANIO" => 21.00m,
        "2 ANIOS" => 31.00m,
        "3 ANIOS" => 40.00m,
        "4 ANIOS" => 49.00m,
        "5 ANIOS" => 57.00m,
        _ => 0m
    };

    public static decimal ObtenerSubtotal(string? vigencia) =>
        ObtenerPrecioFinal(vigencia);
}
