using Simetric.DTOs;

namespace Simetric.Services;

public sealed class DemoCatalogService
{
    public IReadOnlyList<DemoServiceCard> GetGuestServices() =>
    [
        new(
            "dashboard",
            "Inicio",
            "Resumen visual del negocio con indicadores, actividad reciente y accesos rapidos.",
            "/demo/dashboard",
            "ri-dashboard-line"),
        new(
            "emisor",
            "Emisor",
            "Configuracion fiscal y operativa del emisor en modo solo lectura.",
            "/demo/emisor",
            "ri-building-2-line"),
        new(
            "clientes",
            "Clientes",
            "Cartera comercial con datos de ejemplo y formulario demostrativo.",
            "/demo/clientes",
            "ri-group-line"),
        new(
            "productos",
            "Productos",
            "Catalogo de productos y servicios con filtros y detalle demostrativo.",
            "/demo/productos",
            "ri-box-3-line"),
        new(
            "facturas",
            "Nueva factura",
            "Vista guiada del flujo de emision con cliente, productos, guia de remision opcional, correos y validaciones.",
            "/demo/facturas/nueva",
            "ri-file-add-line"),
        new(
            "documentos",
            "Documentos emitidos",
            "Historial de comprobantes para revisar estados SRI, autorizacion, PDF, XML y seguimiento documental.",
            "/demo/documentos",
            "ri-file-list-3-line"),
        new(
            "cxc",
            "Cuentas por cobrar",
            "Registro de abonos, distribucion por factura y visualizacion de cartera en modo seguro.",
            "/demo/cuentas-por-cobrar",
            "ri-wallet-3-line"),
        new(
            "retenciones",
            "Retenciones",
            "Consulta de comprobantes de retencion con filtros, detalle visual y acciones bloqueadas.",
            "/demo/retenciones-generadas",
            "ri-file-shield-2-line"),
        new(
            "notas-credito",
            "Notas de credito",
            "Ajustes emitidos con vista completa del documento y navegacion de consulta.",
            "/demo/notas-credito-generadas",
            "ri-arrow-go-back-line"),
        new(
            "guias-remision",
            "Guias de remision",
            "Traslados emitidos con destinatario, transportista y soporte visual demostrativo.",
            "/demo/guias-remision-generadas",
            "ri-truck-line"),
        new(
            "recargas",
            "Recargas y planes",
            "Compara planes y calcula en tiempo real el costo de cualquier cantidad de documentos.",
            "/demo/recargas",
            "ri-coins-line",
            "Interactivo"),
        new(
            "soporte",
            "Centro de soporte",
            "Consulta respuestas frecuentes y conoce los canales de ayuda disponibles para usuarios de e-fact.",
            "/demo/soporte",
            "ri-customer-service-2-line",
            "Interactivo")
    ];

    public IReadOnlyList<DemoKpiCard> GetDashboardKpis() =>
    [
        new("Facturas del mes", "128", "+14% frente al mes anterior", "ri-file-chart-line"),
        new("Clientes activos", "47", "12 con movimiento esta semana", "ri-group-line"),
        new("Documentos autorizados", "121", "3 en seguimiento SRI y 2 pendientes de correo", "ri-shield-check-line"),
        new("Ventas estimadas", "$18.420", "Ticket promedio $143,90", "ri-money-dollar-circle-line")
    ];

    public IReadOnlyList<DemoDocumentRow> GetRecentDocuments() =>
    [
        new("FACT-001-000001258", "Factura", "Comercial Andina", "Autorizado", DateTime.Today.AddDays(-1), 325.40m),
        new("FACT-001-000001257", "Factura", "Ferreteria Central", "Autorizado", DateTime.Today.AddDays(-1), 89.99m),
        new("FACT-001-000001256", "Factura", "Distribuidora Nova", "Pendiente SRI", DateTime.Today.AddDays(-2), 560.75m),
        new("NC-001-000000087", "Nota de credito", "Distribuidora Nova", "Autorizado", DateTime.Today.AddDays(-3), 56.75m),
        new("GUIA-001-000000021", "Guia de remision", "Textiles del Norte", "En proceso", DateTime.Today.AddDays(-4), 0m)
    ];

