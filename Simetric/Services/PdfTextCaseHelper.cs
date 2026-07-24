using System.Text.RegularExpressions;

namespace Simetric.Services;

internal static partial class PdfTextCaseHelper
{
    public static string Formatear(string? valor, string valorVacio = "-")
    {
        if (string.IsNullOrWhiteSpace(valor))
            return valorVacio;

        var texto = valor.Trim();
        if (!texto.Any(char.IsLower))
        {
            var palabras = texto
                .ToLowerInvariant()
                .Split(' ', StringSplitOptions.RemoveEmptyEntries);

            texto = string.Join(
                ' ',
                palabras.Select(p => p.Length == 1
                    ? p.ToUpperInvariant()
                    : char.ToUpperInvariant(p[0]) + p[1..]));
        }

        return SiglasSocietariasRegex().Replace(
            texto,
            coincidencia => coincidencia.Value.ToUpperInvariant());
    }

    [GeneratedRegex(
        @"(?<![\p{L}\p{N}])(?:S\s*\.?\s*A\s*\.?\s*S\.?|S\s*\.?\s*A\.?|C[IÍ]A\.?\s+LTDA\.?|LTDA\.?)(?![\p{L}\p{N}])",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex SiglasSocietariasRegex();
}
