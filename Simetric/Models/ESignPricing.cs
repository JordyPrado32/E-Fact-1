namespace Simetric.Models;

/// <summary>
/// Precios públicos de eRúbrica. El precio comercial se conserva como total y el precio
/// base se usa en el formulario de compra; el pago y la factura calculan el IVA aparte.
/// </summary>
public static class ESignPricing
{
    public const decimal IvaRate = 0.15m;

    public static decimal ObtenerPrecioFinal(string? vigencia) =>
        decimal.Round(ObtenerPrecioBase(vigencia) * (1m + IvaRate), 2, MidpointRounding.AwayFromZero);

    public static decimal ObtenerSubtotal(string? vigencia) => ObtenerPrecioBase(vigencia);

    public static decimal ObtenerPrecioBase(string? vigencia) => (vigencia ?? string.Empty).Trim().ToUpperInvariant() switch
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

}
