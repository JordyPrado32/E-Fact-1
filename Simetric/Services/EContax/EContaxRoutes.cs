using Microsoft.AspNetCore.Components;

namespace Simetric.Services.EContax;

public static class EContaxRoutes
{
    public const string ServiceKey = "e-conta";
    public const string Root = "/e-contax";
    public const string Dashboard = "/e-contax";
    public const string DashboardAlias = "/e-contax/dashboard";
    public const string Profile = "/e-contax/perfil";
    public const string Soporte = "/e-contax/soporte";
    public const string FacturacionNueva = "/e-contax/facturacion/nueva";
    public const string Organizacion = "/e-contax/organizacion";
    public const string Clientes = "/e-contax/clientes";
    public const string Emisor = "/e-contax/emisor";
    public const string Productos = "/e-contax/productos";
    public const string ConfiguracionCategorias = "/e-contax/configuracion/categorias";
    public const string ConfiguracionPuntosEmision = "/e-contax/configuracion/puntos-emision";
    public const string AdministracionFormasPago = "/e-contax/administracion/formas-pago";
    public const string AdministracionIdentificaciones = "/e-contax/administracion/identificaciones";
    public const string AdministracionImpuestos = "/e-contax/administracion/impuestos";
    public const string AdministracionRetenciones = "/e-contax/administracion/retenciones";
    public const string AdministracionRoles = "/e-contax/administracion/roles";
    public const string AdministracionAuditoriaSql = "/e-contax/administracion/auditoria-sql";
    public const string AdministracionLogsInicio = "/e-contax/administracion/logs-inicio";

    public static bool IsEContaxPath(NavigationManager navigationManager, string location)
    {
        var relativePath = navigationManager.ToBaseRelativePath(location);
        var separatorIndex = relativePath.IndexOfAny(new[] { '?', '#' });
        var pathOnly = (separatorIndex >= 0 ? relativePath[..separatorIndex] : relativePath).Trim('/');

        return pathOnly.Equals("e-contax", StringComparison.OrdinalIgnoreCase) ||
               pathOnly.StartsWith("e-contax/", StringComparison.OrdinalIgnoreCase);
    }
}
