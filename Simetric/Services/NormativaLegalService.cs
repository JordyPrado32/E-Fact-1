using Microsoft.EntityFrameworkCore;
using Simetric.Data;
using Simetric.Models;

namespace Simetric.Services;

public sealed class NormativaLegalService
{
    private const string UrlResolucion14 = "https://www.sri.gob.ec/o/sri-portlet-biblioteca-alfresco-internet/descargar?id=137046a6-787c-47fb-a2d7-176595d292dc&nombre=NAC-DGERCGC25-00000014.pdf";
    private const string UrlFacturacionElectronica = "https://www.sri.gob.ec/facturacion-electronica";
    private static readonly SemaphoreSlim InitializationLock = new(1, 1);
    private static bool _initialized;
    private readonly IDbContextFactory<AppDbContext> _dbFactory;

    public NormativaLegalService(IDbContextFactory<AppDbContext> dbFactory)
    {
        _dbFactory = dbFactory;
    }

    public async Task<List<NormativaLegal>> ObtenerPublicadasAsync()
    {
        await EnsureInitializedAsync();
        await using var context = await _dbFactory.CreateDbContextAsync();
        return await context.NormativasLegales.AsNoTracking()
            .Where(x => x.Activo)
            .OrderBy(x => x.Orden)
            .ThenBy(x => x.Titulo)
            .ToListAsync();
    }

    public async Task<List<NormativaLegal>> ObtenerTodasAsync()
    {
        await EnsureInitializedAsync();
        await using var context = await _dbFactory.CreateDbContextAsync();
        return await context.NormativasLegales.AsNoTracking()
            .OrderBy(x => x.Orden)
            .ThenBy(x => x.Titulo)
            .ToListAsync();
    }

    public async Task GuardarAsync(NormativaLegal normativa)
    {
        await EnsureInitializedAsync();
        Normalizar(normativa);
        Validar(normativa);

        await using var context = await _dbFactory.CreateDbContextAsync();
        if (normativa.Id == 0)
        {
            normativa.FechaCreacion = DateTime.Now;
            normativa.FechaActualizacion = DateTime.Now;
            context.NormativasLegales.Add(normativa);
        }
        else
        {
            var existente = await context.NormativasLegales.FindAsync(normativa.Id)
                ?? throw new InvalidOperationException("La normativa ya no existe.");

            existente.Codigo = normativa.Codigo;
            existente.Titulo = normativa.Titulo;
            existente.Categoria = normativa.Categoria;
            existente.Resumen = normativa.Resumen;
            existente.Contenido = normativa.Contenido;
            existente.UrlOficial = normativa.UrlOficial;
            existente.EstadoNorma = normativa.EstadoNorma;
            existente.FechaPublicacion = normativa.FechaPublicacion;
            existente.FechaVigencia = normativa.FechaVigencia;
            existente.FechaUltimaVerificacion = normativa.FechaUltimaVerificacion;
            existente.Activo = normativa.Activo;
            existente.Orden = normativa.Orden;
            existente.FechaActualizacion = DateTime.Now;
        }

        await context.SaveChangesAsync();
    }

    public async Task AlternarEstadoAsync(int id)
    {
        await EnsureInitializedAsync();
        await using var context = await _dbFactory.CreateDbContextAsync();
        var normativa = await context.NormativasLegales.FindAsync(id)
            ?? throw new InvalidOperationException("La normativa ya no existe.");
        normativa.Activo = !normativa.Activo;
        normativa.FechaActualizacion = DateTime.Now;
        await context.SaveChangesAsync();
    }

