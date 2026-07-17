using Microsoft.AspNetCore.Components;

namespace Simetric.Services.ESign;

public static class ESignRoutes
{
    public const string ServiceKey = "e-sign";
    public const string Root = "/e-sign";
    public const string Dashboard = "/e-sign";
    public const string Profile = "/e-sign/configuracion/perfil";
    public const string Firmas = "/e-sign/firmas";
    public const string Documentos = "/e-sign/documentos";
    public const string Soporte = "/e-sign/soporte";

    public static bool IsESignPath(NavigationManager navigationManager, string location)
    {
        var relativePath = navigationManager.ToBaseRelativePath(location);
        var separatorIndex = relativePath.IndexOfAny(new[] { '?', '#' });
        var pathOnly = (separatorIndex >= 0 ? relativePath[..separatorIndex] : relativePath).Trim('/');

        return pathOnly.Equals("e-sign", StringComparison.OrdinalIgnoreCase) ||
               pathOnly.StartsWith("e-sign/", StringComparison.OrdinalIgnoreCase) ||
               pathOnly.Equals("solicitud", StringComparison.OrdinalIgnoreCase) ||
               pathOnly.StartsWith("solicitud/", StringComparison.OrdinalIgnoreCase);
    }
}
