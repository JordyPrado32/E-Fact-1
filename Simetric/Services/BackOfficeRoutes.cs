using Microsoft.AspNetCore.Components;

namespace Simetric.Services;

public static class BackOfficeRoutes
{
    public const string ServiceKey = "backoffice";
    public const string Root = "/backoffice";
    public const string Dashboard = "/backoffice";
    public const string Clientes = "/backoffice/clientes";
    public const string Ventas = "/backoffice/ventas";
    public const string Facturacion = "/backoffice/facturacion";
    public const string Suscripciones = "/backoffice/suscripciones";
    public const string Cobros = "/backoffice/cobros";
    public const string Renovaciones = "/backoffice/renovaciones";
    public const string Reportes = "/backoffice/reportes";
    public const string ReporteVendedores = "/backoffice/reportes/vendedores";
    public const string Configuracion = "/backoffice/configuracion";
    public const string Perfil = "/backoffice/perfil";

    public static bool IsBackOfficePath(NavigationManager navigationManager, string location)
    {
        var relativePath = navigationManager.ToBaseRelativePath(location);
        var separatorIndex = relativePath.IndexOfAny(new[] { '?', '#' });
        var pathOnly = (separatorIndex >= 0 ? relativePath[..separatorIndex] : relativePath).Trim('/');

        return pathOnly.Equals("backoffice", StringComparison.OrdinalIgnoreCase) ||
               pathOnly.StartsWith("backoffice/", StringComparison.OrdinalIgnoreCase);
    }
}
