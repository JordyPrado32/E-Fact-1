using System.Globalization;
using System.Text;
using Simetric.Models;

namespace Simetric.Services;

public static class BackOfficeReportDataFilter
{
    private static readonly string[] TestNamePatterns =
    [
        "TERAN ARELLANO MARTHA",
        "PRUEBA ANONIMA",
        "FRANKLIN",
        "JORDY"
    ];

    public static bool IsTestData(string? value)
    {
        var normalized = Normalize(value);
        return !string.IsNullOrWhiteSpace(normalized)
            && TestNamePatterns.Any(pattern => normalized.Contains(pattern, StringComparison.OrdinalIgnoreCase));
    }

    public static bool IsTestData(Cliente? cliente)
    {
        return cliente is not null
            && (IsTestData(cliente.Nombrerazonsocial)
                || IsTestData(cliente.Nombrecomercial)
                || IsTestData($"{cliente.Nombres} {cliente.Apellidos}")
                || IsTestData(cliente.Correo));
    }

    public static bool IsTestData(Usuario? usuario)
    {
        return usuario is not null
            && (IsTestData($"{usuario.Nombres} {usuario.Apellidos}")
                || IsTestData(usuario.NombreEmpresa)
                || IsTestData(usuario.Email));
    }

    public static bool IsTestData(Factura? factura)
    {
        return factura is not null
            && (IsTestData(factura.CodclientesNavigation)
                || IsTestData(factura.Nombread)
                || IsTestData(factura.Correoad));
    }

    public static bool IsTestData(ReporteVentaBackOffice? venta)
    {
        return venta is not null
            && (IsTestData(venta.Cliente)
                || IsTestData(venta.Vendedor));
    }

    private static string Normalize(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return string.Empty;

        var normalized = value.Normalize(NormalizationForm.FormD);
        var builder = new StringBuilder(normalized.Length);

        foreach (var ch in normalized)
        {
            if (CharUnicodeInfo.GetUnicodeCategory(ch) != UnicodeCategory.NonSpacingMark)
            {
                builder.Append(char.ToUpperInvariant(ch));
            }
        }

        return builder.ToString().Normalize(NormalizationForm.FormC);
    }
}
