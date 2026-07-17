using System.Globalization;

namespace Simetric.Services;

public static class TaxRateHelper
{
    public static decimal NormalizePercent(decimal value)
    {
        if (value > 0m && value <= 1m)
        {
            value *= 100m;
        }

        return decimal.Round(value, 0, MidpointRounding.AwayFromZero);
    }

    public static int NormalizePercentInt(decimal value) => (int)NormalizePercent(value);

    public static decimal NormalizeMoney(decimal value) =>
        decimal.Round(value, 2, MidpointRounding.AwayFromZero);

    public static string ResolveSriTaxCode(decimal value)
    {
        var tarifa = NormalizePercentInt(value);
        return tarifa switch
        {
            <= 0 => "0",
            5 => "2",
            8 => "2",
            12 => "2",
            13 => "2",
            15 => "4",
            _ => "4"
        };
    }

    public static decimal ParsePercentOrZero(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return 0m;

        var raw = value.Replace("%", string.Empty).Trim();
        if (decimal.TryParse(raw, NumberStyles.Number, CultureInfo.CurrentCulture, out var parsed) ||
            decimal.TryParse(raw, NumberStyles.Number, CultureInfo.InvariantCulture, out parsed))
        {
            return NormalizePercent(parsed);
        }

        return 0m;
    }
}
