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
    public const string FacturacionDocumentos = "/e-contax/facturacion/documentos";
    public const string FacturacionNotaCredito = "/e-contax/facturacion/nota-credito";
    public const string FacturacionNotasCreditoGeneradas = "/e-contax/facturacion/notas-credito-generadas";
    public const string FacturacionNotaDebito = "/e-contax/facturacion/nota-debito";
    public const string FacturacionNotasDebitoGeneradas = "/e-contax/facturacion/notas-debito-generadas";
    public const string FacturacionGuiaRemision = "/e-contax/facturacion/guia-remision";
    public const string FacturacionGuiasRemisionGeneradas = "/e-contax/facturacion/guias-remision-generadas";
    public const string FacturacionRetencionesGeneradas = "/e-contax/facturacion/retenciones-generadas";
    public const string Cotizaciones = "/e-contax/cotizaciones";
    public const string ComprasImportarXml = "/e-contax/compras/importar-xml";
    public const string ComprasNuevaLiquidacion = "/e-contax/compras/nueva-liquidacion";
    public const string ComprasLiquidacionesGeneradas = "/e-contax/compras/liquidaciones-generadas";
    public const string CuentasPorCobrar = "/e-contax/cuentas-por-cobrar";
    public const string CuentasPorCobrarEstadoCuenta = "/e-contax/cuentas-por-cobrar/estado-cuenta";
    public const string ReportesDocumentos = "/e-contax/reportes/documentos";
    public const string ReportesLogs = "/e-contax/reportes/logs";
    public const string CompraDocumentos = "/e-contax/compra-documentos";
    public const string HistorialCompras = "/e-contax/historial-compras";
    public const string Proveedores = "/e-contax/proveedores";
    public const string Parametrizacion = "/e-contax/parametrizacion";
    public const string SolicitudNueva = "/e-contax/solicitud/nueva";
    public const string SolicitudPagos = "/e-contax/solicitud/pagos";
    public const string SolicitudPagoResultado = "/e-contax/solicitud/pago/resultado";
    public const string Suscripciones = "/e-contax/suscripciones";
    public const string Organizacion = "/e-contax/organizacion";
    public const string Clientes = "/e-contax/clientes";
    public const string Emisor = "/e-contax/emisor";
    public const string Productos = "/e-contax/productos";
    public const string ConfiguracionCategorias = "/e-contax/configuracion/categorias";
    public const string ConfiguracionPuntosEmision = "/e-contax/configuracion/puntos-emision";
    public const string ConfiguracionTiposCliente = "/e-contax/configuracion/tipos-cliente";
    public const string ConfiguracionSeguridad = "/e-contax/configuracion/seguridad";
    public const string ConfiguracionFirma = "/e-contax/configuracion/firma";
    public const string ConfiguracionCentroNormativo = "/e-contax/configuracion/centro-normativo";
    public const string ConfiguracionGeneral = "/e-contax/configuracion/general";
    public const string Tutoriales = "/e-contax/ayuda/tutoriales";
    public const string AdministracionFormasPago = "/e-contax/administracion/formas-pago";
    public const string AdministracionIdentificaciones = "/e-contax/administracion/identificaciones";
    public const string AdministracionImpuestos = "/e-contax/administracion/impuestos";
    public const string AdministracionRetenciones = "/e-contax/administracion/retenciones";
    public const string AdministracionRoles = "/e-contax/administracion/roles";
    public const string AdministracionUsuarios = "/e-contax/administracion/usuarios";
    public const string AdministracionCajasSecuencias = "/e-contax/administracion/cajas-secuencias";
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
