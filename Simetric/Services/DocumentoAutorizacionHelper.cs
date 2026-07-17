namespace Simetric.Services;

public static class DocumentoAutorizacionHelper
{
    public const string FiltroTodos = "TODOS";
    public const string FiltroAutorizados = "AUTORIZADOS";
    public const string FiltroPendientes = "PENDIENTES";
    public const string FiltroNoAutorizados = "NO_AUTORIZADOS";
    public const string EstadoAutorizado = "AUTORIZADO";
    public const string EstadoPendiente = "PENDIENTE";
    public const string EstadoNoAutorizado = "NO AUTORIZADO";

    public static bool EstaAutorizado(bool? autorizado, string? estadoSri = null)
        => autorizado == true || EsEstadoAutorizado(estadoSri);

    public static bool EstaAutorizado(string? autorizado, string? estadoSri = null)
        => EsBanderaAutorizada(autorizado) || EsEstadoAutorizado(estadoSri);

    public static bool EsBanderaAutorizada(string? autorizado)
    {
        if (string.IsNullOrWhiteSpace(autorizado))
            return false;

        var valor = autorizado.Trim();
        return valor.Equals("true", StringComparison.OrdinalIgnoreCase)
            || valor.Equals("1", StringComparison.OrdinalIgnoreCase)
            || valor.Equals("t", StringComparison.OrdinalIgnoreCase)
            || valor.Equals("s", StringComparison.OrdinalIgnoreCase)
            || valor.Equals("si", StringComparison.OrdinalIgnoreCase)
            || valor.Equals("sí", StringComparison.OrdinalIgnoreCase)
            || valor.Equals("a", StringComparison.OrdinalIgnoreCase)
            || valor.Equals("autorizado", StringComparison.OrdinalIgnoreCase);
    }

    public static bool EsEstadoAutorizado(string? estadoSri)
    {
        if (string.IsNullOrWhiteSpace(estadoSri))
            return false;

        var valor = estadoSri.Trim().ToUpperInvariant();
        if (EsNoAutorizado(valor))
            return false;

        return valor.Contains("AUTORIZ");
    }

    public static bool EsNoAutorizado(string? estadoSri)
    {
        if (string.IsNullOrWhiteSpace(estadoSri))
            return false;

        var valor = estadoSri.Trim().ToUpperInvariant();
        return valor is "0" or "FALSE" or "F" or "N" or "NO"
            || valor.Contains("NO AUTORIZ")
            || valor.Contains("RECHAZ")
            || valor.Contains("DEVUELT")
            || valor.Contains("NEGAD")
            || valor.Contains("ERROR")
            || valor.Contains("ANULAD")
            || valor.Contains("CANCELAD");
    }

    public static string ObtenerEstadoVisual(string? estadoSri, bool estaAutorizado)
    {
        if (estaAutorizado || EsEstadoAutorizado(estadoSri))
            return EstadoAutorizado;

        return EsNoAutorizado(estadoSri)
            ? EstadoNoAutorizado
            : EstadoPendiente;
    }

    public static string ObtenerNumeroAutorizacionVisual(
        string? estadoSri,
        bool estaAutorizado,
        string? numeroAutorizacion)
    {
        var estado = ObtenerEstadoVisual(estadoSri, estaAutorizado);

        if (estado == EstadoAutorizado)
        {
            return string.IsNullOrWhiteSpace(numeroAutorizacion)
                ? "Autorizado sin numero registrado"
                : numeroAutorizacion.Trim();
        }

        return estado == EstadoNoAutorizado
            ? "No disponible"
            : "Pendiente de autorizacion";
    }

    public static bool TieneAlertaReemision(DateTime? fechaReferencia, bool estaAutorizado, string? estadoSri)
    {
        if (estaAutorizado || !EsNoAutorizado(estadoSri) || !fechaReferencia.HasValue)
            return false;

        return fechaReferencia.Value.Date <= DateTime.Today.AddDays(-3);
    }

    public static string ObtenerEtiquetaAcceso(bool estaAutorizado, string? numeroAutorizacion)
        => estaAutorizado && !string.IsNullOrWhiteSpace(numeroAutorizacion)
            ? "Numero de autorizacion"
            : "Clave de acceso";

    public static string ObtenerValorAcceso(bool estaAutorizado, string? numeroAutorizacion, string? claveAcceso)
        => estaAutorizado && !string.IsNullOrWhiteSpace(numeroAutorizacion)
            ? numeroAutorizacion.Trim()
            : (claveAcceso ?? string.Empty).Trim();
}
