namespace Simetric.Services;

public static class DocumentoEstadoFiltroHelper
{
    public const string Todos = "TODOS";
    public const string Pendientes = "PENDIENTES";
    public const string Autorizados = "AUTORIZADOS";
    public const string NoAutorizados = "NO_AUTORIZADOS";

    public static bool Cumple(string? filtro, string? estadoVisual)
    {
        if (string.IsNullOrWhiteSpace(filtro) || filtro == Todos)
            return true;

        var estado = (estadoVisual ?? string.Empty).Trim();
        return filtro switch
        {
            Pendientes => estado.Equals(DocumentoAutorizacionHelper.EstadoPendiente, StringComparison.OrdinalIgnoreCase),
            Autorizados => estado.Equals(DocumentoAutorizacionHelper.EstadoAutorizado, StringComparison.OrdinalIgnoreCase),
            NoAutorizados => estado.Equals(DocumentoAutorizacionHelper.EstadoNoAutorizado, StringComparison.OrdinalIgnoreCase),
            _ => true
        };
    }
}
