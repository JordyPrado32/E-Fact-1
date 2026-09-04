using Microsoft.AspNetCore.Components;

namespace Simetric.Services.EDeclara;

public static class EDeclaraRoutes
{
    public const string ServiceKey = "e-declara";
    public const string Root = "/e-declara";
    public const string Dashboard = "/e-declara";
    public const string Profile = "/e-declara/configuracion/perfil";
    public const string Contribuyentes = "/e-declara/contribuyentes";
    public const string Declaraciones = "/e-declara/declaraciones";
    public const string GastosIngresos = "/e-declara/gastos-ingresos";
    public const string Soporte = "/e-declara/soporte";
    public const string Parametros = "/e-declara/configuracion/parametros";
    public const string Periodos = "/e-declara/configuracion/periodos";
    public const string TiposDeclaracion = "/e-declara/configuracion/tipos-declaracion";
    public const string Roles = "/e-declara/administracion/roles";

    public static string PreserveContext(NavigationManager navigationManager, string route)
    {
        if (string.IsNullOrWhiteSpace(route) ||
            !IsEDeclaraPath(navigationManager, navigationManager.Uri) ||
            route.Contains("contexto=edeclara", StringComparison.OrdinalIgnoreCase))
        {
            return route;
        }

        return $"{route}{(route.Contains('?') ? '&' : '?')}contexto=edeclara";
    }

    public static bool IsEDeclaraPath(NavigationManager navigationManager, string location)
    {
        var relativePath = navigationManager.ToBaseRelativePath(location);
        var separatorIndex = relativePath.IndexOfAny(new[] { '?', '#' });
        var pathOnly = (separatorIndex >= 0 ? relativePath[..separatorIndex] : relativePath).Trim('/');
        var hasEDeclaraContext = relativePath.Contains("contexto=edeclara", StringComparison.OrdinalIgnoreCase);

        return pathOnly.Equals("e-declara", StringComparison.OrdinalIgnoreCase) ||
               pathOnly.StartsWith("e-declara/", StringComparison.OrdinalIgnoreCase) ||
               hasEDeclaraContext;
    }
}