    public IReadOnlyList<DemoWorkflowStep> GetWorkflowSteps() =>
    [
        new("1. Configura emisor y punto de emision", "La cuenta real define firma, establecimiento, secuencias, caja y ambiente de trabajo."),
        new("2. Registra clientes y productos", "Cada comprobante reutiliza catalogos para emitir mas rapido y con menos errores."),
        new("3. Emite el comprobante", "El sistema valida datos, genera XML, controla el envio al SRI y prepara adjuntos para correo."),
        new("4. Revisa estados y soportes", "Desde historial puedes consultar autorizacion, PDF, XML, reenvio por correo y trazabilidad.")
    ];

    public DemoInvoiceDraft GetInvoiceDraft() =>
        new(
            "Comercial Andina S.A.",
            "1790012345001",
            "contabilidad@comercialandina.ec",
            "001-001-000001259",
            DateTime.Today,
            [
                new("Licencia sistema POS", 1, 120m),
                new("Servicio de parametrizacion", 2, 45m),
                new("Capacitacion inicial", 1, 80m)
            ]);

    public IReadOnlyList<DemoClientInsight> GetClientInsights() =>
    [
        new("Comercial Andina S.A.", 12, "$3.840,00"),
        new("Ferreteria Central", 9, "$1.964,50"),
        new("Distribuidora Nova", 7, "$1.220,10")
    ];

