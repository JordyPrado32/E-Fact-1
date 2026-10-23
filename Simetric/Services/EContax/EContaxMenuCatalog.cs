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
        new("proveedores", "Proveedores", EContaxRoutes.Proveedores, "ri-truck-line", 3),
        new("facturas", "Facturas", null, "ri-file-text-line", 4),
        new("factura-nueva", "Nueva Factura", EContaxRoutes.FacturacionNueva, "ri-file-add-line", 1, "facturas"),
        new("factura-documentos", "Mis Facturas", EContaxRoutes.FacturacionDocumentos, "ri-file-list-3-line", 2, "facturas"),
        new("compras", "Compras", null, "ri-shopping-bag-3-line", 5),
        new("ordenes-compra", "Órdenes de compra", EContaxRoutes.ComprasOrdenes, "ri-file-list-3-line", 1, "compras"),
        new("recepciones-compra", "Recepciones de compra", EContaxRoutes.ComprasRecepciones, "ri-inbox-archive-line", 2, "compras"),
        new("otros-documentos", "Emision de otros Documentos", null, "ri-file-copy-2-line", 4),
        new("guia-remision", "Guia de Remisión", EContaxRoutes.FacturacionGuiaRemision, "ri-truck-line", 1, "otros-documentos"),
        new("liquidacion-compra", "Liquidación de Compra", EContaxRoutes.ComprasNuevaLiquidacion, "ri-file-add-line", 2, "otros-documentos"),
        new("nota-credito", "Nota de Credito", EContaxRoutes.FacturacionNotaCredito, "ri-file-reduce-line", 3, "otros-documentos"),
        new("nota-debito", "Nota de Debito", EContaxRoutes.FacturacionNotaDebito, "ri-file-add-line", 4, "otros-documentos"),
        new("cuentas-cobrar", "Cuentas Por Cobrar", EContaxRoutes.CuentasPorCobrar, "ri-wallet-3-line", 6),
        new("estado-cuenta", "Estado de cuenta", EContaxRoutes.CuentasPorCobrarEstadoCuenta, "ri-file-list-3-line", 1, "cuentas-cobrar"),
        new("cuentas-pagar", "Cuentas Por Pagar", EContaxRoutes.CuentasPorPagar, "ri-bank-card-line", 7),
        new("cuentas-pagar-mis-compras", "Mis compras", EContaxRoutes.CuentasPorPagarMisCompras, "ri-shopping-bag-3-line", 1, "cuentas-pagar"),
        new("cuentas-pagar-nueva-factura", "Nueva factura de compra", EContaxRoutes.CuentasPorPagarNuevaFactura, "ri-file-add-line", 2, "cuentas-pagar"),
        new("cuentas-pagar-registrar", "Registrar pago", EContaxRoutes.CuentasPorPagarRegistrar, "ri-hand-coin-line", 3, "cuentas-pagar"),
        new("cuentas-pagar-estado-cuenta", "Estado de cuenta", EContaxRoutes.CuentasPorPagarEstadoCuenta, "ri-file-list-3-line", 4, "cuentas-pagar"),
        new("productos", "Productos", EContaxRoutes.Productos, "ri-box-3-line", 7),
        new("categorias", "Categorias", EContaxRoutes.ConfiguracionCategorias, "ri-shopping-cart-line", 1, "productos"),
        new("cotizaciones", "Cotizaciones", EContaxRoutes.Cotizaciones, "ri-file-paper-2-line", 2, "productos"),
        new("inventarios", "Inventarios", EContaxRoutes.Inventarios, "ri-archive-stack-line", 8),
        new("inventarios-kardex", "Kardex", EContaxRoutes.InventariosKardex, "ri-file-list-3-line", 1, "inventarios"),
        new("inventarios-bodegas", "Bodegas", EContaxRoutes.InventariosBodegas, "ri-store-2-line", 2, "inventarios"),
        new("inventarios-reportes", "Reportes de inventario", EContaxRoutes.InventariosReportes, "ri-file-chart-line", 3, "inventarios"),
        new("reportes-documentos", "Reporte documentos", EContaxRoutes.ReportesDocumentos, "ri-file-chart-line", 9),
        new("administracion", "Administracion", null, "ri-admin-line", 10),
        new("roles", "Roles y permisos", EContaxRoutes.AdministracionRoles, "ri-shield-keyhole-line", 1, "administracion"),
        new("roles-internos", "Sucursales y roles", EContaxRoutes.Organizacion, "ri-team-line", 10),
        new("cajas", "Cajas y secuencias", EContaxRoutes.AdministracionCajasSecuencias, "ri-layout-grid-line", 2, "administracion"),
        new("impuestos", "Impuestos", EContaxRoutes.AdministracionImpuestos, "ri-percent-line", 3, "administracion"),
        new("usuarios", "Usuarios", EContaxRoutes.AdministracionUsuarios, "ri-user-line", 4, "administracion"),
        new("identificaciones", "Identificaciones", EContaxRoutes.AdministracionIdentificaciones, "ri-id-card-line", 5, "administracion"),
        new("formas-pago", "Formas de Pago", EContaxRoutes.AdministracionFormasPago, "ri-bank-card-line", 6, "administracion"),
        new("logs-inicio", "Logs de Inicio", EContaxRoutes.AdministracionLogsInicio, "ri-history-line", 7, "administracion"),
        new("retenciones", "Retenciones", EContaxRoutes.AdministracionRetenciones, "ri-inbox-archive-line", 8, "administracion"),
        new("auditoria-sql", "SQL Auditoria", EContaxRoutes.AdministracionAuditoriaSql, "ri-shield-check-line", 9, "administracion"),
        new("usuarios-empresa", "Usuarios", EContaxRoutes.Usuarios, "ri-user-settings-line", 11),
        new("documentos-generados", "Documentos Generados", null, "ri-folder-history-line", 12),
        new("notas-credito-generadas", "Mis Notas de Crédito", EContaxRoutes.FacturacionNotasCreditoGeneradas, "ri-file-list-2-line", 1, "documentos-generados"),
        new("retenciones-generadas", "Mis Retenciones", EContaxRoutes.FacturacionRetencionesGeneradas, "ri-file-list-2-line", 2, "documentos-generados"),
        new("guias-generadas", "Mis Guias de Remisión", EContaxRoutes.FacturacionGuiasRemisionGeneradas, "ri-file-list-2-line", 3, "documentos-generados"),
        new("liquidaciones-generadas", "Mis Liquidaciones Compras", EContaxRoutes.ComprasLiquidacionesGeneradas, "ri-file-list-2-line", 4, "documentos-generados"),
        new("notas-debito-generadas", "Mis Notas de Debito", EContaxRoutes.FacturacionNotasDebitoGeneradas, "ri-file-list-2-line", 5, "documentos-generados"),
        new("configuracion", "Configuración", null, "ri-settings-3-line", 13),
        new("emisor", "Emisor", EContaxRoutes.Emisor, "ri-building-2-line", 1, "configuracion"),
        new("perfil", "Mi Perfil", EContaxRoutes.Profile, "ri-user-settings-line", 2, "configuracion"),
        new("afiliacion", "Afiliación", EContaxRoutes.Afiliacion, "ri-building-4-line", 3, "configuracion"),
        new("puntos-emision", "Pto. Emision", EContaxRoutes.ConfiguracionPuntosEmision, "ri-safe-2-line", 3, "configuracion"),
        new("centro-normativo", "Centro normativo", EContaxRoutes.ConfiguracionCentroNormativo, "ri-scales-3-line", 4, "configuracion"),
        new("firma", "Firma", EContaxRoutes.ConfiguracionFirma, "ri-shield-keyhole-line", 5, "configuracion"),
        new("tutoriales", "Tutoriales", EContaxRoutes.Tutoriales, "ri-question-line", 14),
        new("soporte", "Soporte", EContaxRoutes.Soporte, "ri-customer-service-2-line", 15)
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
                (definicion.Key == "roles-internos" && string.Equals(item.UrlMenu, EContaxRoutes.AdministracionRolesInternos, StringComparison.OrdinalIgnoreCase)) ||
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

        await context.Database.ExecuteSqlInterpolatedAsync($@"
IF OBJECT_ID(N'[dbo].[ECONTAX_ROL_MENU]', N'U') IS NOT NULL
BEGIN
    INSERT INTO [dbo].[ECONTAX_ROL_MENU] ([id_rol], [id_menu])
    SELECT r.[id_rol], m.[id_menu]
    FROM [dbo].[rol] r
    INNER JOIN [dbo].[menu] m ON m.[url_menu] = {EContaxRoutes.InventariosBodegas}
    WHERE r.[nombre_rol] = {EContaxTenantService.RolJefeEmpresa}
      AND ISNULL(r.[estado_rol], 0) = 1
      AND NOT EXISTS (
          SELECT 1
          FROM [dbo].[ECONTAX_ROL_MENU] rm
          WHERE rm.[id_rol] = r.[id_rol] AND rm.[id_menu] = m.[id_menu]);
END");
    }
}
