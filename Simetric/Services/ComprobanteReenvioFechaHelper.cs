using System.Globalization;

namespace Simetric.Services;

internal static class ComprobanteReenvioFechaHelper
{
    public static bool PuedeRenovarFecha(string? estado, string? mensaje = null)
    {
        var texto = $"{estado} {mensaje}".ToUpperInvariant();
        return !texto.Contains("RECIBIDA", StringComparison.Ordinal) &&
               !texto.Contains("EN PROCESAMIENTO", StringComparison.Ordinal) &&
               !texto.Contains("PROCESANDO", StringComparison.Ordinal) &&
               !texto.Contains("POR PROCESAR", StringComparison.Ordinal);
    }

    public static bool DebeActualizar(DateTime? fechaEmision, string? claveAcceso)
    {
        var fechaReferencia = ObtenerFechaClave(claveAcceso) ?? fechaEmision;
        return fechaReferencia.HasValue && fechaReferencia.Value.Date < DateTime.Today;
    }

    public static DateTime? DesplazarFecha(DateTime? fecha, DateTime fechaAnterior)
    {
        if (!fecha.HasValue)
            return null;

        return fecha.Value.AddDays((DateTime.Today - fechaAnterior.Date).Days);
    }

    private static DateTime? ObtenerFechaClave(string? claveAcceso)
    {
        var clave = (claveAcceso ?? string.Empty).Trim();
        return clave.Length >= 8 && DateTime.TryParseExact(
            clave[..8], "ddMMyyyy", CultureInfo.InvariantCulture, DateTimeStyles.None, out var fecha)
                ? fecha
                : null;
    }
}