    public DashboardSnapshotDto GetDashboardSnapshot(int currentYear)
    {
        var stats = new DashboardStatsDto
        {
            FacturasRegistradas = 128,
            FacturasAutorizadas = 121,
            ClientesActivos = 47,
            ClientesNuevosMes = 6,
            ProductosActivos = 183,
            ProductosFacturables = 169,
            ProductosListosFacturar = 162,
            DocumentosEmitidos = 156,
            DocumentosAutorizados = 148,
            VentasAcumuladas = 18420m,
            VentasAnioActual = 18420m,
            VentasAnioAnterior = 15980m,
            CuentasPorCobrar = 4220m,
            CarteraAlDia = 2850m,
            CarteraVencida = 1370m,
            CobradoHistorico = 14200m,
            OpenReceivablesCount = 11,
            OverdueReceivablesCount = 4,
            TicketPromedio = 143.90m
        };

        var sales = new[]
        {
            (Month: "Ene", Current: 1120m, Previous: 980m),
            (Month: "Feb", Current: 1380m, Previous: 1040m),
            (Month: "Mar", Current: 1440m, Previous: 1190m),
            (Month: "Abr", Current: 1520m, Previous: 1260m),
            (Month: "May", Current: 1690m, Previous: 1380m),
            (Month: "Jun", Current: 1740m, Previous: 1460m),
            (Month: "Jul", Current: 1810m, Previous: 1520m),
            (Month: "Ago", Current: 1660m, Previous: 1490m),
            (Month: "Sep", Current: 1580m, Previous: 1420m),
            (Month: "Oct", Current: 1720m, Previous: 1500m),
            (Month: "Nov", Current: 1890m, Previous: 1660m),
            (Month: "Dic", Current: 1870m, Previous: 1680m)
        };

        var maxSales = sales.Max(x => Math.Max(x.Current, x.Previous));

        return new DashboardSnapshotDto
        {
            LoadedAt = DateTime.Now,
            Stats = stats,
            SalesByMonth = sales
                .Select(x => new DashboardMonthlySalesPointDto
                {
                    MonthLabel = x.Month,
                    CurrentTotal = x.Current,
                    PreviousTotal = x.Previous,
                    CurrentHeightPercent = maxSales <= 0 ? 0 : (int)Math.Round(x.Current / maxSales * 100m),
                    PreviousHeightPercent = maxSales <= 0 ? 0 : (int)Math.Round(x.Previous / maxSales * 100m)
                })
                .ToList(),
            RecentInvoices = new List<DashboardRecentInvoiceItemDto>
            {
                new()
                {
                    DisplayNumber = "001-001-000001258",
                    ClientName = "Comercial Andina S.A.",
                    ClientDocument = "1790012345001",
                    DateLabel = DateTime.Today.AddDays(-1).ToString("dd/MM/yyyy"),
                    RelativeLabel = "Ayer",
                    SriStatusText = "AUTORIZADO",
                    SriStatusClass = "status-pill-success",
                    Total = 325.40m,
                    IsPaid = false,
                    IsOverdue = false
                },
                new()
                {
                    DisplayNumber = "001-001-000001257",
                    ClientName = "Ferreteria Central",
                    ClientDocument = "0999988877001",
                    DateLabel = DateTime.Today.AddDays(-2).ToString("dd/MM/yyyy"),
                    RelativeLabel = "Hace 2 dias",
                    SriStatusText = "AUTORIZADO",
                    SriStatusClass = "status-pill-success",
                    Total = 89.99m,
                    IsPaid = true,
                    IsOverdue = false
                },
                new()
                {
                    DisplayNumber = "001-001-000001256",
                    ClientName = "Distribuidora Nova",
                    ClientDocument = "1791122233001",
                    DateLabel = DateTime.Today.AddDays(-3).ToString("dd/MM/yyyy"),
                    RelativeLabel = "Hace 3 dias",
                    SriStatusText = "PENDIENTE SRI",
                    SriStatusClass = "status-pill-warning",
                    Total = 560.75m,
                    IsPaid = false,
                    IsOverdue = true
                }
            },
            DocumentTypes = new List<DashboardDocumentTypeItemDto>
            {
                new() { Label = "Facturas", Count = 128, AuthorizedCount = 121, SharePercent = 82, FillPercent = 82 },
                new() { Label = "Notas de credito", Count = 9, AuthorizedCount = 8, SharePercent = 6, FillPercent = 6 },
                new() { Label = "Retenciones", Count = 7, AuthorizedCount = 6, SharePercent = 4, FillPercent = 4 },
                new() { Label = "Guias de remision", Count = 6, AuthorizedCount = 6, SharePercent = 4, FillPercent = 4 },
                new() { Label = "Notas de debito", Count = 3, AuthorizedCount = 3, SharePercent = 2, FillPercent = 2 },
                new() { Label = "Liquidaciones", Count = 3, AuthorizedCount = 2, SharePercent = 2, FillPercent = 2 }
            },
            ReceivableCustomers = new List<DashboardReceivableCustomerItemDto>
            {
                new() { ClientName = "Distribuidora Nova", ClientDocument = "1791122233001", OutstandingAmount = 1370m, OpenInvoices = 4, HasOverdueInvoices = true, StatusText = "Con vencidos", FillPercent = 100 },
                new() { ClientName = "Comercial Andina S.A.", ClientDocument = "1790012345001", OutstandingAmount = 980m, OpenInvoices = 3, HasOverdueInvoices = false, StatusText = "Al dia", FillPercent = 72 },
                new() { ClientName = "Servicios Integrales JM", ClientDocument = "0912345678001", OutstandingAmount = 720m, OpenInvoices = 2, HasOverdueInvoices = false, StatusText = "Seguimiento", FillPercent = 53 }
            }
        };
    }

