using System.Globalization;
using System.Text;

namespace Simetric.Services;

public static class TipoClienteClasificacion
{
    public const string EtiquetaNatural = "Persona Natural";
    public const string EtiquetaJuridica = "Persona Jurídica";

    public static bool EsJuridica(string? descripcion)
    {
        var normalizada = Normalizar(descripcion);
        return normalizada.Contains("JURIDICA", StringComparison.Ordinal) ||
               normalizada.Contains("EMPRESA", StringComparison.Ordinal);
    }

    public static bool EsNatural(string? descripcion)
    {
        var normalizada = Normalizar(descripcion);
        return !EsJuridica(normalizada) &&
               (normalizada.Contains("NATURAL", StringComparison.Ordinal) ||
                normalizada is "PERSONA" or "CLIENTE");
    }

    public static string ObtenerEtiqueta(string? descripcion)
    {
        if (EsJuridica(descripcion))
            return EtiquetaJuridica;

        if (EsNatural(descripcion))
            return EtiquetaNatural;

        return string.IsNullOrWhiteSpace(descripcion) ? string.Empty : descripcion.Trim();
    }

    private static string Normalizar(string? descripcion)
    {
        if (string.IsNullOrWhiteSpace(descripcion))
            return string.Empty;

        var descompuesta = descripcion.Trim().Normalize(NormalizationForm.FormD);
        var resultado = new StringBuilder(descompuesta.Length);

        foreach (var caracter in descompuesta)
        {
            if (CharUnicodeInfo.GetUnicodeCategory(caracter) != UnicodeCategory.NonSpacingMark)
                resultado.Append(char.ToUpperInvariant(caracter));
        }

        return resultado.ToString().Normalize(NormalizationForm.FormC);
    }
}
