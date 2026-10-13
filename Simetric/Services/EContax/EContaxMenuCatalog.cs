using Microsoft.EntityFrameworkCore;
using Simetric.Data;
using Simetric.Models.EContax;

namespace Simetric.Services.EContax;

public sealed record EContaxMenuDefinition(
    string Key,
    string Nombre,
    string? Url,
    string Icono,
    int Orden,
    string? PadreKey = null);

public static class EContaxMenuCatalog
{
    public static IReadOnlyList<EContaxMenuDefinition> Todos { get; } =
    [
        new("inicio", "Inicio", EContaxRoutes.Dashboard, "ri-home-4-line", 1),
        new("clientes", "Clientes / Proveedores", EContaxRoutes.Clientes, "ri-group-line", 2),
        new("facturas", "Facturas", null, "ri-file-text-line", 3),
        new("factura-nueva", "Nueva Factura", EContaxRoutes.FacturacionNueva, "ri-file-add-line", 1, "facturas"),
        new("factura-documentos", "Mis Facturas", EContaxRoutes.FacturacionDocumentos, "ri-file-list-3-line", 2, "facturas"),
        new("otros-documentos", "Emision de otros Documentos", null, "ri-file-copy-2-line", 4),
        new("guia-remision", "Guia de Remisión", EContaxRoutes.FacturacionGuiaRemision, "ri-truck-line", 1, "otros-documentos"),
        new("liquidacion-compra", "Liquidación de Compra", EContaxRoutes.ComprasNuevaLiquidacion, "ri-file-add-line", 2, "otros-documentos"),
        new("nota-credito", "Nota de Credito", EContaxRoutes.FacturacionNotaCredito, "ri-file-reduce-line", 3, "otros-documentos"),
        new("nota-debito", "Nota de Debito", EContaxRoutes.FacturacionNotaDebito, "ri-file-add-line", 4, "otros-documentos"),
        new("cuentas-cobrar", "Cuentas Por Cobrar", EContaxRoutes.CuentasPorCobrar, "ri-wallet-3-line", 5),
        new("estado-cuenta", "Estado de cuenta", EContaxRoutes.CuentasPorCobrarEstadoCuenta, "ri-file-list-3-line", 1, "cuentas-cobrar"),
        new("productos", "Productos", EContaxRoutes.Productos, "ri-box-3-line", 6),
        new("categorias", "Categorias", EContaxRoutes.ConfiguracionCategorias, "ri-shopping-cart-line", 1, "productos"),
        new("cotizaciones", "Cotizaciones", EContaxRoutes.Cotizaciones, "ri-file-paper-2-line", 2, "productos"),
        new("reportes-documentos", "Reporte documentos", EContaxRoutes.ReportesDocumentos, "ri-file-chart-line", 8),
        new("administracion", "Administracion", null, "ri-admin-line", 9),
        new("roles", "Roles y permisos", EContaxRoutes.AdministracionRoles, "ri-shield-keyhole-line", 1, "administracion"),
        new("cajas", "Cajas y secuencias", EContaxRoutes.AdministracionCajasSecuencias, "ri-layout-grid-line", 2, "administracion"),
        new("impuestos", "Impuestos", EContaxRoutes.AdministracionImpuestos, "ri-percent-line", 3, "administracion"),
        new("usuarios", "Usuarios", EContaxRoutes.AdministracionUsuarios, "ri-user-line", 4, "administracion"),
        new("identificaciones", "Identificaciones", EContaxRoutes.AdministracionIdentificaciones, "ri-id-card-line", 5, "administracion"),
        new("formas-pago", "Formas de Pago", EContaxRoutes.AdministracionFormasPago, "ri-bank-card-line", 6, "administracion"),
        new("logs-inicio", "Logs de Inicio", EContaxRoutes.AdministracionLogsInicio, "ri-history-line", 7, "administracion"),
        new("retenciones", "Retenciones", EContaxRoutes.AdministracionRetenciones, "ri-inbox-archive-line", 8, "administracion"),
        new("auditoria-sql", "SQL Auditoria", EContaxRoutes.AdministracionAuditoriaSql, "ri-shield-check-line", 9, "administracion"),
        new("documentos-generados", "Documentos Generados", null, "ri-folder-history-line", 10),
        new("notas-credito-generadas", "Mis Notas de Crédito", EContaxRoutes.FacturacionNotasCreditoGeneradas, "ri-file-list-2-line", 1, "documentos-generados"),
        new("retenciones-generadas", "Mis Retenciones", EContaxRoutes.FacturacionRetencionesGeneradas, "ri-file-list-2-line", 2, "documentos-generados"),
        new("guias-generadas", "Mis Guias de Remisión", EContaxRoutes.FacturacionGuiasRemisionGeneradas, "ri-file-list-2-line", 3, "documentos-generados"),
        new("liquidaciones-generadas", "Mis Liquidaciones Compras", EContaxRoutes.ComprasLiquidacionesGeneradas, "ri-file-list-2-line", 4, "documentos-generados"),
        new("notas-debito-generadas", "Mis Notas de Debito", EContaxRoutes.FacturacionNotasDebitoGeneradas, "ri-file-list-2-line", 5, "documentos-generados"),
        new("configuracion", "Configuración", null, "ri-settings-3-line", 11),
        new("emisor", "Emisor", EContaxRoutes.Emisor, "ri-building-2-line", 1, "configuracion"),
        new("perfil", "Mi Perfil", EContaxRoutes.Profile, "ri-user-settings-line", 2, "configuracion"),
        new("puntos-emision", "Pto. Emision", EContaxRoutes.ConfiguracionPuntosEmision, "ri-safe-2-line", 3, "configuracion"),
        new("centro-normativo", "Centro normativo", EContaxRoutes.ConfiguracionCentroNormativo, "ri-scales-3-line", 4, "configuracion"),
        new("firma", "Firma", EContaxRoutes.ConfiguracionFirma, "ri-shield-keyhole-line", 5, "configuracion"),
        new("tutoriales", "Tutoriales", EContaxRoutes.Tutoriales, "ri-question-line", 12),
        new("soporte", "Soporte", EContaxRoutes.Soporte, "ri-customer-service-2-line", 13)
    ];