    public ReporteComprobantesCargaDto GetReporteComprobantesDemo()
    {
        var items = new List<ReporteComprobanteItemDto>
        {
            new()
            {
                DocumentoId = 1258,
                TipoDocumento = "Factura",
                TipoDocumentoCodigo = ReporteComprobantesTipos.Factura,
                FechaEmision = DateTime.Today.AddDays(-1),
                NumeroDocumento = "001-001-000001258",
                TerceroNombre = "Comercial Andina S.A.",
                TerceroIdentificacion = "1790012345001",
                TerceroRol = "Cliente",
                EstadoDocumento = "AUTORIZADO",
                BaseImponible = 282.96m,
                Iva = 42.44m,
                Total = 325.40m,
                DocumentoRelacionado = string.Empty,
                ClaveAcceso = "2505202601179001234500120010010000012581234567812",
                NumeroAutorizacion = "2505202601179001234500120010010000012581234567812",
                EstaAutorizado = true,
                ProductosRelacionados = new List<string> { "Licencia sistema POS", "Soporte preferencial" },
                TieneProducto = true,
                TieneServicio = true
            },
            new()
            {
                DocumentoId = 1257,
                TipoDocumento = "Factura",
                TipoDocumentoCodigo = ReporteComprobantesTipos.Factura,
                FechaEmision = DateTime.Today.AddDays(-2),
                NumeroDocumento = "001-001-000001257",
                TerceroNombre = "Ferreteria Central",
                TerceroIdentificacion = "0999988877001",
                TerceroRol = "Cliente",
                EstadoDocumento = "AUTORIZADO",
                BaseImponible = 78.25m,
                Iva = 11.74m,
                Total = 89.99m,
                DocumentoRelacionado = string.Empty,
                ClaveAcceso = "2405202601099998887700120010010000012571234567812",
                NumeroAutorizacion = "2405202601099998887700120010010000012571234567812",
                EstaAutorizado = true,
                ProductosRelacionados = new List<string> { "Herramientas", "Consumibles" },
                TieneProducto = true,
                TieneServicio = false
            },
            new()
            {
                DocumentoId = 1256,
                TipoDocumento = "Factura",
                TipoDocumentoCodigo = ReporteComprobantesTipos.Factura,
                FechaEmision = DateTime.Today.AddDays(-3),
                NumeroDocumento = "001-001-000001256",
                TerceroNombre = "Distribuidora Nova",
                TerceroIdentificacion = "1791122233001",
                TerceroRol = "Cliente",
                EstadoDocumento = "NO AUTORIZADO",
                BaseImponible = 487.61m,
                Iva = 73.14m,
                Total = 560.75m,
                DocumentoRelacionado = string.Empty,
                ClaveAcceso = "2305202601179112223300120010010000012561234567812",
                NumeroAutorizacion = string.Empty,
                EstaAutorizado = false,
                ProductosRelacionados = new List<string> { "Servicio de parametrizacion", "Capacitacion inicial" },
                TieneProducto = false,
                TieneServicio = true
            },
            new()
            {
                DocumentoId = 87,
                TipoDocumento = "Nota de credito",
                TipoDocumentoCodigo = ReporteComprobantesTipos.NotaCredito,
                FechaEmision = DateTime.Today.AddDays(-3),
                NumeroDocumento = "001-001-000000087",
                TerceroNombre = "Distribuidora Nova",
                TerceroIdentificacion = "1791122233001",
                TerceroRol = "Cliente",
                EstadoDocumento = "AUTORIZADO",
                BaseImponible = 49.35m,
                Iva = 7.40m,
                Total = 56.75m,
                DocumentoRelacionado = "001-001-000001240",
                ClaveAcceso = "2305202601179112223300120010010000000871234567812",
                NumeroAutorizacion = "2305202601179112223300120010010000000871234567812",
                EstaAutorizado = true,
                ProductosRelacionados = new List<string> { "Ajuste comercial" },
                TieneProducto = false,
                TieneServicio = true
            },
            new()
            {
                DocumentoId = 311,
                TipoDocumento = "Retencion",
                TipoDocumentoCodigo = ReporteComprobantesTipos.Retencion,
                FechaEmision = DateTime.Today.AddDays(-4),
                NumeroDocumento = "001-001-000000311",
                TerceroNombre = "Servicios Integrales JM",
                TerceroIdentificacion = "0912345678001",
                TerceroRol = "Proveedor",
                EstadoDocumento = "PENDIENTE",
                BaseImponible = 368.20m,
                Iva = 0m,
                Total = 44.18m,
                DocumentoRelacionado = "RET-OC-2026-0311",
                ClaveAcceso = "2205202601091234567800120010010000003111234567812",
                NumeroAutorizacion = string.Empty,
                EstaAutorizado = false,
                ProductosRelacionados = new List<string> { "Servicios profesionales" },
                TieneProducto = false,
                TieneServicio = true
            },
            new()
            {
                DocumentoId = 21,
                TipoDocumento = "Guia de remision",
                TipoDocumentoCodigo = ReporteComprobantesTipos.GuiaRemision,
                FechaEmision = DateTime.Today.AddDays(-5),
                NumeroDocumento = "001-001-000000021",
                TerceroNombre = "Textiles del Norte",
                TerceroIdentificacion = "0998877665001",
                TerceroRol = "Cliente",
                EstadoDocumento = "EN PROCESO",
                BaseImponible = 0m,
                Iva = 0m,
                Total = 0m,
                DocumentoRelacionado = "Pedido TXT-882",
                ClaveAcceso = "2105202601099887766500120010010000000211234567812",
                NumeroAutorizacion = string.Empty,
                EstaAutorizado = false,
                ProductosRelacionados = new List<string> { "Tela drill", "Tela popelina", "Hilos" },
                TieneProducto = true,
                TieneServicio = false
            },
            new()
            {
                DocumentoId = 15,
                TipoDocumento = "Liquidacion de compra",
                TipoDocumentoCodigo = ReporteComprobantesTipos.LiquidacionCompra,
                FechaEmision = DateTime.Today.AddDays(-6),
                NumeroDocumento = "001-001-000000015",
                TerceroNombre = "Juan Carlos Mena",
                TerceroIdentificacion = "1712345678",
                TerceroRol = "Proveedor",
                EstadoDocumento = "AUTORIZADO",
                BaseImponible = 540m,
                Iva = 81m,
                Total = 621m,
                DocumentoRelacionado = "Compra de insumos varios",
                ClaveAcceso = "2005202601171234567800120010010000000151234567812",
                NumeroAutorizacion = "2005202601171234567800120010010000000151234567812",
                EstaAutorizado = true,
                ProductosRelacionados = new List<string> { "Insumos", "Material de empaque" },
                TieneProducto = true,
                TieneServicio = false
            }
        };

        return new ReporteComprobantesCargaDto
        {
            NombreEmisor = "Numerica e-fact Demo",
            RucEmisor = "1799999999001",
            NombreUsuario = "Invitado demo",
            GeneradoEn = DateTime.Now,
            Items = items,
            ClientesDisponibles = items
                .Select(x => x.TerceroNombre)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .OrderBy(x => x)
                .ToList(),
            ProductosDisponibles = items
                .SelectMany(x => x.ProductosRelacionados)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .OrderBy(x => x)
                .ToList(),
            EstadosDisponibles = items
                .Select(x => x.EstadoDocumento)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .OrderBy(x => x)
                .ToList()
        };
    }
}

