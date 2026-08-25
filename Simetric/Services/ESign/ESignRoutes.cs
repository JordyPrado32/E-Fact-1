using Microsoft.AspNetCore.Components;

namespace Simetric.Services.ESign;

public static class ESignRoutes
{
    public const string ServiceKey = "e-sign";
    public const string Root = "/e-rubrica";
    public const string Dashboard = "/e-rubrica";
    public const string Firmas = "/e-rubrica/mis-firmas";
    public const string Documentos = "/e-rubrica/documentos";
    public const string Soporte = "/e-rubrica/soporte";

    public static bool IsESignPath(NavigationManager navigationManager, string location)
    {
        var relativePath = navigationManager.ToBaseRelativePath(location);
        var separatorIndex = relativePath.IndexOfAny(new[] { '?', '#' });
        var pathOnly = (separatorIndex >= 0 ? relativePath[..separatorIndex] : relativePath).Trim('/');

        return pathOnly.Equals("e-rubrica", StringComparison.OrdinalIgnoreCase) ||
               pathOnly.StartsWith("e-rubrica/", StringComparison.OrdinalIgnoreCase) ||
               pathOnly.Equals("solicitud", StringComparison.OrdinalIgnoreCase) ||
               pathOnly.StartsWith("solicitud/", StringComparison.OrdinalIgnoreCase);
    }
}