    public static async Task EnsureAsync(AppDbContext context)
    {
        var tablaExiste = await context.Database
            .SqlQuery<int>($"SELECT CASE WHEN OBJECT_ID(N'dbo.menu', N'U') IS NULL THEN 0 ELSE 1 END AS [Value]")
            .FirstOrDefaultAsync();

        if (tablaExiste != 1)
        {
            return;
        }

        var menus = await context.EContaxMenus.ToListAsync();
        var siguienteId = (menus.Select(item => (int?)item.IdMenu).Max() ?? 0) + 1;
        var idsPorClave = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);

        foreach (var definicion in Todos)
        {
            var idPadre = definicion.PadreKey is null
                ? (int?)null
                : idsPorClave.GetValueOrDefault(definicion.PadreKey);

            var menu = menus.FirstOrDefault(item =>
                (definicion.Url is not null && string.Equals(item.UrlMenu, definicion.Url, StringComparison.OrdinalIgnoreCase)) ||
                (definicion.Url is null &&
                 string.Equals(item.NombreMenu?.Trim(), definicion.Nombre, StringComparison.OrdinalIgnoreCase) &&
                 (item.IdPadre ?? 0) == (idPadre ?? 0)));

            if (menu is null)
            {
                menu = new EContaxMenu
                {
                    IdMenu = siguienteId++,
                    NombreMenu = definicion.Nombre,
                    UrlMenu = definicion.Url,
                    IconoMenu = definicion.Icono,
                    EstadoMenu = 1,
                    IdPadre = idPadre,
                    OrdenMenu = definicion.Orden
                };
                menus.Add(menu);
                context.EContaxMenus.Add(menu);
            }
            else
            {
                menu.NombreMenu = definicion.Nombre;
                menu.UrlMenu = definicion.Url;
                menu.IconoMenu = definicion.Icono;
                menu.EstadoMenu = 1;
                menu.IdPadre = idPadre;
                menu.OrdenMenu = definicion.Orden;
            }

            idsPorClave[definicion.Key] = menu.IdMenu;
        }

        await context.SaveChangesAsync();
    }
}
