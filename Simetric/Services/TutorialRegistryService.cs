using Simetric.Tutorials;

namespace Simetric.Services;

public sealed class TutorialRegistryService
{
    private readonly IReadOnlyDictionary<string, TutorialDefinition> _definitions;
    private readonly IReadOnlyDictionary<string, TutorialDefinition> _definitionsByRoute;

    public TutorialRegistryService()
    {
        var definitions = new[]
        {
            BuildDashboardTutorial(),
            BuildNuevaFacturaTutorial(),
            BuildFacturasTutorial(),
            BuildGuiaRemisionTutorial(),
            BuildGuiasRemisionGeneradasTutorial(),
            BuildNotaCreditoTutorial(),
            BuildNotasCreditoGeneradasTutorial(),
            BuildNotaDebitoTutorial(),
            BuildNotasDebitoGeneradasTutorial(),
            BuildRetencionesGeneradasTutorial(),
            BuildClientesTutorial(),
            BuildProductosTutorial(),
            BuildProveedoresTutorial(),
            BuildImportarCompraXmlTutorial(),
            BuildNuevaLiquidacionCompraTutorial(),
            BuildLiquidacionesCompraGeneradasTutorial(),
            BuildCuentasPorCobrarTutorial(),
            BuildEmisorTutorial(),
            BuildPerfilTutorial(),
            BuildMiCajaTutorial(),
            BuildCompraDocumentosTutorial(),
            BuildConfiguracionGeneralTutorial(),
            BuildCategoriasTutorial(),
            BuildIdentificacionesTutorial(),
            BuildImpuestosTutorial(),
            BuildRetencionesConfigTutorial(),
            BuildTutorialesCatalogoTutorial(),
            BuildLogsTutorial(),
            BuildNuevaSolicitudTutorial(),
            BuildAdminSolicitudesTutorial(),
            BuildMisPagosTutorial(),
            BuildAsociadosTutorial(),
            BuildUsuariosTutorial(),
            BuildTiposClienteTutorial(),
            BuildMfaTutorial(),
            BuildRolesPermisosTutorial()
        };

        _definitions = definitions.ToDictionary(x => x.Id, StringComparer.OrdinalIgnoreCase);
        _definitionsByRoute = definitions
            .SelectMany(definition => GetAllRoutes(definition)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .Select(route => new
                {
                    Route = NormalizeRoute(route),
                    Definition = definition
                }))
            .GroupBy(x => x.Route, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(
                x => x.Key,
                x => x.Last().Definition,
                StringComparer.OrdinalIgnoreCase);
    }

    public IReadOnlyList<TutorialDefinition> GetAll() =>
        _definitions.Values
            .OrderBy(x => x.Category)
            .ThenBy(x => x.Title)
            .ToList();

    public TutorialDefinition? GetById(string? tutorialId)
    {
        if (string.IsNullOrWhiteSpace(tutorialId))
            return null;

        return _definitions.TryGetValue(tutorialId.Trim(), out var definition)
            ? definition
            : null;
    }

    public TutorialDefinition? GetByRoute(string? route)
    {
        if (string.IsNullOrWhiteSpace(route))
            return null;

        return _definitionsByRoute.TryGetValue(NormalizeRoute(route), out var definition)
            ? definition
            : null;
    }

    private static string NormalizeRoute(string route)
    {
        var separatorIndex = route.IndexOfAny(new[] { '?', '#' });
        var cleanRoute = (separatorIndex >= 0 ? route[..separatorIndex] : route).Trim();

        if (string.IsNullOrWhiteSpace(cleanRoute))
            return "/";

        return cleanRoute.StartsWith("/", StringComparison.Ordinal)
            ? cleanRoute
            : $"/{cleanRoute}";
    }

    private static IEnumerable<string> GetAllRoutes(TutorialDefinition definition)
    {
        yield return definition.Route;

        foreach (var routeAlias in definition.RouteAliases.Where(x => !string.IsNullOrWhiteSpace(x)))
        {
            yield return routeAlias;
        }
    }

    private static TutorialDefinition BuildDashboardTutorial() =>
        new()
        {
            Id = "dashboard-inicio",
            Title = "Panel de inicio",
            Description = "Recorrido guiado para entender los accesos directos y los indicadores principales del dashboard.",
            Route = "/dashboard",
            DefaultTargetSelector = "[data-tour='dashboard.page']",
            Category = "Operacion",
            Steps = new[]
            {
                new TutorialStep
                {
                    Id = "dashboard-toolbar",
                    Title = "Barra superior",
                    Description = "Desde aqui buscas informacion del sistema, revisas filtros rapidos, notificaciones y la sesion activa.",
                    TargetSelector = "[data-tour='dashboard.toolbar']",
                    FallbackTargetSelector = "[data-tour='dashboard.page']",
                    Padding = 12,
                    CardPlacement = "bottom"
                },
                new TutorialStep
                {
                    Id = "dashboard-search",
                    Title = "Busqueda del sistema",
                    Description = "Usa este buscador para ubicar accesos o informacion de trabajo sin salir del dashboard.",
                    TargetSelector = "[data-tour='dashboard.search']",
                    FallbackTargetSelector = "[data-tour='dashboard.toolbar']",
                    Padding = 10,
                    CardPlacement = "bottom"
                },
                new TutorialStep
                {
                    Id = "dashboard-hero",
                    Title = "Indicadores principales",
                    Description = "Esta franja resume el comportamiento del mes: facturas, ventas, cartera, cobro y documentos emitidos.",
                    TargetSelector = "[data-tour='dashboard.hero']",
                    Padding = 14,
                    CardPlacement = "bottom"
                },
                new TutorialStep
                {
                    Id = "dashboard-summary",
                    Title = "Resumen principal",
                    Description = "Este panel muestra cifras reales del negocio, como facturas registradas, clientes activos y productos disponibles.",
                    TargetSelector = "[data-tour='dashboard.summary']",
                    Padding = 12,
                    CardPlacement = "left"
                },
                new TutorialStep
                {
                    Id = "dashboard-metrics",
                    Title = "Indicadores operativos",
                    Description = "Aqui se concentran los indicadores de rendimiento y seguimiento diario de la operacion.",
                    TargetSelector = "[data-tour='dashboard.metrics']",
                    Padding = 12,
                    CardPlacement = "bottom"
                },
                new TutorialStep
                {
                    Id = "dashboard-quick-actions",
                    Title = "Accesos rapidos",
                    Description = "Estos botones te llevan directo a crear facturas, clientes, productos, notas, cobros, guias o historiales.",
                    TargetSelector = "[data-tour='dashboard.quick-actions']",
                    FallbackTargetSelector = "[data-tour='dashboard.page']",
                    Padding = 12,
                    CardPlacement = "top"
                },
                new TutorialStep
                {
                    Id = "dashboard-recent-documents",
                    Title = "Ultimos documentos",
                    Description = "Aqui revisas los comprobantes recientes y cambias la cantidad visible para consultar actividad reciente.",
                    TargetSelector = "[data-tour='dashboard.recent-documents']",
                    FallbackTargetSelector = "[data-tour='dashboard.quick-actions']",
                    Padding = 12,
                    CardPlacement = "top"
                },
                new TutorialStep
                {
                    Id = "dashboard-setup-bot",
                    Title = "Bot de configuracion",
                    Description = "Cuando falte un paso para emitir, este bot te indica que completar y te lleva directo a la vista necesaria.",
                    TargetSelector = "[data-tour='dashboard.setup-bot']",
                    FallbackTargetSelector = "[data-tour='dashboard.ai-assistant']",
                    Padding = 12,
                    CardPlacement = "top"
                },
                new TutorialStep
                {
                    Id = "dashboard-ai-assistant",
                    Title = "Asistente IA",
                    Description = "Este boton abre el asistente de facturacion para ayudarte con facturas, notas y consultas operativas.",
                    TargetSelector = "[data-tour='dashboard.ai-assistant']",
                    FallbackTargetSelector = "[data-tour='dashboard.page']",
                    Shape = "circle",
                    Padding = 12,
                    CardPlacement = "left"
                }
            }
        };

    private static TutorialDefinition BuildNuevaFacturaTutorial() =>
        new()
        {
            Id = "facturacion-nueva",
            Title = "Nueva factura",
            Description = "Guia para registrar el cliente, completar los datos del comprobante, agregar productos y emitir la factura.",
            Route = "/facturacion/nueva",
            DefaultTargetSelector = "[data-tour='invoice.page']",
            Category = "Facturacion",
            Steps = new[]
            {
                new TutorialStep
                {
                    Id = "invoice-client",
                    Title = "Informacion del cliente",
                    Description = "Primero identifica o crea al cliente y completa sus datos comerciales y de contacto.",
                    TargetSelector = "[data-tour='invoice.client-section']",
                    Padding = 12,
                    CardPlacement = "bottom"
                },
                new TutorialStep
                {
                    Id = "invoice-header",
                    Title = "Forma de pago",
                    Description = "En esta seccion defines la forma de pago antes de revisar el detalle y cerrar el comprobante.",
                    TargetSelector = "[data-tour='invoice.payment-section']",
                    FallbackTargetSelector = "[data-tour='invoice.client-section']",
                    Padding = 12,
                    CardPlacement = "bottom"
                },
                new TutorialStep
                {
                    Id = "invoice-detail",
                    Title = "Detalle de productos",
                    Description = "Aqui tienes una fila inicial y botones juntos para seleccionar un producto/servicio o registrar uno nuevo.",
                    TargetSelector = "[data-tour='invoice.detail-section']",
                    Padding = 12,
                    CardPlacement = "top"
                },
                new TutorialStep
                {
                    Id = "invoice-totals",
                    Title = "Totales y cierre",
                    Description = "Revisa los totales finales y usa la accion principal para guardar y procesar la factura.",
                    TargetSelector = "[data-tour='invoice.totals-section']",
                    Padding = 12,
                    CardPlacement = "left"
                }
            }
        };

    private static TutorialDefinition BuildFacturasTutorial() =>
        new()
        {
            Id = "facturas-listado",
            Title = "Facturas emitidas",
            Description = "Recorrido para filtrar, revisar y abrir el detalle de los comprobantes emitidos.",
            Route = "/facturas",
            DefaultTargetSelector = "[data-tour='facturas.page']",
            Category = "Facturacion",
            Steps = new[]
            {
                new TutorialStep
                {
                    Id = "facturas-toolbar",
                    Title = "Barra de control",
                    Description = "Usa esta zona para buscar facturas concretas y refrescar el listado cuando necesites datos actualizados.",
                    TargetSelector = "[data-tour='facturas.toolbar']",
                    Padding = 12,
                    CardPlacement = "bottom"
                },
                new TutorialStep
                {
                    Id = "facturas-list",
                    Title = "Listado de documentos",
                    Description = "La tabla muestra numero, cliente, identificacion y monto total de cada factura encontrada.",
                    TargetSelector = "[data-tour='facturas.table']",
                    Padding = 12,
                    CardPlacement = "bottom"
                },
                new TutorialStep
                {
                    Id = "facturas-actions",
                    Title = "Acciones por registro",
                    Description = "Desde cada fila puedes abrir la vista detallada del comprobante seleccionado.",
                    TargetSelector = "[data-tour='facturas.row-action']",
                    FallbackTargetSelector = "[data-tour='facturas.table']",
                    Padding = 8,
                    CardPlacement = "left"
                },
                new TutorialStep
                {
                    Id = "facturas-footer",
                    Title = "Resumen visible",
                    Description = "Este bloque resume los resultados filtrados, el monto visible y la paginacion del listado actual.",
                    TargetSelector = "[data-tour='facturas.footer']",
                    Padding = 12,
                    CardPlacement = "top"
                }
            }
        };

    private static TutorialDefinition BuildGuiaRemisionTutorial() =>
        new()
        {
            Id = "guia-remision-emision",
            Title = "Guia de remision",
            Description = "Guia para cargar una factura origen, completar el traslado y guardar la guia emitida.",
            Route = "/facturacion/guia-remision",
            DefaultTargetSelector = "[data-tour='guia.page']",
            Category = "Facturacion",
            Steps = new[]
            {
                new TutorialStep
                {
                    Id = "guia-hero",
                    Title = "Resumen del documento",
                    Description = "Esta cabecera resume la factura base, el numero de guia, el transportista y los detalles cargados.",
                    TargetSelector = "[data-tour='guia.hero']",
                    Padding = 12,
                    CardPlacement = "bottom"
                },
                new TutorialStep
                {
                    Id = "guia-lookup",
                    Title = "Datos de guia",
                    Description = "Completa la referencia, el transportista, las fechas de traslado y los datos del destinatario.",
                    TargetSelector = "[data-tour='guia.form']",
                    Padding = 12,
                    CardPlacement = "bottom"
                },
                new TutorialStep
                {
                    Id = "guia-form",
                    Title = "Detalle movilizado",
                    Description = "Revisa los items traidos desde la factura y ajusta codigos, descripcion y cantidades cuando corresponda.",
                    TargetSelector = "[data-tour='guia.detail']",
                    Padding = 12,
                    CardPlacement = "top"
                },
                new TutorialStep
                {
                    Id = "guia-detail",
                    Title = "Resumen del traslado",
                    Description = "Este bloque resume los totales y datos finales antes de emitir la guia de remision.",
                    TargetSelector = "[data-tour='guia.summary']",
                    FallbackTargetSelector = "[data-tour='guia.totals-section']",
                    Padding = 12,
                    CardPlacement = "top"
                },
                new TutorialStep
                {
                    Id = "guia-save",
                    Title = "Guardar la guia",
                    Description = "Cuando la informacion obligatoria este completa, revisa los totales finales antes de guardar.",
                    TargetSelector = "[data-tour='guia.totals-section']",
                    FallbackTargetSelector = "[data-tour='guia.summary']",
                    Padding = 10,
                    CardPlacement = "top"
                }
            }
        };

    private static TutorialDefinition BuildGuiasRemisionGeneradasTutorial() =>
        new()
        {
            Id = "guias-remision-generadas",
            Title = "Guias de remision generadas",
            Description = "Recorrido para consultar las guias emitidas y abrir su detalle, XML o PDF.",
            Route = "/facturacion/guias-remision-generadas",
            DefaultTargetSelector = "[data-tour='grgen.page']",
            Category = "Facturacion",
            Steps = new[]
            {
                new TutorialStep
                {
                    Id = "grgen-toolbar",
                    Title = "Resumen del listado",
                    Description = "Esta cabecera describe la vista y deja visible el control de cantidad de registros por pagina.",
                    TargetSelector = "[data-tour='grgen.toolbar']",
                    Padding = 12,
                    CardPlacement = "bottom"
                },
                new TutorialStep
                {
                    Id = "grgen-table",
                    Title = "Guias emitidas",
                    Description = "La tabla concentra numero de guia, destinatario, transportista, fechas de traslado y estado SRI.",
                    TargetSelector = "[data-tour='grgen.table']",
                    Padding = 12,
                    CardPlacement = "bottom"
                },
                new TutorialStep
                {
                    Id = "grgen-action",
                    Title = "Detalle y archivos",
                    Description = "Desde esta accion puedes abrir el detalle del comprobante y consultar su XML o PDF generado.",
                    TargetSelector = "[data-tour='grgen.action']",
                    FallbackTargetSelector = "[data-tour='grgen.table']",
                    Padding = 8,
                    CardPlacement = "left"
                }
            }
        };

    private static TutorialDefinition BuildClientesTutorial() =>
        new()
        {
            Id = "clientes-gestion",
            Title = "Gestion de clientes",
            Description = "Tutorial para consultar clientes, filtrar registros y mantener la ficha comercial desde un mismo panel.",
            Route = "/clientes",
            DefaultTargetSelector = "[data-tour='clientes.page']",
            Category = "Catalogos",
            Steps = new[]
            {
                new TutorialStep
                {
                    Id = "clientes-hero",
                    Title = "Resumen comercial",
                    Description = "Aqui ves el total de clientes activos y el estado general del catalogo.",
                    TargetSelector = "[data-tour='clientes.hero']",
                    FallbackTargetSelector = "[data-tour='clientes.page']",
                    Padding = 12,
                    CardPlacement = "bottom"
                },
                new TutorialStep
                {
                    Id = "clientes-toolbar",
                    Title = "Busqueda y filtros",
                    Description = "Usa esta barra para ubicar clientes por nombre, identificacion o tipo comercial.",
                    TargetSelector = "[data-tour='clientes.toolbar']",
                    Padding = 12,
                    CardPlacement = "bottom"
                },
                new TutorialStep
                {
                    Id = "clientes-table",
                    Title = "Listado de clientes",
                    Description = "La tabla te permite revisar rapidamente datos clave y abrir acciones de ver, editar o eliminar.",
                    TargetSelector = "[data-tour='clientes.table']",
                    Padding = 12,
                    CardPlacement = "bottom"
                },
                new TutorialStep
                {
                    Id = "clientes-editor",
                    Title = "Formulario lateral",
                    Description = "Desde este formulario registras nuevos clientes o actualizas la informacion del registro seleccionado.",
                    TargetSelector = "[data-tour='clientes.editor']",
                    Padding = 12,
                    CardPlacement = "left"
                }
            }
        };

    private static TutorialDefinition BuildProductosTutorial() =>
        new()
        {
            Id = "productos-catalogo",
            Title = "Productos y servicios",
            Description = "Recorrido para administrar el catalogo, filtrar por categoria y completar la configuracion comercial.",
            Route = "/productos",
            DefaultTargetSelector = "[data-tour='productos.page']",
            Category = "Catalogos",
            Steps = new[]
            {
                new TutorialStep
                {
                    Id = "productos-hero",
                    Title = "Resumen del catalogo",
                    Description = "Este bloque presenta el estado general del catalogo de productos y servicios.",
                    TargetSelector = "[data-tour='productos.hero']",
                    FallbackTargetSelector = "[data-tour='productos.page']",
                    Padding = 12,
                    CardPlacement = "bottom"
                },
                new TutorialStep
                {
                    Id = "productos-toolbar",
                    Title = "Busqueda y categoria",
                    Description = "Desde aqui puedes filtrar por nombre, codigo o categoria para encontrar rapidamente un registro.",
                    TargetSelector = "[data-tour='productos.toolbar']",
                    Padding = 12,
                    CardPlacement = "bottom"
                },
                new TutorialStep
                {
                    Id = "productos-table",
                    Title = "Tabla de productos",
                    Description = "La tabla concentra el catalogo activo y sus acciones principales de consulta, edicion y baja logica.",
                    TargetSelector = "[data-tour='productos.table']",
                    Padding = 12,
                    CardPlacement = "bottom"
                },
                new TutorialStep
                {
                    Id = "productos-editor",
                    Title = "Formulario de registro",
                    Description = "En este formulario defines precios, impuestos, categoria y otros datos del producto o servicio.",
                    TargetSelector = "[data-tour='productos.editor']",
                    Padding = 12,
                    CardPlacement = "left"
                }
            }
        };

    private static TutorialDefinition BuildProveedoresTutorial() =>
        new()
        {
            Id = "proveedores-gestion",
            Title = "Gestion de proveedores",
            Description = "Recorrido para consultar proveedores, aplicar filtros y mantener su informacion comercial.",
            Route = "/proveedores",
            DefaultTargetSelector = "[data-tour='proveedores.page']",
            Category = "Catalogos",
            Steps = new[]
            {
                new TutorialStep
                {
                    Id = "proveedores-hero",
                    Title = "Resumen de proveedores",
                    Description = "Este bloque presenta el estado general del catalogo de proveedores.",
                    TargetSelector = "[data-tour='proveedores.hero']",
                    Padding = 12,
                    CardPlacement = "bottom"
                },
                new TutorialStep
                {
                    Id = "proveedores-toolbar",
                    Title = "Busqueda y filtros",
                    Description = "Usa esta barra para ubicar proveedores por nombre, identificacion o estado.",
                    TargetSelector = "[data-tour='proveedores.toolbar']",
                    Padding = 12,
                    CardPlacement = "bottom"
                },
                new TutorialStep
                {
                    Id = "proveedores-table",
                    Title = "Listado de proveedores",
                    Description = "La tabla concentra los registros disponibles y sus acciones de consulta o mantenimiento.",
                    TargetSelector = "[data-tour='proveedores.table']",
                    Padding = 12,
                    CardPlacement = "bottom"
                },
                new TutorialStep
                {
                    Id = "proveedores-editor",
                    Title = "Formulario de proveedor",
                    Description = "Desde este formulario registras nuevos proveedores o actualizas los datos del registro seleccionado.",
                    TargetSelector = "[data-tour='proveedores.editor']",
                    Padding = 12,
                    CardPlacement = "left"
                }
            }
        };

    private static TutorialDefinition BuildEmisorTutorial() =>
        new()
        {
            Id = "emisor-gestion",
            Title = "Gestion del emisor",
            Description = "Recorrido para mantener la informacion fiscal y los datos operativos del emisor.",
            Route = "/emisor",
            DefaultTargetSelector = "[data-tour='emisor.page']",
            Category = "Configuracion",
            Steps = new[]
            {
                new TutorialStep
                {
                    Id = "emisor-hero",
                    Title = "Resumen del emisor",
                    Description = "Este panel muestra el estado general de los emisores registrados y su configuracion principal.",
                    TargetSelector = "[data-tour='emisor.hero']",
                    FallbackTargetSelector = "[data-tour='emisor.page']",
                    Padding = 12,
                    CardPlacement = "bottom"
                },
                new TutorialStep
                {
                    Id = "emisor-toolbar",
                    Title = "Acciones de control",
                    Description = "Desde aqui puedes buscar registros, refrescar la informacion y crear un nuevo emisor cuando corresponda.",
                    TargetSelector = "[data-tour='emisor.toolbar']",
                    Padding = 12,
                    CardPlacement = "bottom"
                },
                new TutorialStep
                {
                    Id = "emisor-table",
                    Title = "Listado de emisores",
                    Description = "La tabla centraliza la consulta de emisores y sus acciones de detalle, edicion y baja logica.",
                    TargetSelector = "[data-tour='emisor.table']",
                    Padding = 12,
                    CardPlacement = "bottom"
                },
                new TutorialStep
                {
                    Id = "emisor-editor",
                    Title = "Formulario fiscal",
                    Description = "En este formulario completas razon social, RUC, logo y demas configuraciones del emisor.",
                    TargetSelector = "[data-tour='emisor.editor']",
                    Padding = 12,
                    CardPlacement = "left"
                }
            }
        };

    private static TutorialDefinition BuildPerfilTutorial() =>
        new()
        {
            Id = "perfil-usuario",
            Title = "Mi perfil",
            Description = "Recorrido para elegir avatar, actualizar datos personales y cambiar la clave desde el perfil.",
            Route = "/perfil",
            DefaultTargetSelector = "[data-tour='perfil.page']",
            Category = "Cuenta",
            Steps = new[]
            {
                new TutorialStep
                {
                    Id = "perfil-avatar",
                    Title = "Avatar y biblioteca",
                    Description = "En esta columna ves la vista previa del avatar y puedes seleccionar una imagen disponible para tu cuenta.",
                    TargetSelector = "[data-tour='perfil.avatar']",
                    FallbackTargetSelector = "[data-tour='perfil.page']",
                    Padding = 12,
                    CardPlacement = "right"
                },
                new TutorialStep
                {
                    Id = "perfil-form",
                    Title = "Datos personales",
                    Description = "Aqui editas nombres, apellidos y fecha de nacimiento manteniendo visible el correo de solo lectura.",
                    TargetSelector = "[data-tour='perfil.form']",
                    FallbackTargetSelector = "[data-tour='perfil.page']",
                    Padding = 12,
                    CardPlacement = "left"
                },
                new TutorialStep
                {
                    Id = "perfil-security",
                    Title = "Seguridad de acceso",
                    Description = "Este bloque te permite decidir si vas a cambiar la contrasena y completar el nuevo par de claves.",
                    TargetSelector = "[data-tour='perfil.security']",
                    FallbackTargetSelector = "[data-tour='perfil.form']",
                    Padding = 12,
                    CardPlacement = "top"
                },
                new TutorialStep
                {
                    Id = "perfil-save",
                    Title = "Guardar cambios",
                    Description = "Cuando todo este correcto, usa esta zona final para persistir la actualizacion del perfil.",
                    TargetSelector = "[data-tour='perfil.save']",
                    FallbackTargetSelector = "[data-tour='perfil.form']",
                    Padding = 10,
                    CardPlacement = "top"
                }
            }
        };

    private static TutorialDefinition BuildConfiguracionGeneralTutorial() =>
        new()
        {
            Id = "configuracion-general",
            Title = "Configuracion general",
            Description = "Guia para administrar formas de pago, tipos de documento y cajas desde un mismo panel de mantenimiento.",
            Route = "/configuracion/general",
            DefaultTargetSelector = "[data-tour='config.page']",
            Category = "Configuracion",
            Steps = new[]
            {
                new TutorialStep
                {
                    Id = "config-hero",
                    Title = "Resumen de mantenimiento",
                    Description = "Este bloque presenta el volumen de formas de pago, documentos y cajas configuradas en el sistema.",
                    TargetSelector = "[data-tour='config.hero']",
                    Padding = 12,
                    CardPlacement = "bottom"
                },
                new TutorialStep
                {
                    Id = "config-tabs",
                    Title = "Secciones del panel",
                    Description = "Estas pestañas cambian entre formas de pago, tipos de documento y configuraciones de cajas y series.",
                    TargetSelector = "[data-tour='config.tabs']",
                    Padding = 10,
                    CardPlacement = "bottom"
                },
                new TutorialStep
                {
                    Id = "config-header",
                    Title = "Contexto actual",
                    Description = "Aqui ves el mantenimiento activo y el boton principal para crear o editar registros segun la pestaña seleccionada.",
                    TargetSelector = "[data-tour='config.card-head']",
                    Padding = 12,
                    CardPlacement = "bottom"
                },
                new TutorialStep
                {
                    Id = "config-table",
                    Title = "Listado de mantenimiento",
                    Description = "La tabla cambia segun la pestaña y concentra los registros operativos junto con sus acciones disponibles.",
                    TargetSelector = "[data-tour='config.table']",
                    Padding = 12,
                    CardPlacement = "top"
                }
            }
        };

    private static TutorialDefinition BuildUsuariosTutorial() =>
        new()
        {
            Id = "usuarios-dashboard",
            Title = "Usuarios del sistema",
            Description = "Tutorial para administrar usuarios, revisar seguridad y acceder a las acciones de mantenimiento de cuentas.",
            Route = "/configuracion/usuarios",
            DefaultTargetSelector = "[data-tour='usuarios.page']",
            Category = "Seguridad",
            Steps = new[]
            {
                new TutorialStep
                {
                    Id = "usuarios-hero",
                    Title = "Resumen de usuarios",
                    Description = "Esta cabecera resume el estado del directorio administrativo y sus accesos principales.",
                    TargetSelector = "[data-tour='usuarios.hero']",
                    Padding = 12,
                    CardPlacement = "bottom"
                },
                new TutorialStep
                {
                    Id = "usuarios-actions",
                    Title = "Acciones principales",
                    Description = "Desde aqui puedes abrir la galeria de avatares o crear un nuevo usuario del sistema.",
                    TargetSelector = "[data-tour='usuarios.actions']",
                    Padding = 10,
                    CardPlacement = "left"
                },
                new TutorialStep
                {
                    Id = "usuarios-table",
                    Title = "Directorio administrativo",
                    Description = "La tabla muestra la informacion del usuario, su seguridad y las acciones de mantenimiento disponibles.",
                    TargetSelector = "[data-tour='usuarios.table']",
                    Padding = 12,
                    CardPlacement = "bottom"
                }
            }
        };

    private static TutorialDefinition BuildTiposClienteTutorial() =>
        new()
        {
            Id = "tipos-cliente",
            Title = "Tipos de cliente",
            Description = "Recorrido para mantener el catalogo de tipos de cliente que se usa en clientes y procesos comerciales.",
            Route = "/configuracion/tipos-cliente",
            DefaultTargetSelector = "[data-tour='tipos.page']",
            Category = "Configuracion",
            Steps = new[]
            {
                new TutorialStep
                {
                    Id = "tipos-hero",
                    Title = "Resumen del catalogo",
                    Description = "Esta cabecera muestra el total de tipos registrados y el contexto del mantenimiento.",
                    TargetSelector = "[data-tour='tipos.hero']",
                    Padding = 12,
                    CardPlacement = "bottom"
                },
                new TutorialStep
                {
                    Id = "tipos-toolbar",
                    Title = "Accion principal",
                    Description = "Usa este boton para crear un nuevo tipo de cliente desde la misma vista.",
                    TargetSelector = "[data-tour='tipos.toolbar']",
                    Padding = 10,
                    CardPlacement = "left"
                },
                new TutorialStep
                {
                    Id = "tipos-table",
                    Title = "Listado de tipos",
                    Description = "La tabla presenta los tipos disponibles y sus acciones de detalle, edicion y eliminacion.",
                    TargetSelector = "[data-tour='tipos.table']",
                    Padding = 12,
                    CardPlacement = "bottom"
                }
            }
        };

    private static TutorialDefinition BuildImportarCompraXmlTutorial() =>
        new()
        {
            Id = "compras-importar-xml",
            Title = "Importar compras XML",
            Description = "Guia para emitir retenciones desde XML, liquidacion o captura manual con campos bloqueados y detalle revisable.",
            Route = "/compras/importar-xml",
            DefaultTargetSelector = "[data-tour='compras.page']",
            Category = "Compras",
            Steps = new[]
            {
                new TutorialStep
                {
                    Id = "compras-overview",
                    Title = "Vista de retencion",
                    Description = "Esta pantalla centraliza XML, busqueda de liquidacion y captura manual para generar la retencion.",
                    TargetSelector = "[data-tour='compras.page']",
                    Padding = 16,
                    CardPlacement = "bottom"
                },
                new TutorialStep
                {
                    Id = "compras-search",
                    Title = "Buscar liquidacion",
                    Description = "Usa este buscador para recuperar una liquidacion por numero o secuencial con el mismo estilo de factura.",
                    TargetSelector = "[data-tour='compras.search']",
                    Padding = 12,
                    CardPlacement = "bottom"
                },
                new TutorialStep
                {
                    Id = "compras-import",
                    Title = "Modos de registro",
                    Description = "Desde la cabecera puedes trabajar con XML, liquidacion o captura manual segun el documento recibido.",
                    TargetSelector = "[data-tour='compras.hero']",
                    Padding = 12,
                    CardPlacement = "bottom"
                },
                new TutorialStep
                {
                    Id = "compras-preview",
                    Title = "Revision previa",
                    Description = "Aqui revisas proveedor, RUC, razon social, comprobante, detalle con fila inicial, retenciones y totales.",
                    TargetSelector = "[data-tour='compras.preview']",
                    Padding = 12,
                    CardPlacement = "top"
                },
                new TutorialStep
                {
                    Id = "compras-save",
                    Title = "Guardar compra",
                    Description = "Cuando todo este correcto, usa esta accion para registrar la compra y generar XML/PDF de retencion.",
                    TargetSelector = "[data-tour='compras.save']",
                    Padding = 10,
                    CardPlacement = "left"
                }
            }
        };

    private static TutorialDefinition BuildNuevaLiquidacionCompraTutorial() =>
        new()
        {
            Id = "liquidacion-compra-manual",
            Title = "Nueva liquidacion de compra",
            Description = "Guia para registrar una liquidacion manual con buscadores de proveedor, detalle editable y totales actualizados.",
            Route = "/compras/nueva-liquidacion",
            DefaultTargetSelector = "[data-tour='liquidacion.page']",
            Category = "Compras",
            Steps = new[]
            {
                new TutorialStep
                {
                    Id = "liquidacion-hero",
                    Title = "Resumen del borrador",
                    Description = "Esta cabecera resume la serie activa, el proveedor cargado y el total acumulado de la liquidacion.",
                    TargetSelector = "[data-tour='liquidacion.hero']",
                    Padding = 12,
                    CardPlacement = "bottom"
                },
                new TutorialStep
                {
                    Id = "liquidacion-document",
                    Title = "Emisor y documento",
                    Description = "Aqui revisas serie, secuencial, fecha y forma de pago antes de completar el proveedor.",
                    TargetSelector = "[data-tour='liquidacion.document']",
                    Padding = 12,
                    CardPlacement = "bottom"
                },
                new TutorialStep
                {
                    Id = "liquidacion-provider",
                    Title = "Datos del proveedor",
                    Description = "Busca proveedor por identificacion o razon social con sugerencias visuales alineadas a factura.",
                    TargetSelector = "[data-tour='liquidacion.provider']",
                    Padding = 12,
                    CardPlacement = "bottom"
                },
                new TutorialStep
                {
                    Id = "liquidacion-detail",
                    Title = "Detalle de compra",
                    Description = "En este bloque siempre tienes una fila inicial y puedes editar cantidades, descuentos e impuestos.",
                    TargetSelector = "[data-tour='liquidacion.detail']",
                    Padding = 12,
                    CardPlacement = "top"
                },
                new TutorialStep
                {
                    Id = "liquidacion-summary",
                    Title = "Totales y guardado",
                    Description = "Este resumen recalcula bases, descuentos e IVA en tiempo real y concentra las acciones finales para guardar.",
                    TargetSelector = "[data-tour='liquidacion.summary']",
                    Padding = 12,
                    CardPlacement = "top"
                }
            }
        };

    private static TutorialDefinition BuildLiquidacionesCompraGeneradasTutorial() =>
        new()
        {
            Id = "liquidaciones-compra-generadas",
            Title = "Liquidaciones generadas",
            Description = "Tutorial para revisar liquidaciones emitidas y abrir su detalle, XML o PDF cuando sea necesario.",
            Route = "/compras/liquidaciones-generadas",
            DefaultTargetSelector = "[data-tour='liqgen.page']",
            Category = "Compras",
            Steps = new[]
            {
                new TutorialStep
                {
                    Id = "liqgen-toolbar",
                    Title = "Resumen del modulo",
                    Description = "Esta cabecera introduce la vista y deja visible el control de cantidad de registros por pagina.",
                    TargetSelector = "[data-tour='liqgen.toolbar']",
                    Padding = 12,
                    CardPlacement = "bottom"
                },
                new TutorialStep
                {
                    Id = "liqgen-table",
                    Title = "Listado de liquidaciones",
                    Description = "La tabla muestra numero, fecha, proveedor y valores principales de cada liquidacion registrada.",
                    TargetSelector = "[data-tour='liqgen.table']",
                    Padding = 12,
                    CardPlacement = "bottom"
                },
                new TutorialStep
                {
                    Id = "liqgen-action",
                    Title = "Detalle y comprobantes",
                    Description = "Desde esta fila puedes abrir el detalle de la liquidacion y consultar su XML o PDF.",
                    TargetSelector = "[data-tour='liqgen.action']",
                    FallbackTargetSelector = "[data-tour='liqgen.table']",
                    Padding = 8,
                    CardPlacement = "left"
                }
            }
        };

    private static TutorialDefinition BuildCuentasPorCobrarTutorial() =>
        new()
        {
            Id = "cuentas-por-cobrar-abonos",
            Title = "Cuentas por cobrar",
            Description = "Guia para buscar un cliente, distribuir un abono entre facturas pendientes y registrar el pago.",
            Route = "/cuentas-por-cobrar",
            DefaultTargetSelector = "[data-tour='abonos.page']",
            Category = "Operacion",
            UseGlobalHost = false,
            Steps = new[]
            {
                new TutorialStep
                {
                    Id = "abonos-hero",
                    Title = "Resumen de cartera",
                    Description = "Esta cabecera muestra facturas encontradas, total pendiente, monto recibido y saldo por asignar.",
                    TargetSelector = "[data-tour='abonos.hero']",
                    Padding = 12,
                    CardPlacement = "bottom"
                },
                new TutorialStep
                {
                    Id = "abonos-search",
                    Title = "Busqueda rapida",
                    Description = "Busca por cliente, identificacion o numero de factura y usa los botones para limpiar o aplicar la busqueda.",
                    TargetSelector = "[data-tour='abonos.search']",
                    FallbackTargetSelector = "[data-tour='abonos.page']",
                    Padding = 12,
                    CardPlacement = "bottom"
                },
                new TutorialStep
                {
                    Id = "abonos-filters",
                    Title = "Filtros de cartera",
                    Description = "Refina la cartera por estado, cliente, vencimiento y saldo pendiente para encontrar el documento correcto.",
                    TargetSelector = "[data-tour='abonos.filters']",
                    FallbackTargetSelector = "[data-tour='abonos.search']",
                    Padding = 12,
                    CardPlacement = "bottom"
                },
                new TutorialStep
                {
                    Id = "abonos-table",
                    Title = "Facturas por cobrar",
                    Description = "La tabla muestra cliente, vencimiento, saldo y estado de cada factura pendiente.",
                    TargetSelector = "[data-tour='abonos.table']",
                    FallbackTargetSelector = "[data-tour='abonos.page']",
                    Padding = 12,
                    CardPlacement = "top"
                },
                new TutorialStep
                {
                    Id = "abonos-row-action",
                    Title = "Registrar desde la fila",
                    Description = "Este boton selecciona la factura y te lleva al flujo de registro del abono.",
                    TargetSelector = "[data-tour='abonos.row-action']",
                    FallbackTargetSelector = "[data-tour='abonos.table']",
                    Padding = 8,
                    CardPlacement = "left"
                },
                new TutorialStep
                {
                    Id = "abonos-steps",
                    Title = "Flujo por pasos",
                    Description = "Puedes navegar por identificacion del cliente, registro del pago, distribucion y confirmacion final.",
                    TargetSelector = "[data-tour='abonos.steps']",
                    FallbackTargetSelector = "[data-tour='abonos.form']",
                    Padding = 10,
                    CardPlacement = "bottom"
                },
                new TutorialStep
                {
                    Id = "abonos-client-search",
                    Title = "Seleccionar cliente",
                    Description = "Ingresa cedula, RUC o nombre y selecciona una coincidencia para cargar sus facturas pendientes.",
                    TargetSelector = "[data-tour='abonos.client-search']",
                    FallbackTargetSelector = "[data-tour='abonos.form']",
                    Padding = 12,
                    CardPlacement = "bottom"
                },
                new TutorialStep
                {
                    Id = "abonos-payment",
                    Title = "Registrar pago",
                    Description = "Cuando el cliente este seleccionado, registra el monto recibido, observacion y accesos rapidos como saldo total.",
                    TargetSelector = "[data-tour='abonos.payment-fields']",
                    FallbackTargetSelector = "[data-tour='abonos.form']",
                    Padding = 12,
                    CardPlacement = "bottom"
                },
                new TutorialStep
                {
                    Id = "abonos-distribution-actions",
                    Title = "Acciones de distribucion",
                    Description = "Distribuye automaticamente el pago, limpia la distribucion o vuelve a editar el monto recibido.",
                    TargetSelector = "[data-tour='abonos.distribution-actions']",
                    FallbackTargetSelector = "[data-tour='abonos.distribution']",
                    Padding = 10,
                    CardPlacement = "bottom"
                },
                new TutorialStep
                {
                    Id = "abonos-invoice-list",
                    Title = "Aplicar por factura",
                    Description = "Aqui defines cuanto se aplica a cada factura y puedes usar los botones Disponible o Maximo por fila.",
                    TargetSelector = "[data-tour='abonos.invoice-list']",
                    FallbackTargetSelector = "[data-tour='abonos.distribution']",
                    Padding = 12,
                    CardPlacement = "top"
                },
                new TutorialStep
                {
                    Id = "abonos-review",
                    Title = "Revision final",
                    Description = "Antes de registrar, revisa monto recibido, total distribuido, saldo sin asignar y facturas afectadas.",
                    TargetSelector = "[data-tour='abonos.review']",
                    FallbackTargetSelector = "[data-tour='abonos.distribution']",
                    Padding = 12,
                    CardPlacement = "top"
                },
                new TutorialStep
                {
                    Id = "abonos-confirm",
                    Title = "Confirmar abono",
                    Description = "Este boton abre la confirmacion final y registra el abono cuando todo esta correcto.",
                    TargetSelector = "[data-tour='abonos.confirm']",
                    FallbackTargetSelector = "[data-tour='abonos.review']",
                    Padding = 10,
                    CardPlacement = "top"
                }
            }
        };

    private static TutorialDefinition BuildNotaCreditoTutorial() =>
        new()
        {
            Id = "nota-credito-emision",
            Title = "Nota de credito",
            Description = "Recorrido para emitir nota de credito por factura, XML o manual, con busqueda de cliente y detalle editable.",
            Route = "/facturacion/nota-credito",
            DefaultTargetSelector = "[data-tour='nc.page']",
            Category = "Facturacion",
            Steps = new[]
            {
                new TutorialStep
                {
                    Id = "nc-lookup",
                    Title = "Buscar la factura origen",
                    Description = "Puedes buscar la factura modificada o cambiar a XML/manual segun el caso de emision.",
                    TargetSelector = "[data-tour='nc.lookup']",
                    Padding = 12,
                    CardPlacement = "bottom"
                },
                new TutorialStep
                {
                    Id = "nc-client",
                    Title = "Datos del cliente",
                    Description = "Busca por identificacion o razon social; si el cliente no existe, decide si agregarlo o continuar.",
                    TargetSelector = "[data-tour='nc.client']",
                    Padding = 12,
                    CardPlacement = "bottom"
                },
                new TutorialStep
                {
                    Id = "nc-invoice",
                    Title = "Totales de la nota",
                    Description = "Aqui revisas bases, impuestos y total antes de continuar con la emision.",
                    TargetSelector = "[data-tour='nc.totals-section']",
                    Padding = 12,
                    CardPlacement = "bottom"
                },
                new TutorialStep
                {
                    Id = "nc-detail",
                    Title = "Detalle y totales",
                    Description = "Desde este bloque tienes una fila inicial y las acciones juntas para seleccionar o registrar productos.",
                    TargetSelector = "[data-tour='nc.detail']",
                    Padding = 12,
                    CardPlacement = "top"
                },
                new TutorialStep
                {
                    Id = "nc-save",
                    Title = "Emitir la nota",
                    Description = "Despues de revisar el detalle y el motivo, usa esta accion para guardar la nota de credito y generar su XML.",
                    TargetSelector = "[data-tour='nc.save']",
                    Padding = 10,
                    CardPlacement = "top"
                }
            }
        };

    private static TutorialDefinition BuildNotasCreditoGeneradasTutorial() =>
        new()
        {
            Id = "notas-credito-generadas",
            Title = "Notas de credito generadas",
            Description = "Tutorial para revisar las notas de credito emitidas y consultar su detalle o XML.",
            Route = "/facturacion/notas-credito-generadas",
            DefaultTargetSelector = "[data-tour='ncgen.page']",
            Category = "Facturacion",
            Steps = new[]
            {
                new TutorialStep
                {
                    Id = "ncgen-toolbar",
                    Title = "Resumen del listado",
                    Description = "Esta cabecera explica el objetivo del modulo y deja visible el control principal de paginacion del listado.",
                    TargetSelector = "[data-tour='ncgen.toolbar']",
                    Padding = 12,
                    CardPlacement = "bottom"
                },
                new TutorialStep
                {
                    Id = "ncgen-table",
                    Title = "Documentos emitidos",
                    Description = "La tabla muestra numero de nota, factura modificada, cliente y montos principales de cada comprobante.",
                    TargetSelector = "[data-tour='ncgen.table']",
                    Padding = 12,
                    CardPlacement = "bottom"
                },
                new TutorialStep
                {
                    Id = "ncgen-action",
                    Title = "Acciones por comprobante",
                    Description = "Desde cada fila puedes abrir el detalle de la nota de credito y descargar o consultar su XML.",
                    TargetSelector = "[data-tour='ncgen.action']",
                    FallbackTargetSelector = "[data-tour='ncgen.table']",
                    Padding = 8,
                    CardPlacement = "left"
                }
            }
        };

    private static TutorialDefinition BuildNotaDebitoTutorial() =>
        new()
        {
            Id = "nota-debito-emision",
            Title = "Nota de debito",
            Description = "Recorrido para cargar la factura origen, buscar cliente por identificacion o nombre y emitir la nota de debito.",
            Route = "/facturacion/nota-debito",
            DefaultTargetSelector = "[data-tour='nd.page']",
            Category = "Facturacion",
            Steps = new[]
            {
                new TutorialStep
                {
                    Id = "nd-hero",
                    Title = "Resumen del documento",
                    Description = "Esta cabecera concentra el acceso al historial, el recalculo de numero y las acciones rapidas del formulario.",
                    TargetSelector = "[data-tour='nd.hero']",
                    Padding = 12,
                    CardPlacement = "bottom"
                },
                new TutorialStep
                {
                    Id = "nd-lookup",
                    Title = "Buscar la factura origen",
                    Description = "Empieza buscando la factura que vas a modificar para cargar cliente, serie, secuencial y estado de trabajo.",
                    TargetSelector = "[data-tour='nd.lookup']",
                    Padding = 12,
                    CardPlacement = "bottom"
                },
                new TutorialStep
                {
                    Id = "nd-client-document",
                    Title = "Cliente y documento",
                    Description = "Aqui puedes buscar el cliente desde identificacion o razon social cuando trabajas manualmente.",
                    TargetSelector = "[data-tour='nd.client-document']",
                    Padding = 12,
                    CardPlacement = "bottom"
                },
                new TutorialStep
                {
                    Id = "nd-detail",
                    Title = "Detalle del cargo",
                    Description = "Desde este bloque siempre partes con una fila editable para ingresar el cargo de la nota.",
                    TargetSelector = "[data-tour='nd.detail']",
                    Padding = 12,
                    CardPlacement = "top"
                },
                new TutorialStep
                {
                    Id = "nd-summary",
                    Title = "Totales y guardado",
                    Description = "Revisa el subtotal, IVA y total final antes de guardar la nota de debito y generar el comprobante.",
                    TargetSelector = "[data-tour='nd.summary']",
                    Padding = 10,
                    CardPlacement = "top"
                }
            }
        };

    private static TutorialDefinition BuildNotasDebitoGeneradasTutorial() =>
        new()
        {
            Id = "notas-debito-generadas",
            Title = "Notas de debito generadas",
            Description = "Tutorial para filtrar notas de debito emitidas y consultar su detalle, XML o PDF.",
            Route = "/facturacion/notas-debito-generadas",
            DefaultTargetSelector = "[data-tour='ndgen.page']",
            Category = "Facturacion",
            Steps = new[]
            {
                new TutorialStep
                {
                    Id = "ndgen-toolbar",
                    Title = "Resumen del listado",
                    Description = "Esta franja reune busqueda, rango de fechas, exportacion y refresco del historial de notas de debito.",
                    TargetSelector = "[data-tour='ndgen.toolbar']",
                    Padding = 12,
                    CardPlacement = "bottom"
                },
                new TutorialStep
                {
                    Id = "ndgen-table",
                    Title = "Documentos emitidos",
                    Description = "La tabla muestra fecha, numero de nota, factura modificada, cliente, estado y total de cada comprobante.",
                    TargetSelector = "[data-tour='ndgen.table']",
                    Padding = 12,
                    CardPlacement = "bottom"
                },
                new TutorialStep
                {
                    Id = "ndgen-action",
                    Title = "Acciones por comprobante",
                    Description = "Desde cada fila puedes abrir el detalle de la nota y consultar sus archivos XML o PDF.",
                    TargetSelector = "[data-tour='ndgen.action']",
                    FallbackTargetSelector = "[data-tour='ndgen.table']",
                    Padding = 8,
                    CardPlacement = "left"
                }
            }
        };

    private static TutorialDefinition BuildRetencionesGeneradasTutorial() =>
        new()
        {
            Id = "retenciones-generadas",
            Title = "Retenciones generadas",
            Description = "Recorrido para consultar los comprobantes de retencion emitidos y revisar su sustento.",
            Route = "/facturacion/retenciones-generadas",
            DefaultTargetSelector = "[data-tour='retgen.page']",
            Category = "Compras",
            Steps = new[]
            {
                new TutorialStep
                {
                    Id = "retgen-toolbar",
                    Title = "Resumen del modulo",
                    Description = "Esta franja describe el objetivo de la vista y controla cuantos comprobantes ves por pagina.",
                    TargetSelector = "[data-tour='retgen.toolbar']",
                    Padding = 12,
                    CardPlacement = "bottom"
                },
                new TutorialStep
                {
                    Id = "retgen-table",
                    Title = "Listado de retenciones",
                    Description = "Aqui se concentran numero, fecha, documento sustento, proveedor y valores retenidos de cada registro.",
                    TargetSelector = "[data-tour='retgen.table']",
                    Padding = 12,
                    CardPlacement = "bottom"
                },
                new TutorialStep
                {
                    Id = "retgen-action",
                    Title = "Detalle y XML",
                    Description = "Usa esta accion para abrir el comprobante generado y revisar el XML de la retencion emitida.",
                    TargetSelector = "[data-tour='retgen.action']",
                    FallbackTargetSelector = "[data-tour='retgen.table']",
                    Padding = 8,
                    CardPlacement = "left"
                }
            }
        };

    private static TutorialDefinition BuildMiCajaTutorial() =>
        new()
        {
            Id = "mi-caja-configuracion",
            Title = "Mi caja",
            Description = "Guia para revisar las series operativas de la caja actual y guardar la configuracion activa.",
            Route = "/mi-caja",
            DefaultTargetSelector = "[data-tour='caja.page']",
            Category = "Configuracion",
            Steps = new[]
            {
                new TutorialStep
                {
                    Id = "caja-card",
                    Title = "Panel de caja",
                    Description = "Este panel concentra toda la configuracion de series que usa tu usuario para facturas, notas y compras.",
                    TargetSelector = "[data-tour='caja.card']",
                    Padding = 12,
                    CardPlacement = "bottom"
                },
                new TutorialStep
                {
                    Id = "caja-series",
                    Title = "Series operativas",
                    Description = "Aqui defines la numeracion de factura, notas de credito, guia, compras y debitos de tu caja actual.",
                    TargetSelector = "[data-tour='caja.series']",
                    Padding = 12,
                    CardPlacement = "bottom"
                },
                new TutorialStep
                {
                    Id = "caja-save",
                    Title = "Guardar configuracion",
                    Description = "Despues de validar las series, usa este boton para activar o actualizar la informacion de tu caja.",
                    TargetSelector = "[data-tour='caja.save']",
                    Padding = 10,
                    CardPlacement = "top"
                }
            }
        };

    private static TutorialDefinition BuildCompraDocumentosTutorial() =>
        new()
        {
            Id = "compra-documentos",
            Title = "Compra de documentos",
            Description = "Recorrido para revisar saldo, elegir un paquete, confirmar la recarga y consultar compras recientes.",
            Route = "/compra-documentos",
            DefaultTargetSelector = "[data-tour='recargas.page']",
            Category = "Operacion",
            Steps = new[]
            {
                new TutorialStep
                {
                    Id = "recargas-hero",
                    Title = "Saldo y ultimas compras",
                    Description = "Esta cabecera resume tu saldo actual de documentos, la ultima recarga y el historial registrado.",
                    TargetSelector = "[data-tour='recargas.hero']",
                    Padding = 12,
                    CardPlacement = "bottom"
                },
                new TutorialStep
                {
                    Id = "recargas-plans",
                    Title = "Planes disponibles",
                    Description = "Selecciona un paquete predefinido o ingresa un monto personalizado para calcular documentos disponibles.",
                    TargetSelector = "[data-tour='recargas.plans']",
                    Padding = 12,
                    CardPlacement = "bottom"
                },
                new TutorialStep
                {
                    Id = "recargas-checkout",
                    Title = "Resumen de compra",
                    Description = "Aqui se calcula subtotal, IVA, total a pagar y datos del comprador antes de abrir el checkout.",
                    TargetSelector = "[data-tour='recargas.checkout']",
                    Padding = 12,
                    CardPlacement = "left"
                },
                new TutorialStep
                {
                    Id = "recargas-history",
                    Title = "Historial de recargas",
                    Description = "Este bloque muestra los ultimos movimientos y confirma si el saldo de documentos ya fue aplicado.",
                    TargetSelector = "[data-tour='recargas.history']",
                    Padding = 12,
                    CardPlacement = "top"
                }
            }
        };

    private static TutorialDefinition BuildCategoriasTutorial() =>
        new()
        {
            Id = "categorias-configuracion",
            Title = "Categorias",
            Description = "Recorrido para administrar tipos de producto y subtipos desde la misma pantalla.",
            Route = "/configuracion/categorias",
            DefaultTargetSelector = "[data-tour='categorias.page']",
            Category = "Configuracion",
            Steps = new[]
            {
                new TutorialStep
                {
                    Id = "categorias-tabs",
                    Title = "Tipos y subtipos",
                    Description = "Estas pestanas cambian entre categorias principales y familias o subtipos asociados.",
                    TargetSelector = "[data-tour='categorias.tabs']",
                    Padding = 10,
                    CardPlacement = "bottom"
                },
                new TutorialStep
                {
                    Id = "categorias-toolbar",
                    Title = "Filtro y accion principal",
                    Description = "Desde esta barra cambias el estado visible y abres el formulario para crear un nuevo registro.",
                    TargetSelector = "[data-tour='categorias.toolbar']",
                    Padding = 12,
                    CardPlacement = "bottom"
                },
                new TutorialStep
                {
                    Id = "categorias-table",
                    Title = "Listado configurable",
                    Description = "La tabla cambia segun la pestana activa y muestra las acciones de ver, editar y eliminar.",
                    TargetSelector = "[data-tour='categorias.table']",
                    Padding = 12,
                    CardPlacement = "top"
                }
            }
        };

    private static TutorialDefinition BuildIdentificacionesTutorial() =>
        new()
        {
            Id = "identificaciones-configuracion",
            Title = "Tipos de identificacion",
            Description = "Guia para mantener los documentos oficiales usados por clientes, usuarios y procesos del sistema.",
            Route = "/configuracion/identificaciones",
            DefaultTargetSelector = "[data-tour='identificaciones.page']",
            Category = "Configuracion",
            Steps = new[]
            {
                new TutorialStep
                {
                    Id = "identificaciones-hero",
                    Title = "Contexto del catalogo",
                    Description = "Esta cabecera resume cuantos tipos de identificacion estan activos y para que se usan en la operacion.",
                    TargetSelector = "[data-tour='identificaciones.hero']",
                    Padding = 12,
                    CardPlacement = "bottom"
                },
                new TutorialStep
                {
                    Id = "identificaciones-toolbar",
                    Title = "Busqueda y alta",
                    Description = "Usa esta barra para filtrar por codigo o descripcion y abrir el formulario de un nuevo documento.",
                    TargetSelector = "[data-tour='identificaciones.toolbar']",
                    Padding = 12,
                    CardPlacement = "bottom"
                },
                new TutorialStep
                {
                    Id = "identificaciones-table",
                    Title = "Documentos registrados",
                    Description = "La tabla muestra el catalogo actual y sus acciones de detalle, edicion y eliminacion logica.",
                    TargetSelector = "[data-tour='identificaciones.table']",
                    Padding = 12,
                    CardPlacement = "top"
                }
            }
        };

    private static TutorialDefinition BuildImpuestosTutorial() =>
        new()
        {
            Id = "impuestos-configuracion",
            Title = "Impuestos",
            Description = "Tutorial para administrar codigos fiscales y porcentajes de IVA desde una sola vista.",
            Route = "/configuracion/impuestos",
            DefaultTargetSelector = "[data-tour='impuestos.page']",
            Category = "Configuracion",
            Steps = new[]
            {
                new TutorialStep
                {
                    Id = "impuestos-tabs",
                    Title = "Secciones fiscales",
                    Description = "Estas pestanas alternan entre codigos de impuesto y porcentajes de IVA configurados en el sistema.",
                    TargetSelector = "[data-tour='impuestos.tabs']",
                    Padding = 10,
                    CardPlacement = "bottom"
                },
                new TutorialStep
                {
                    Id = "impuestos-toolbar",
                    Title = "Busqueda y mantenimiento",
                    Description = "Aqui filtras por texto, estado y abres el formulario para crear un impuesto o porcentaje nuevo.",
                    TargetSelector = "[data-tour='impuestos.toolbar']",
                    Padding = 12,
                    CardPlacement = "bottom"
                },
                new TutorialStep
                {
                    Id = "impuestos-table",
                    Title = "Listado tributario",
                    Description = "La tabla concentra las configuraciones fiscales y sus acciones de ver, editar o eliminar.",
                    TargetSelector = "[data-tour='impuestos.table']",
                    Padding = 12,
                    CardPlacement = "top"
                }
            }
        };

    private static TutorialDefinition BuildRetencionesConfigTutorial() =>
        new()
        {
            Id = "retenciones-configuracion",
            Title = "Retenciones",
            Description = "Recorrido para administrar retenciones de IVA, ISD y renta desde el panel fiscal.",
            Route = "/configuracion/retenciones",
            DefaultTargetSelector = "[data-tour='retenciones.page']",
            Category = "Configuracion",
            Steps = new[]
            {
                new TutorialStep
                {
                    Id = "retenciones-hero",
                    Title = "Panel fiscal",
                    Description = "Esta cabecera resume la seccion activa y el volumen de registros cargados en el modulo de retenciones.",
                    TargetSelector = "[data-tour='retenciones.hero']",
                    Padding = 12,
                    CardPlacement = "bottom"
                },
                new TutorialStep
                {
                    Id = "retenciones-tabs",
                    Title = "Tipos de retencion",
                    Description = "Desde aqui cambias entre retenciones de IVA, ISD y renta sin salir de la vista.",
                    TargetSelector = "[data-tour='retenciones.tabs']",
                    Padding = 10,
                    CardPlacement = "bottom"
                },
                new TutorialStep
                {
                    Id = "retenciones-toolbar",
                    Title = "Acciones del modulo",
                    Description = "Esta barra controla la creacion de nuevas retenciones y, en renta, la cantidad de filas visibles.",
                    TargetSelector = "[data-tour='retenciones.toolbar']",
                    Padding = 12,
                    CardPlacement = "bottom"
                },
                new TutorialStep
                {
                    Id = "retenciones-table",
                    Title = "Catalogo tributario",
                    Description = "La tabla muestra las retenciones del tipo seleccionado y sus acciones de consulta, edicion y eliminacion.",
                    TargetSelector = "[data-tour='retenciones.table']",
                    Padding = 12,
                    CardPlacement = "top"
                }
            }
        };

    private static TutorialDefinition BuildMfaTutorial() =>
        new()
        {
            Id = "mfa-configuracion",
            Title = "Autenticacion multifactor",
            Description = "Guia para revisar el estado de MFA y activar o desactivar el segundo factor de seguridad.",
            Route = "/configuracion/mfa",
            DefaultTargetSelector = "[data-tour='mfa.page']",
            Category = "Seguridad",
            Steps = new[]
            {
                new TutorialStep
                {
                    Id = "mfa-hero",
                    Title = "Estado de seguridad",
                    Description = "Esta cabecera resume si MFA esta activo y en que etapa del proceso se encuentra la cuenta actual.",
                    TargetSelector = "[data-tour='mfa.hero']",
                    Padding = 12,
                    CardPlacement = "bottom"
                },
                new TutorialStep
                {
                    Id = "mfa-panel",
                    Title = "Panel de accion",
                    Description = "En este bloque ves el flujo completo de activacion o desactivacion segun el estado actual del usuario.",
                    TargetSelector = "[data-tour='mfa.panel']",
                    Padding = 12,
                    CardPlacement = "bottom"
                },
                new TutorialStep
                {
                    Id = "mfa-primary-action",
                    Title = "Accion principal",
                    Description = "Este boton ejecuta la accion clave del proceso: activar, confirmar o desactivar MFA segun el caso.",
                    TargetSelector = "[data-tour='mfa.primary-action']",
                    Padding = 10,
                    CardPlacement = "left"
                },
                new TutorialStep
                {
                    Id = "mfa-setup",
                    Title = "Vinculacion con autenticador",
                    Description = "Cuando entras al paso de configuracion, aqui escaneas el QR e ingresas el codigo temporal para finalizar.",
                    TargetSelector = "[data-tour='mfa.setup']",
                    Padding = 12,
                    CardPlacement = "top"
                }
            }
        };

    private static TutorialDefinition BuildTutorialesCatalogoTutorial() =>
        new()
        {
            Id = "tutoriales-catalogo",
            Title = "Centro de tutoriales",
            Description = "Recorrido para entender como volver a abrir cualquier tutorial y revisar el estado de tus recorridos.",
            Route = "/ayuda/tutoriales",
            RouteAliases = new[] { "/tutoriales" },
            DefaultTargetSelector = "[data-tour='tutoriales.page']",
            Category = "Ayuda",
            Steps = new[]
            {
                new TutorialStep
                {
                    Id = "tutoriales-grid",
                    Title = "Catalogo de recorridos",
                    Description = "Aqui se listan todos los tutoriales disponibles por modulo junto con su estado actual para tu usuario.",
                    TargetSelector = "[data-tour='tutoriales.grid']",
                    Padding = 12,
                    CardPlacement = "top"
                },
                new TutorialStep
                {
                    Id = "tutoriales-action",
                    Title = "Repetir un tutorial",
                    Description = "Usa este boton para abrir nuevamente el recorrido guiado de una vista cuando quieras repasar el proceso.",
                    TargetSelector = "[data-tour='tutoriales.first-action']",
                    FallbackTargetSelector = "[data-tour='tutoriales.grid']",
                    Padding = 10,
                    CardPlacement = "left"
                }
            }
        };

    private static TutorialDefinition BuildLogsTutorial() =>
        new()
        {
            Id = "logs-acceso",
            Title = "Historial de accesos",
            Description = "Guia para filtrar eventos de seguridad, depurar periodos y revisar el detalle de cada acceso.",
            Route = "/reportes/logs",
            DefaultTargetSelector = "[data-tour='logs.page']",
            Category = "Reportes",
            Steps = new[]
            {
                new TutorialStep
                {
                    Id = "logs-hero",
                    Title = "Resumen del historial",
                    Description = "Esta cabecera resume resultados, accesos exitosos, fallidos y deja visible la accion de borrado por periodo.",
                    TargetSelector = "[data-tour='logs.hero']",
                    Padding = 12,
                    CardPlacement = "bottom"
                },
                new TutorialStep
                {
                    Id = "logs-filters",
                    Title = "Filtros y purgado",
                    Description = "Aqui defines el rango de fechas, aplicas filtros y seleccionas periodos rapidos para depurar registros.",
                    TargetSelector = "[data-tour='logs.filters']",
                    Padding = 12,
                    CardPlacement = "bottom"
                },
                new TutorialStep
                {
                    Id = "logs-table",
                    Title = "Bitacora de eventos",
                    Description = "La tabla muestra fecha, estado, IP, navegador y acciones por cada evento de acceso registrado.",
                    TargetSelector = "[data-tour='logs.table']",
                    Padding = 12,
                    CardPlacement = "bottom"
                },
                new TutorialStep
                {
                    Id = "logs-footer",
                    Title = "Paginacion y volumen",
                    Description = "Este bloque resume cuantos registros ves y te permite navegar entre paginas del historial.",
                    TargetSelector = "[data-tour='logs.footer']",
                    Padding = 10,
                    CardPlacement = "top"
                }
            }
        };

    private static TutorialDefinition BuildNuevaSolicitudTutorial() =>
        new()
        {
            Id = "solicitud-firma",
            Title = "Nueva solicitud de firma",
            Description = "Recorrido para configurar la solicitud, completar datos y adjuntar documentos antes de enviarla.",
            Route = "/solicitud/nueva",
            DefaultTargetSelector = "[data-tour='solicitud.page']",
            Category = "Soporte",
            Steps = new[]
            {
                new TutorialStep
                {
                    Id = "solicitud-hero",
                    Title = "Vista general del flujo",
                    Description = "Esta cabecera explica que la solicitud se completa en dos pasos y resume el contexto del proceso.",
                    TargetSelector = "[data-tour='solicitud.hero']",
                    Padding = 12,
                    CardPlacement = "bottom"
                },
                new TutorialStep
                {
                    Id = "solicitud-step1",
                    Title = "Configuracion inicial",
                    Description = "Primero defines el tipo de firma, la vigencia y el tipo de persona antes de abrir el resto del formulario.",
                    TargetSelector = "[data-tour='solicitud.step1']",
                    Padding = 12,
                    CardPlacement = "bottom"
                },
                new TutorialStep
                {
                    Id = "solicitud-form",
                    Title = "Datos del solicitante",
                    Description = "Aqui completas identificacion, nombres, contacto y direccion del solicitante segun el flujo seleccionado.",
                    TargetSelector = "[data-tour='solicitud.form']",
                    FallbackTargetSelector = "[data-tour='solicitud.step1']",
                    Padding = 12,
                    CardPlacement = "top"
                },
                new TutorialStep
                {
                    Id = "solicitud-uploads",
                    Title = "Documentos del paso 2",
                    Description = "Al avanzar al segundo paso, en esta zona adjuntas la documentacion obligatoria y revisas sus validaciones.",
                    TargetSelector = "[data-tour='solicitud.uploads']",
                    FallbackTargetSelector = "[data-tour='solicitud.page']",
                    Padding = 12,
                    CardPlacement = "top"
                },
                new TutorialStep
                {
                    Id = "solicitud-submit",
                    Title = "Enviar la solicitud",
                    Description = "Desde aqui regresas al paso anterior o finalizas el envio cuando todos los archivos requeridos esten cargados.",
                    TargetSelector = "[data-tour='solicitud.submit']",
                    FallbackTargetSelector = "[data-tour='solicitud.page']",
                    Padding = 10,
                    CardPlacement = "top"
                }
            }
        };

    private static TutorialDefinition BuildAdminSolicitudesTutorial() =>
        new()
        {
            Id = "soporte-solicitudes",
            Title = "Solicitudes de firma",
            Description = "Guia para revisar solicitudes recibidas y abrir la gestion de cada caso desde el panel de soporte.",
            Route = "/soporte/solicitudes",
            DefaultTargetSelector = "[data-tour='soporte.page']",
            Category = "Soporte",
            Steps = new[]
            {
                new TutorialStep
                {
                    Id = "soporte-hero",
                    Title = "Resumen del panel",
                    Description = "Esta cabecera muestra el objetivo de la vista y el total de solicitudes actualmente cargadas.",
                    TargetSelector = "[data-tour='soporte.hero']",
                    Padding = 12,
                    CardPlacement = "bottom"
                },
                new TutorialStep
                {
                    Id = "soporte-table",
                    Title = "Listado de solicitudes",
                    Description = "La tabla concentra fecha, cliente, identificacion, vigencia y estado actual de cada caso.",
                    TargetSelector = "[data-tour='soporte.table']",
                    Padding = 12,
                    CardPlacement = "bottom"
                },
                new TutorialStep
                {
                    Id = "soporte-action",
                    Title = "Gestionar un caso",
                    Description = "Usa esta accion para entrar al detalle de la solicitud y continuar con su revision administrativa.",
                    TargetSelector = "[data-tour='soporte.action']",
                    FallbackTargetSelector = "[data-tour='soporte.table']",
                    Padding = 8,
                    CardPlacement = "left"
                }
            }
        };

    private static TutorialDefinition BuildMisPagosTutorial() =>
        new()
        {
            Id = "mis-pagos-solicitud",
            Title = "Mis pagos",
            Description = "Recorrido para revisar pagos de solicitudes de firma y responder avisos enviados por soporte.",
            Route = "/solicitud/pagos",
            DefaultTargetSelector = "[data-tour='pagos.page']",
            Category = "Soporte",
            Steps = new[]
            {
                new TutorialStep
                {
                    Id = "pagos-header",
                    Title = "Resumen de pagos",
                    Description = "Esta cabecera muestra el total de solicitudes, pagos aprobados y pagos pendientes del usuario.",
                    TargetSelector = "[data-tour='pagos.header']",
                    Padding = 12,
                    CardPlacement = "bottom"
                },
                new TutorialStep
                {
                    Id = "pagos-table",
                    Title = "Solicitudes y estados",
                    Description = "La tabla concentra solicitud, fecha, vigencia, total, estado de pago, estado operativo y avisos de soporte.",
                    TargetSelector = "[data-tour='pagos.table']",
                    FallbackTargetSelector = "[data-tour='pagos.page']",
                    Padding = 12,
                    CardPlacement = "bottom"
                },
                new TutorialStep
                {
                    Id = "pagos-action",
                    Title = "Nueva solicitud",
                    Description = "Desde esta accion puedes iniciar una nueva solicitud cuando necesites generar otra firma electronica.",
                    TargetSelector = "[data-tour='pagos.action']",
                    FallbackTargetSelector = "[data-tour='pagos.page']",
                    Padding = 10,
                    CardPlacement = "top"
                }
            }
        };

    private static TutorialDefinition BuildAsociadosTutorial() =>
        new()
        {
            Id = "asociados-gestion",
            Title = "Gestion de asociados",
            Description = "Guia para revisar colaboradores asociados, aprobar accesos pendientes o rechazar solicitudes.",
            Route = "/admin/asociados",
            DefaultTargetSelector = "[data-tour='asociados.page']",
            Category = "Administracion",
            Steps = new[]
            {
                new TutorialStep
                {
                    Id = "asociados-hero",
                    Title = "Resumen de asociados",
                    Description = "Esta cabecera indica el objetivo de la vista y el total de colaboradores asociados al usuario actual.",
                    TargetSelector = "[data-tour='asociados.hero']",
                    Padding = 12,
                    CardPlacement = "bottom"
                },
                new TutorialStep
                {
                    Id = "asociados-table",
                    Title = "Listado de colaboradores",
                    Description = "La tabla muestra datos de contacto, fecha de registro, estado y acciones disponibles para cada solicitud.",
                    TargetSelector = "[data-tour='asociados.table']",
                    Padding = 12,
                    CardPlacement = "bottom"
                },
                new TutorialStep
                {
                    Id = "asociados-action",
                    Title = "Aprobar o rechazar",
                    Description = "Usa estas acciones para activar el acceso del colaborador o eliminar la solicitud pendiente.",
                    TargetSelector = "[data-tour='asociados.action']",
                    FallbackTargetSelector = "[data-tour='asociados.table']",
                    Padding = 8,
                    CardPlacement = "left"
                }
            }
        };

    private static TutorialDefinition BuildRolesPermisosTutorial() =>
        new()
        {
            Id = "roles-permisos",
            Title = "Roles y permisos",
            Description = "Recorrido guiado para administrar perfiles, modulos y permisos desde el panel de seguridad.",
            Route = "/configuracion/seguridad",
            DefaultTargetSelector = "[data-tour='security.page']",
            Category = "Seguridad",
            UseGlobalHost = false,
            Steps = new[]
            {
                new TutorialStep
                {
                    Id = "security-overview",
                    Title = "Vista general",
                    Description = "Aqui ves el resumen del modulo y los accesos rapidos para crear perfiles o administrar la estructura del menu.",
                    TargetSelector = "[data-tour='security.hero']",
                    Shape = "rounded",
                    Padding = 14,
                    CardPlacement = "bottom"
                },
                new TutorialStep
                {
                    Id = "security-new-master",
                    Title = "Nuevo Menu",
                    Description = "Este boton crea modulos principales o items hijos dentro de la jerarquia del menu.",
                    TargetSelector = "[data-tour='security.new-master-item']",
                    Shape = "rounded",
                    Padding = 8,
                    CardPlacement = "left"
                },
                new TutorialStep
                {
                    Id = "security-new-role",
                    Title = "Nuevo perfil",
                    Description = "Aqui creas un rol nuevo para despues asignarle permisos y rutas disponibles.",
                    TargetSelector = "[data-tour='security.new-role']",
                    Shape = "rounded",
                    Padding = 8,
                    CardPlacement = "left"
                },
                new TutorialStep
                {
                    Id = "security-roles-panel",
                    Title = "Listado de roles",
                    Description = "Este panel contiene todos los perfiles disponibles. Desde aqui eliges el rol sobre el que vas a trabajar.",
                    TargetSelector = "[data-tour='security.roles-panel-header']",
                    Shape = "rounded",
                    Padding = 10,
                    CardPlacement = "right"
                },
                new TutorialStep
                {
                    Id = "security-first-role",
                    Title = "Seleccion de perfil",
                    Description = "Usa este boton para cargar el perfil y trabajar sobre su matriz de permisos.",
                    TargetSelector = "[data-tour='security.first-role-select']",
                    FallbackTargetSelector = "[data-tour='security.roles-panel-header']",
                    Shape = "rounded",
                    Padding = 6,
                    CardPlacement = "right"
                },
                new TutorialStep
                {
                    Id = "security-permissions-panel",
                    Title = "Matriz de permisos",
                    Description = "Cuando un rol esta seleccionado, este panel muestra todos los modulos e items que puedes activar o desactivar.",
                    TargetSelector = "[data-tour='security.permissions-panel-header']",
                    Shape = "rounded",
                    Padding = 10,
                    CardPlacement = "left"
                },
                new TutorialStep
                {
                    Id = "security-first-module",
                    Title = "Permisos por modulo",
                    Description = "Cada modulo agrupa permisos relacionados. Al marcar su casilla, se seleccionan tambien los items hijos.",
                    TargetSelector = "[data-tour='security.first-module-card']",
                    FallbackTargetSelector = "[data-tour='security.permissions-panel-header']",
                    Shape = "rounded",
                    Padding = 10,
                    CardPlacement = "top"
                },
                new TutorialStep
                {
                    Id = "security-save",
                    Title = "Guardar cambios",
                    Description = "Despues de ajustar accesos y rutas, usa este boton para persistir la matriz de permisos del rol.",
                    TargetSelector = "[data-tour='security.save-access']",
                    Shape = "rounded",
                    Padding = 8,
                    CardPlacement = "left"
                }
            }
        };
}