    private async Task EnsureInitializedAsync()
    {
        if (_initialized)
            return;

        await InitializationLock.WaitAsync();
        try
        {
            if (_initialized)
                return;

            await using var context = await _dbFactory.CreateDbContextAsync();
            await context.Database.ExecuteSqlRawAsync("""
IF OBJECT_ID(N'dbo.NORMATIVA_LEGAL', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.NORMATIVA_LEGAL
    (
        Id int IDENTITY(1,1) NOT NULL CONSTRAINT PK_NORMATIVA_LEGAL PRIMARY KEY,
        Codigo nvarchar(120) NOT NULL,
        Titulo nvarchar(300) NOT NULL,
        Categoria nvarchar(120) NOT NULL,
        Resumen nvarchar(1000) NOT NULL CONSTRAINT DF_NORMATIVA_LEGAL_Resumen DEFAULT(''),
        Contenido nvarchar(max) NOT NULL,
        UrlOficial nvarchar(1000) NULL,
        EstadoNorma nvarchar(40) NOT NULL CONSTRAINT DF_NORMATIVA_LEGAL_Estado DEFAULT('Vigente'),
        FechaPublicacion datetime2 NULL,
        FechaVigencia datetime2 NULL,
        FechaUltimaVerificacion datetime2 NULL,
        Activo bit NOT NULL CONSTRAINT DF_NORMATIVA_LEGAL_Activo DEFAULT(1),
        Orden int NOT NULL CONSTRAINT DF_NORMATIVA_LEGAL_Orden DEFAULT(0),
        FechaCreacion datetime2 NOT NULL CONSTRAINT DF_NORMATIVA_LEGAL_Creacion DEFAULT(GETDATE()),
        FechaActualizacion datetime2 NOT NULL CONSTRAINT DF_NORMATIVA_LEGAL_Actualizacion DEFAULT(GETDATE())
    );
    CREATE UNIQUE INDEX UX_NORMATIVA_LEGAL_Codigo ON dbo.NORMATIVA_LEGAL(Codigo);
END
""");

            await SembrarContenidoInicialAsync(context);
            _initialized = true;
        }
        finally
        {
            InitializationLock.Release();
        }
    }

    private static async Task SembrarContenidoInicialAsync(AppDbContext context)
    {
        var existentes = await context.NormativasLegales
            .Select(x => x.Codigo)
            .ToListAsync();
        var codigos = existentes.ToHashSet(StringComparer.OrdinalIgnoreCase);
        var nuevas = CrearContenidoInicial().Where(x => !codigos.Contains(x.Codigo)).ToList();

        if (nuevas.Count == 0)
            return;

        context.NormativasLegales.AddRange(nuevas);
        await context.SaveChangesAsync();
    }