public sealed record DemoServiceCard(
    string Key,
    string Title,
    string Description,
    string Route,
    string Icon,
    string Badge = "Solo lectura");

public sealed record DemoKpiCard(
    string Title,
    string Value,
    string Hint,
    string Icon);

public sealed record DemoDocumentRow(
    string Number,
    string Type,
    string Customer,
    string Status,
    DateTime Date,
    decimal Total);

public sealed record DemoWorkflowStep(
    string Title,
    string Description);

public sealed record DemoClientInsight(
    string Name,
    int Documents,
    string Revenue);

public sealed class DemoInvoiceDraft
{
    public DemoInvoiceDraft(
        string customerName,
        string identification,
        string email,
        string documentNumber,
        DateTime issueDate,
        IReadOnlyList<DemoInvoiceLine> lines)
    {
        CustomerName = customerName;
        Identification = identification;
        Email = email;
        DocumentNumber = documentNumber;
        IssueDate = issueDate;
        Lines = lines;
    }

    public string CustomerName { get; }
    public string Identification { get; }
    public string Email { get; }
    public string DocumentNumber { get; }
    public DateTime IssueDate { get; }
    public IReadOnlyList<DemoInvoiceLine> Lines { get; }
    public decimal Subtotal => Lines.Sum(x => x.Subtotal);
    public decimal Iva => Math.Round(Subtotal * 0.15m, 2);
    public decimal Total => Subtotal + Iva;
}

public sealed record DemoInvoiceLine(
    string Description,
    decimal Quantity,
    decimal UnitPrice)
{
    public decimal Subtotal => Quantity * UnitPrice;
}
