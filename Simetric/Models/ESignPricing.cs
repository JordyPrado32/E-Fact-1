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

    public static decimal ObtenerCostoProveedor(string? vigencia)
    {
        var texto = (vigencia ?? string.Empty).ToUpperInvariant().Replace('Ñ', 'N').Replace('Í', 'I');
        var plazo = System.Text.RegularExpressions.Regex.Match(texto, @"(?<!\d)(\d+)\s*(ANIOS?|ANOS?|DIAS?)\b");
        var unidad = plazo.Groups[2].Value.StartsWith("D", StringComparison.Ordinal) ? "DIAS" : "ANIOS";
        return (plazo.Groups[1].Value, unidad) switch
        {
            ("7", "DIAS") => 5.59m,
            ("30", "DIAS") => 6.39m,
            ("1", "ANIOS") => 12.05m,
            ("2", "ANIOS") => 18.75m,
            ("3", "ANIOS") => 24.72m,
            ("4", "ANIOS") => 30.72m,
            ("5", "ANIOS") => 36.05m,
            _ => throw new InvalidOperationException($"No se puede calcular la comisión de la firma sin una vigencia válida: {vigencia}.")
        };
    }

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