    private static IEnumerable<NormativaLegal> CrearContenidoInicial()
    {
        var verificado = new DateTime(2026, 7, 30);
        yield return Crear(
            "NAC-DGERCGC25-00000014",
            "Anulación de comprobantes electrónicos",
            "Facturación electrónica",
            "Regula la anulación de comprobantes de venta, retención y documentos complementarios electrónicos. Fue reformada por la Resolución NAC-DGERCGC25-00000017.",
            """
Art. 1.- Los comprobantes electrónicos con errores o emitidos por operaciones que no se produjeron pueden anularse.

Art. 2.- Los comprobantes de venta pueden anularse en línea o mediante nota de crédito. Los comprobantes de retención y documentos complementarios se anulan únicamente en línea, desde los servicios del SRI.

Art. 3.- La anulación en línea puede solicitarse hasta el día 7 del mes siguiente a la emisión; si coincide con feriado o fin de semana, se traslada al siguiente día hábil. Vencido el plazo, los comprobantes de venta se corrigen mediante nota de crédito. Las facturas emitidas como consumidor final no pueden anularse ni modificarse con nota de crédito una vez transmitidas al SRI.

Art. 4.- La anulación de retenciones, notas de crédito y notas de débito requiere aceptación del receptor dentro de cinco días hábiles, salvo las excepciones previstas para identificaciones del exterior, pasaportes o personas fallecidas.

Art. 5.- Las notas de crédito solo pueden emitirse en los casos previstos por la normativa.

Disposiciones relevantes: las facturas comerciales negociables y los comprobantes que sustenten devoluciones de impuestos no pueden anularse. Las solicitudes masivas de más de 1.000 comprobantes deben seguir el procedimiento definido por el SRI.

Aplicación: disposiciones principales desde el 1 de agosto de 2025 y reglas específicas desde el 1 de enero de 2026.
""",
            UrlResolucion14, 1, new DateTime(2025, 6, 27), new DateTime(2025, 8, 1), verificado);

        yield return Crear(
            "RCV-RDC-ART-6-8",
            "Vigencia, suspensión y obligación de emitir comprobantes",
            "Comprobantes",
            "Artículos 6, 7 y 8 del Reglamento de Comprobantes de Venta, Retención y Documentos Complementarios.",
            """
Art. 6.- El SRI puede conceder autorización para comprobantes por un plazo de hasta un año, siempre que el sujeto pasivo mantenga sus obligaciones tributarias al día y cumpla las condiciones reglamentarias.

Art. 7.- La autorización puede suspenderse cuando se incumplan las obligaciones tributarias o las condiciones que dieron lugar a su otorgamiento.

Art. 8.- Las personas naturales, sociedades y demás sujetos señalados por la normativa deben emitir y entregar comprobantes de venta por las transferencias de bienes o prestaciones de servicios que realicen.
""",
            UrlFacturacionElectronica, 2, null, null, verificado);

        yield return Crear(
            "RCV-RDC-ART-11",
            "Emisión y contenido de facturas",
            "Facturas",
            "Condiciones generales para emitir facturas y detallar impuestos, consumidor final y exportaciones.",
            """
Art. 11.- Se emitirán facturas para respaldar transferencias de bienes, prestación de servicios y otras operaciones gravadas. Deben identificar al adquirente cuando corresponda y desglosar los impuestos aplicables.

En operaciones con consumidor final se aplican los límites y requisitos reglamentarios. En exportaciones deben constar los datos necesarios de la operación y del adquirente del exterior.
""",
            UrlFacturacionElectronica, 3, null, null, verificado);

        yield return Crear(
            "RCV-RDC-ART-13",
            "Liquidaciones de compra de bienes y prestación de servicios",
            "Liquidaciones de compra",
            "Casos en los que procede emitir una liquidación de compra y obligaciones asociadas.",
            """
Art. 13.- Las liquidaciones de compra se emiten en los casos expresamente previstos por el reglamento, entre ellos adquisiciones a personas que no se encuentran obligadas a emitir comprobantes o determinados servicios prestados desde el exterior.

El emisor debe identificar al proveedor, detallar la operación y efectuar las retenciones que correspondan. La liquidación sustenta costos, gastos y crédito tributario únicamente cuando cumple todos los requisitos legales.
""",
            UrlFacturacionElectronica, 4, null, null, verificado);

        yield return Crear(
            "RCV-RDC-ART-15",
            "Notas de crédito",
            "Notas de crédito",
            "Reglas para anular operaciones, aceptar devoluciones y conceder descuentos o bonificaciones.",
            """
Art. 15.- Las notas de crédito se emiten para anular operaciones, aceptar devoluciones y conceder descuentos o bonificaciones. Deben identificar el comprobante que modifican, señalar su fecha y contener los datos del adquirente.

No procede emitir notas de crédito respecto de facturas comerciales negociables cuando se afecten derechos de terceros, salvo los casos permitidos por la normativa.
""",
            UrlFacturacionElectronica, 5, null, null, verificado);

        yield return Crear(
            "RCV-RDC-ART-16",
            "Notas de débito",
            "Notas de débito",
            "Reglas para recuperar intereses, costos y gastos posteriores a la emisión del comprobante.",
            """
Art. 16.- Las notas de débito se emiten para cobrar intereses de mora y recuperar costos y gastos incurridos por el vendedor después de la emisión del comprobante de venta.

Deben identificar la factura o comprobante relacionado y no pueden modificar facturas comerciales negociables en perjuicio de terceros, salvo los casos autorizados.
""",
            UrlFacturacionElectronica, 6, null, null, verificado);

        yield return Crear(
            "RCV-RDC-ART-17",
            "Oportunidad de entrega de comprobantes",
            "Entrega de documentos",
            "Define cuándo deben emitirse y entregarse los comprobantes según el tipo de operación.",
            """
Art. 17.- Los comprobantes deben emitirse y entregarse al momento de la transferencia del bien o de la prestación del servicio.

El reglamento contempla reglas particulares para ventas por medios electrónicos o telefónicos, acuerdos de débito, transferencias de inmuebles, contratos por etapas, servicios continuos y otras operaciones cuya ejecución o pago ocurre en distintos momentos.
""",
            UrlFacturacionElectronica, 7, null, null, verificado);

        yield return Crear(
            "RCV-RDC-ART-18-19",
            "Requisitos de impresión y llenado",
            "Requisitos y características",
            "Datos obligatorios de los comprobantes y reglas para completar la información de la operación.",
            """
Art. 18.- Entre los requisitos preimpresos constan el número de autorización, RUC, razón social o nombres, denominación del documento, numeración de quince dígitos, dirección del establecimiento y fecha de caducidad, cuando sean aplicables.

Art. 19.- El llenado debe incluir identificación y nombre del adquirente, descripción de los bienes o servicios, precios unitarios, subtotal, descuentos, impuestos, propina, ICE o ISD cuando correspondan, importe total, moneda, fecha de emisión y referencias a guías de remisión.
""",
            UrlFacturacionElectronica, 8, null, null, verificado);
    }

    private static NormativaLegal Crear(
        string codigo,
        string titulo,
        string categoria,
        string resumen,
        string contenido,
        string url,
        int orden,
        DateTime? publicacion,
        DateTime? vigencia,
        DateTime verificado)
        => new()
        {
            Codigo = codigo,
            Titulo = titulo,
            Categoria = categoria,
            Resumen = resumen,
            Contenido = contenido.Trim(),
            UrlOficial = url,
            EstadoNorma = "Vigente",
            Orden = orden,
            FechaPublicacion = publicacion,
            FechaVigencia = vigencia,
            FechaUltimaVerificacion = verificado,
            Activo = true
        };

    private static void Normalizar(NormativaLegal normativa)
    {
        normativa.Codigo = normativa.Codigo.Trim();
        normativa.Titulo = normativa.Titulo.Trim();
        normativa.Categoria = normativa.Categoria.Trim();
        normativa.Resumen = normativa.Resumen?.Trim() ?? string.Empty;
        normativa.Contenido = normativa.Contenido.Trim();
        normativa.UrlOficial = string.IsNullOrWhiteSpace(normativa.UrlOficial) ? null : normativa.UrlOficial.Trim();
        normativa.EstadoNorma = string.IsNullOrWhiteSpace(normativa.EstadoNorma) ? "Vigente" : normativa.EstadoNorma.Trim();
    }

    private static void Validar(NormativaLegal normativa)
    {
        if (string.IsNullOrWhiteSpace(normativa.Codigo) ||
            string.IsNullOrWhiteSpace(normativa.Titulo) ||
            string.IsNullOrWhiteSpace(normativa.Categoria) ||
            string.IsNullOrWhiteSpace(normativa.Contenido))
        {
            throw new InvalidOperationException("Código, título, categoría y contenido son obligatorios.");
        }

        if (!string.IsNullOrWhiteSpace(normativa.UrlOficial) &&
            (!Uri.TryCreate(normativa.UrlOficial, UriKind.Absolute, out var uri) ||
             (uri.Scheme != Uri.UriSchemeHttps && uri.Scheme != Uri.UriSchemeHttp)))
        {
            throw new InvalidOperationException("La URL oficial no es válida.");
        }
    }
}
