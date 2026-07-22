using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;
using BarcodeStandard;
using Simetric.DTOs;
using Simetric.Models;
using System.Collections.Generic;
using System.Globalization;
using SkiaSharp;

namespace Simetric.Services;

public interface IFacturaPdfService
{
    Task<string> GenerarPdfFacturaAsync(FacturaViewDto facturaView, FormatoImpresionDocumento formato = FormatoImpresionDocumento.A4);
    Task<string> GenerarPdfFacturaTemporalAsync(FacturaViewDto facturaView, FormatoImpresionDocumento formato = FormatoImpresionDocumento.A4);
    Task<bool> EliminarPdfFacturaTemporalAsync(string rutaRelativaPdf);
}

public sealed class FacturaPdfService : IFacturaPdfService
{
    private static readonly CultureInfo Cultura = new("es-EC");
    private const float FuenteBasePdf = 8.2f;
    private const float FuenteEtiquetaPdf = 7.2f;
    private const float FuenteTituloSeccionPdf = 10.6f;
    private const float PaddingBloquePdf = 7f;
    private const float SpacingBloquePdf = 3f;
    private const float AlturaMinimaInfoEmisorPdf = 180f;
    private readonly IWebHostEnvironment _environment;

    public FacturaPdfService(IWebHostEnvironment environment)
    {
        _environment = environment;
    }

    public Task<string> GenerarPdfFacturaAsync(FacturaViewDto facturaView, FormatoImpresionDocumento formato = FormatoImpresionDocumento.A4)
    {
        if (facturaView?.Factura == null)
            throw new InvalidOperationException("No se encontro la informacion necesaria para generar el PDF de la factura.");

        var carpetaFacturas = Path.Combine(ObtenerWebRootPath(), "FacturasGeneradas");
        Directory.CreateDirectory(carpetaFacturas);

        var ruc = LimpiarSegmentoArchivo(facturaView.Emisor?.Ruc, "factura");
        var serie = LimpiarSegmentoArchivo(facturaView.Factura.Serie, "001001");
        var numero = LimpiarSegmentoArchivo(facturaView.Factura.Numfactura, facturaView.Factura.Codfactura.ToString());
        var rutaPdf = Path.Combine(carpetaFacturas, $"{ruc}_{serie}_{numero}{formato.ObtenerSufijoArchivo()}.pdf");

        var logoSistema = CargarLogoDocumento(facturaView.Emisor);
        var lineas = ConstruirLineas(facturaView);

        QuestPDF.Fluent.Document.Create(container =>
        {
            container.Page(page =>
            {
                if (formato.EsTermico())
                {
                    ThermalPdfComposer.ConfigurePage(page, formato);
                    page.Content().Element(content => ThermalPdfComposer.ComposeTicket(content, ConstruirTicketTermico(facturaView, lineas)));
                }
                else
                {
                    page.Size(PageSizes.A4);
                    page.Margin(0.6f, Unit.Centimetre);
                    page.PageColor(Colors.White);
                    page.DefaultTextStyle(EstiloBasePdf);

                    page.Header().Element(header => ComponerEncabezado(header, facturaView, logoSistema));
                    page.Content().Element(content => ComponerContenido(content, facturaView, lineas));
                    page.Footer().Element(footer => ComponerPie(footer));
                }
            });
        }).GeneratePdf(rutaPdf);

        return Task.FromResult(rutaPdf);
    }

    public Task<string> GenerarPdfFacturaTemporalAsync(FacturaViewDto facturaView, FormatoImpresionDocumento formato = FormatoImpresionDocumento.A4)
    {
        if (facturaView?.Factura == null)
            throw new InvalidOperationException("No se encontro la informacion necesaria para generar el PDF de la factura.");

        var carpetaTemporal = Path.Combine(ObtenerWebRootPath(), "FacturasGeneradas", "tmp");
        Directory.CreateDirectory(carpetaTemporal);

        var nombreTemporal = $"{Guid.NewGuid():N}_{LimpiarSegmentoArchivo(facturaView.Factura.Numfactura, facturaView.Factura.Codfactura.ToString())}{formato.ObtenerSufijoArchivo()}.pdf";
        var rutaPdf = Path.Combine(carpetaTemporal, nombreTemporal);

        GenerarPdfFacturaInterno(facturaView, formato, rutaPdf);

        var urlRelativa = $"/FacturasGeneradas/tmp/{nombreTemporal}";
        return Task.FromResult(urlRelativa);
    }

    public Task<bool> EliminarPdfFacturaTemporalAsync(string rutaRelativaPdf)
    {
        if (string.IsNullOrWhiteSpace(rutaRelativaPdf))
            return Task.FromResult(false);

        var rutaNormalizada = rutaRelativaPdf.Replace('\\', '/').TrimStart('/');
        var rutaFisica = Path.Combine(ObtenerWebRootPath(), rutaNormalizada.Replace('/', Path.DirectorySeparatorChar));
        if (!rutaFisica.StartsWith(Path.Combine(ObtenerWebRootPath(), "FacturasGeneradas"), StringComparison.OrdinalIgnoreCase))
            return Task.FromResult(false);

        if (File.Exists(rutaFisica))
        {
            File.Delete(rutaFisica);
            return Task.FromResult(true);
        }

        return Task.FromResult(false);
    }

    private void GenerarPdfFacturaInterno(FacturaViewDto facturaView, FormatoImpresionDocumento formato, string rutaPdf)
    {
        var logoSistema = CargarLogoDocumento(facturaView.Emisor);
        var lineas = ConstruirLineas(facturaView);

        QuestPDF.Fluent.Document.Create(container =>
        {
            container.Page(page =>
            {
                if (formato.EsTermico())
                {
                    ThermalPdfComposer.ConfigurePage(page, formato);
                    page.Content().Element(content => ThermalPdfComposer.ComposeTicket(content, ConstruirTicketTermico(facturaView, lineas)));
                }
                else
                {
                    page.Size(PageSizes.A4);
                    page.Margin(0.6f, Unit.Centimetre);
                    page.PageColor(Colors.White);
                    page.DefaultTextStyle(EstiloBasePdf);

                    page.Header().Element(header => ComponerEncabezado(header, facturaView, logoSistema));
                    page.Content().Element(content => ComponerContenido(content, facturaView, lineas));
                    page.Footer().Element(footer => ComponerPie(footer));
                }
            });
        }).GeneratePdf(rutaPdf);
    }

    private string ObtenerWebRootPath()
    {
        if (!string.IsNullOrWhiteSpace(_environment.WebRootPath))
            return _environment.WebRootPath;

        return Path.Combine(Directory.GetCurrentDirectory(), "wwwroot");
    }

    private byte[]? CargarLogoDocumento(Emisor? emisor)
    {
        return CargarLogoEmisor(emisor?.LogoImagen);
    }

    private byte[]? CargarLogoEmisor(string? logoImagen)
    {
        if (string.IsNullOrWhiteSpace(logoImagen))
            return null;

        var logo = logoImagen.Trim();

        const string jpegPrefix = "data:image/jpeg;base64,";
        const string jpgPrefix = "data:image/jpg;base64,";
        const string pngPrefix = "data:image/png;base64,";
        const string webpPrefix = "data:image/webp;base64,";

        if (logo.StartsWith(jpegPrefix, StringComparison.OrdinalIgnoreCase)
            || logo.StartsWith(jpgPrefix, StringComparison.OrdinalIgnoreCase)
            || logo.StartsWith(pngPrefix, StringComparison.OrdinalIgnoreCase)
            || logo.StartsWith(webpPrefix, StringComparison.OrdinalIgnoreCase))
        {
            var commaIndex = logo.IndexOf(',');
            if (commaIndex > -1 && commaIndex < logo.Length - 1)
            {
                try
                {
                    return Convert.FromBase64String(logo[(commaIndex + 1)..]);
                }
                catch
                {
                    return null;
                }
            }
        }

        var normalized = logo.Replace('/', Path.DirectorySeparatorChar).TrimStart(Path.DirectorySeparatorChar);
        var candidatePaths = new[]
        {
            Path.Combine(ObtenerWebRootPath(), normalized),
            Path.Combine(Directory.GetCurrentDirectory(), normalized),
            logo
        };

        foreach (var candidate in candidatePaths.Where(File.Exists))
            return File.ReadAllBytes(candidate);

        return null;
    }

    private static List<FacturaPdfLinea> ConstruirLineas(FacturaViewDto facturaView)
    {
        var detalles = facturaView.Detalles ?? new List<Detallefactura>();

        return detalles.Select((detalle, index) =>
        {
            var bruto = detalle.Cantproducto * detalle.Precioproducto;
            var descuentoValor = Math.Clamp(detalle.Descuento ?? 0m, 0m, Math.Max(0m, bruto));
            var descuentoPct = bruto > 0m ? descuentoValor / bruto : 0m;

            return new FacturaPdfLinea
            {
                Item = index + 1,
                Codigo = !string.IsNullOrWhiteSpace(detalle.Codprincipal)
                    ? detalle.Codprincipal!.Trim()
                    : $"PROD-{detalle.Codproducto}",
                Descripcion = string.IsNullOrWhiteSpace(detalle.Descripproducto) ? "Producto / servicio" : FormatearTextoCasing(detalle.Descripproducto),
                Cantidad = detalle.Cantproducto,
                PrecioUnitario = detalle.Precioproducto,
                DescuentoPorcentaje = descuentoPct,
                DescuentoValor = descuentoValor,
                BaseImponible = detalle.Valortproducto,
                ValorIva = detalle.Valoriva,
                TarifaIva = detalle.Tarifa,
                Total = detalle.Valortotal
            };
        }).ToList();
    }

    private static decimal NormalizarPorcentajeDescuento(decimal? descuento)
    {
        if (!descuento.HasValue || descuento.Value <= 0)
            return 0m;

        return descuento.Value > 1m ? descuento.Value / 100m : descuento.Value;
    }

    private static TextStyle EstiloBasePdf(TextStyle style)
        => style
            .FontFamily("Arial")
            .FontSize(FuenteBasePdf)
            .LineHeight(1.1f);

    private static void ComponerEncabezado(IContainer container, FacturaViewDto facturaView, byte[]? logoSistema)
    {
        var factura = facturaView.Factura;
        var emisor = facturaView.Emisor;
        var estaAutorizada = DocumentoAutorizacionHelper.EstaAutorizado(factura.Autorizado, factura.Estadoenviosri);
        var numeroDocumento = ObtenerNumeroDocumento(factura);
        var numeroAutorizacion = DocumentoAutorizacionHelper.ObtenerNumeroAutorizacionVisual(
            factura.Estadoenviosri,
            estaAutorizada,
            factura.Numautorizacion);
        var claveAcceso = ObtenerTextoOGuion(factura.Codclave);
        var ambiente = ObtenerAmbienteVisual(factura.Ambiente, emisor?.TipoAmbiente);
        var tipoEmision = ObtenerTipoEmisionVisualFromEmisor(emisor?.TipoEmision);
        var estadoDocumento = estaAutorizada ? "Autorizada" : "Emitida";

        container.PaddingBottom(6).Row(row =>
        {
            row.Spacing(10);
            row.RelativeItem().Column(column =>
            {
                var tieneLogoEmisor = !string.IsNullOrWhiteSpace(emisor?.LogoImagen);

                if (logoSistema != null)
                {
                    column.Item()
                        .AlignCenter()
                        .MaxWidth(92)
                        .PaddingBottom(4)
                        .Image(logoSistema)
                        .FitWidth();
                }
                else
                {
                    column.Item()
                        .AlignCenter()
                        .PaddingBottom(4)
                        .Text("NO TIENE LOGO")
                        .FontFamily("Arial")
                        .Bold()
                        .FontSize(28f)
                        .FontColor(Colors.Red.Medium);
                }

                column.Item()
                    .Border(1)
                    .BorderColor(Colors.Blue.Lighten4)
                    .Background(Colors.White)
                    .MinHeight(AlturaMinimaInfoEmisorPdf)
                    .Padding(PaddingBloquePdf)
                    .Column(info =>
                    {
                        info.Spacing(2);

                        info.Item()
                            .Background(Colors.Blue.Lighten5)
                            .PaddingVertical(2)
                            .PaddingHorizontal(5)
                            .Text("Facturación Electrónica")
                            .FontSize(FuenteEtiquetaPdf)
                            .SemiBold()
                            .FontColor(Colors.Blue.Darken3);

                        info.Item().Text(FormatearTextoCasing(emisor?.RazonSocial ?? "Emisor"))
                            .FontSize(13f)
                            .SemiBold()
                            .FontColor(Colors.Blue.Darken3);

                        if (!string.IsNullOrWhiteSpace(emisor?.Ruc))
                        {
                            info.Item().Element(item => ComponerLineaEncabezado(item, "RUC:", emisor.Ruc));
                        }

                        if (!string.IsNullOrWhiteSpace(emisor?.DireccionMatriz))
                        {
                            info.Item().Element(item => ComponerLineaEncabezado(
                                item,
                                "Direccion matriz:",
                                FormatearTextoCasing(emisor.DireccionMatriz)));
                        }

                if (!string.IsNullOrWhiteSpace(emisor?.Telefono))
                {
                    info.Item().Element(item => ComponerLineaEncabezado(item, "Telefono:", emisor.Telefono));
                }

                info.Item().Element(item => ComponerLineaEncabezado(
                    item,
                    "Fecha emision:",
                    ObtenerFechaEmisionFactura(factura).ToString("dd/MM/yyyy")));
            });
            });

            row.RelativeItem()
                .Border(1)
                .BorderColor(Colors.Blue.Lighten3)
                .Background(Colors.Blue.Lighten5)
                .Padding(PaddingBloquePdf)
                .Column(column =>
                {
                    column.Spacing(2);

                    column.Item().AlignCenter().Text(text =>
                    {
                        text.DefaultTextStyle(x => x.FontSize(10.2f).SemiBold());
                        text.Span("Factura").FontColor(Colors.Blue.Darken3).FontSize(12.2f);
                        text.Span($"  No. {numeroDocumento}").FontColor(Colors.Black);
                    });

                    column.Item().Element(item => ComponerLineaEncabezado(item, "Estado del documento:", estadoDocumento));
                    column.Item().PaddingTop(2).Element(item => ComponerCajaDatoEncabezado(item, "Clave de acceso", ObtenerTextoOGuion(claveAcceso)));
                    column.Item().PaddingTop(2).Element(item => ComponerCajaDatoEncabezado(item, "Número de autorización", ObtenerTextoOGuion(numeroAutorizacion)));

                    if (factura.Fchautorizacion.HasValue)
                        column.Item().PaddingTop(2).Text($"Fecha y hora de autorización: {factura.Fchautorizacion.Value:dd/MM/yyyy HH:mm}")
                            .FontSize(8);

                    column.Item().PaddingTop(2).Element(item => ComponerLineaEncabezado(item, "Ambiente:", ambiente));

                    column.Item().Element(item => ComponerLineaEncabezado(item, "Tipo emisión:", tipoEmision));

                    var barcodeBytes = GenerarBarcodeNumeroAutorizacion(factura.Codclave);
                    if (barcodeBytes != null)
                    {
                        column.Item().PaddingTop(4)
                            .Border(1)
                            .BorderColor(Colors.Blue.Lighten3)
                            .Background(Colors.White)
                            .Padding(5)
                            .AlignCenter()
                            .MaxWidth(220)
                            .Image(barcodeBytes)
                            .FitWidth();
                    }
                });
        });
    }

    private static void ComponerContenido(IContainer container, FacturaViewDto facturaView, IReadOnlyCollection<FacturaPdfLinea> lineas)
    {
        var factura = facturaView.Factura;
        var cliente = facturaView.Cliente;
        var emisor = facturaView.Emisor;
        var descuentoGlobalPct = factura.DescuentoGlobalPct ?? 0m;
        var subtotal = factura.Subtotal ?? lineas.Sum(x => x.BaseImponible);
        var descuentos = factura.Descuentos ?? lineas.Sum(x => x.DescuentoValor);
        var iva = factura.Iva ?? lineas.Sum(x => x.ValorIva);
        var total = factura.Valortotal ?? lineas.Sum(x => x.Total);
        var subtotal0 = factura.Subtotal0 ?? 0m;
        var subtotal12 = factura.Subtotal12 ?? subtotal;
        var subtotalNoObjeto = factura.Noimp ?? 0m;
        var subtotalExento = factura.Exiva ?? 0m;
        var ice = factura.Valorice ?? 0m;
        var irbpnr = factura.ValorbiIrbpnr ?? factura.BiIrbpnr ?? 0m;

        container.Column(column =>
        {
            column.Spacing(4);

            column.Item().Element(card => ComponerBloqueCliente(card, cliente));

            column.Item().Element(table => ComponerDetalle(table, lineas));

            column.Item().ShowEntire().Row(row =>
            {
                row.Spacing(8);
                row.RelativeItem(0.95f).Element(card => ComponerBloquePago(card, facturaView));

                row.RelativeItem(1.25f).Element(card => ComponerBloqueResumen(
                    card,
                    factura,
                    subtotal0,
                    subtotal12,
                    subtotalNoObjeto,
                    subtotalExento,
                    subtotal,
                    descuentos,
                    ice,
                    iva,
                    irbpnr,
                    total,
                    lineas));
            });

            column.Item().PaddingTop(1).Text("Documento generado por el sistema de facturacion electronica.")
                .FontSize(FuenteEtiquetaPdf)
                .FontColor(Colors.Grey.Darken1);
        });
    }

    private static void ComponerBloqueDocumento(IContainer container, FacturaViewDto facturaView)
    {
        var factura = facturaView.Factura;
        var emisor = facturaView.Emisor;

        container.Border(1)
            .BorderColor(Colors.Blue.Lighten4)
            .Background(Colors.Blue.Lighten5)
            .Padding(PaddingBloquePdf)
            .Column(column =>
            {
                column.Spacing(SpacingBloquePdf);
                column.Item().Text("Resumen del comprobante")
                    .FontSize(FuenteTituloSeccionPdf)
                    .SemiBold()
                    .FontColor(Colors.Blue.Darken3);

                column.Item().Row(row =>
                {
                    row.RelativeItem().Element(card => ComponerCampoDato(card, "Serie", ObtenerSerieVisual(factura.Serie, emisor)));
                    row.RelativeItem().Element(card => ComponerCampoDato(card, "Numero", ObtenerNumeroDocumento(factura)));
                    row.RelativeItem().Element(card => ComponerCampoDato(card, "Emision", ObtenerFechaEmisionFactura(factura).ToString("dd/MM/yyyy")));
                    row.RelativeItem().Element(card => ComponerCampoDato(card, "Vence", factura.Fechavence?.ToString("dd/MM/yyyy")));
                });

            });
    }

    private static void ComponerBloquePago(IContainer container, FacturaViewDto facturaView)
    {
        var factura = facturaView.Factura;
        var notas = ObtenerNotasAdicionales(facturaView);

        container.Border(1)
            .BorderColor(Colors.Blue.Lighten3)
            .Background(Colors.White)
            .Padding(PaddingBloquePdf)
            .Column(column =>
            {
                column.Spacing(SpacingBloquePdf);
                column.Item().Text("Forma de pago")
                    .FontSize(FuenteTituloSeccionPdf)
                    .SemiBold()
                    .FontColor(Colors.Blue.Darken3);

                column.Item().Table(table =>
                {
                    table.ColumnsDefinition(columns =>
                    {
                        columns.RelativeColumn(1.7f);
                        columns.ConstantColumn(44);
                        columns.ConstantColumn(28);
                    });

                    static IContainer Encabezado(IContainer container) =>
                        container
                            .Border(1)
                            .BorderColor(Colors.Blue.Lighten2)
                            .Background(Colors.Blue.Lighten5)
                            .PaddingVertical(2)
                            .PaddingHorizontal(4);

                    static IContainer Celda(IContainer container) =>
                        container
                            .Border(1)
                            .BorderColor(Colors.Blue.Lighten3)
                            .Background(Colors.White)
                            .PaddingVertical(2)
                            .PaddingHorizontal(4);

                    table.Header(header =>
                    {
                        header.Cell().Element(Encabezado).Text("Forma de Pago").FontSize(FuenteEtiquetaPdf).SemiBold();
                        header.Cell().Element(Encabezado).AlignRight().Text("Valor").FontSize(FuenteEtiquetaPdf).SemiBold();
                        header.Cell().Element(Encabezado).AlignCenter().Text("Dias").FontSize(FuenteEtiquetaPdf).SemiBold();
                    });

                    table.Cell().Element(Celda).Text(ObtenerFormaPagoVisual(facturaView)).FontSize(FuenteEtiquetaPdf);
                    table.Cell().Element(Celda).AlignRight().Text(FormatearMoneda(factura.Valorapagar ?? factura.Valortotal ?? 0m)).FontSize(FuenteEtiquetaPdf);
                    table.Cell().Element(Celda).AlignCenter().Text((factura.Tiempocredito ?? 0).ToString(Cultura)).FontSize(FuenteEtiquetaPdf);
                });

                if (notas.Count > 0)
                {
                    column.Item().PaddingTop(3).Element(item => ComponerCajaNotas(item, "Informacion adicional", notas.Take(3)));
                }

            });
    }

    private static void ComponerBloqueEmisor(IContainer container, Emisor? emisor)
    {
        container.Border(1)
            .BorderColor(Colors.Blue.Lighten4)
            .Background(Colors.White)
            .Padding(PaddingBloquePdf)
            .Column(column =>
            {
                column.Spacing(2);
                column.Item().Text("Datos del emisor")
                    .FontSize(FuenteTituloSeccionPdf)
                    .SemiBold()
                    .FontColor(Colors.Blue.Darken3);

                column.Item().Text(FormatearTextoCasing(emisor?.RazonSocial ?? "Emisor"))
                    .FontSize(10.8f)
                    .SemiBold();

                column.Item().Element(item => ComponerParDato(item, "RUC", emisor?.Ruc));
                column.Item().Element(item => ComponerParDato(item, "Establecimiento", FormatearTextoCasing(emisor?.DirEstablecimiento)));
                column.Item().Element(item => ComponerParDato(item, "Matriz", FormatearTextoCasing(emisor?.DireccionMatriz)));
                column.Item().Element(item => ComponerParDato(item, "Telefono", emisor?.Telefono));
            });
    }

    private static void ComponerBloqueCliente(IContainer container, Cliente? cliente)
    {
        container.Border(1)
            .BorderColor(Colors.Blue.Lighten4)
            .Background(Colors.White)
            .Padding(PaddingBloquePdf)
            .Column(column =>
            {
                column.Spacing(2);
                column.Item().Text("Datos del cliente")
                    .FontSize(FuenteTituloSeccionPdf)
                    .SemiBold()
                    .FontColor(Colors.Blue.Darken3);

                column.Item().Text(ObtenerNombreCliente(cliente))
                    .FontSize(10.8f)
                    .SemiBold();

                column.Item().Element(item => ComponerParDato(item, "Tipo identificacion", ObtenerTipoIdentificacionVisual(cliente?.Tipoidentificacion, cliente?.Numeroidentificacion)));
                column.Item().Element(item => ComponerParDato(item, "Identificacion", cliente?.Numeroidentificacion));
                column.Item().Element(item => ComponerParDato(item, "Direccion", FormatearTextoCasing(cliente?.Direccion)));
                column.Item().Element(item => ComponerParDato(item, "Telefono fijo", cliente?.Telefonoconvencional));
                column.Item().Element(item => ComponerParDato(item, "Celular", cliente?.Celular));
                column.Item().Element(item => ComponerParDato(item, "Correo", cliente?.Correo));
                column.Item().Element(item => ComponerParDato(item, "Dias de credito", cliente?.DiasCredito?.ToString(Cultura)));
            });
    }

    private static void ComponerBloqueNotas(IContainer container, FacturaViewDto facturaView)
    {
        var notas = ObtenerNotasAdicionales(facturaView);

        if (notas.Count == 0)
            return;

        container.Border(1)
            .BorderColor(Colors.Blue.Lighten4)
            .Background(Colors.White)
            .Padding(PaddingBloquePdf)
            .Column(column =>
            {
                column.Spacing(2);
                column.Item().Text("Informacion adicional")
                    .FontSize(FuenteTituloSeccionPdf)
                    .SemiBold()
                    .FontColor(Colors.Blue.Darken3);

                if (notas.Count > 0)
                {
                    column.Item().Element(item => ComponerCajaNotas(item, "Informacion adicional", notas.Take(3)));
                }

            });
    }

    private static void ComponerBloqueResumen(
        IContainer container,
        Factura factura,
        decimal subtotal0,
        decimal subtotal12,
        decimal subtotalNoObjeto,
        decimal subtotalExento,
        decimal subtotal,
        decimal descuentos,
        decimal ice,
        decimal iva,
        decimal irbpnr,
        decimal total,
        IReadOnlyCollection<FacturaPdfLinea> lineas)
    {
        var subtotalConDescuento = Math.Max(0m, subtotal);
        var subtotalBaseGravada = lineas.Where(x => x.TarifaIva > 0 || x.ValorIva > 0).Sum(x => x.Cantidad * x.PrecioUnitario);
        var subtotalBaseCero = lineas.Where(x => x.TarifaIva == 0 && x.ValorIva == 0).Sum(x => x.Cantidad * x.PrecioUnitario);
        if (lineas.Count == 0)
        {
            subtotalBaseGravada = subtotal12;
            subtotalBaseCero = subtotal0;
        }
        var subtotalSinImpuestos = Math.Max(0m, subtotalBaseGravada + subtotalBaseCero + subtotalNoObjeto + subtotalExento);
        var servicio10 = Math.Max(0m, lineas.Where(x => x.TarifaIva == 10).Sum(x => x.ValorIva));
        var iva15 = Math.Max(0m, iva - servicio10);

        container.Border(1)
            .BorderColor(Colors.Blue.Lighten4)
            .Background(Colors.White)
            .Padding(6)
            .Column(column =>
            {
                column.Spacing(2);
                column.Item().Text("Resumen")
                    .FontSize(FuenteTituloSeccionPdf)
                    .SemiBold()
                    .FontColor(Colors.Blue.Darken3);

                column.Item().PaddingTop(1).Table(table =>
                {
                    table.ColumnsDefinition(columns =>
                    {
                        columns.RelativeColumn(2.3f);
                        columns.RelativeColumn(1.0f);
                    });

                    void AgregarFila(string etiqueta, string valor, bool totalFila = false)
                    {
                        table.Cell().Element(c => CeldaResumenEtiqueta(c, totalFila))
                            .Text(etiqueta)
                            .FontColor(Colors.Black)
                            .FontSize(totalFila ? FuenteBasePdf : FuenteEtiquetaPdf)
                            .SemiBold();
                        table.Cell().Element(c => CeldaResumenValor(c, totalFila))
                            .AlignRight()
                            .Text(valor)
                            .FontColor(Colors.Black)
                            .FontSize(totalFila ? FuenteBasePdf : FuenteEtiquetaPdf)
                            .SemiBold();
                    }

                    AgregarFila("Subtotal base gravada", FormatearMoneda(subtotalBaseGravada));
                    AgregarFila("Subtotal base 0%", FormatearMoneda(subtotalBaseCero));
                    AgregarFila("Subtotal no objeto IVA", FormatearMoneda(subtotalNoObjeto));
                    AgregarFila("Subtotal exento IVA", FormatearMoneda(subtotalExento));
                    AgregarFila("Subtotal sin impuestos", FormatearMoneda(subtotalSinImpuestos));
                    AgregarFila("Total descuento", FormatearMoneda(descuentos));
                    AgregarFila("Subtotal con descuento", FormatearMoneda(subtotalConDescuento));
                    AgregarFila("ICE", FormatearMoneda(ice));
                    AgregarFila("IVA 15%", FormatearMoneda(iva15));
                    AgregarFila("Servicio 10%", FormatearMoneda(servicio10));
                    AgregarFila("Valor total", FormatearMoneda(total), true);
                });
            });

        static IContainer CeldaResumenEtiqueta(IContainer container, bool totalFila) =>
            container
                .Border(1)
                .BorderColor(Colors.Grey.Lighten1)
                .PaddingVertical(totalFila ? 3 : 2)
                .PaddingHorizontal(4)
                .Background(Colors.White);

        static IContainer CeldaResumenValor(IContainer container, bool totalFila) =>
            container
                .Border(1)
                .BorderColor(Colors.Grey.Lighten1)
                .PaddingVertical(totalFila ? 3 : 2)
                .PaddingHorizontal(4)
                .Background(Colors.White);
    }

    private static void ComponerDetalle(IContainer container, IReadOnlyCollection<FacturaPdfLinea> lineas)
    {
        container.Border(1)
            .BorderColor(Colors.Blue.Lighten4)
            .Padding(PaddingBloquePdf)
            .Column(column =>
            {
                column.Item().Text("Detalle de la factura")
                    .FontSize(FuenteTituloSeccionPdf)
                    .SemiBold()
                    .FontColor(Colors.Blue.Darken3);

                column.Item().PaddingTop(3).Table(table =>
                {
                    table.ColumnsDefinition(columns =>
                    {
                        columns.ConstantColumn(22);
                        columns.RelativeColumn(1.1f);
                        columns.RelativeColumn(2.9f);
                        columns.ConstantColumn(42);
                        columns.ConstantColumn(56);
                        columns.ConstantColumn(52);
                        columns.ConstantColumn(46);
                        columns.ConstantColumn(54);
                    });

                    table.Header(header =>
                    {
                        header.Cell().Element(CellHeader).Text("#").FontSize(FuenteEtiquetaPdf).SemiBold().FontColor(Colors.White);
                        header.Cell().Element(CellHeader).Text("Codigo").FontSize(FuenteEtiquetaPdf).SemiBold().FontColor(Colors.White);
                        header.Cell().Element(CellHeader).Text("Descripcion").FontSize(FuenteEtiquetaPdf).SemiBold().FontColor(Colors.White);
                        header.Cell().Element(CellHeader).AlignRight().Text("Cant.").FontSize(FuenteEtiquetaPdf).SemiBold().FontColor(Colors.White);
                        header.Cell().Element(CellHeader).AlignRight().Text("P. Unit").FontSize(FuenteEtiquetaPdf).SemiBold().FontColor(Colors.White);
                        header.Cell().Element(CellHeader).AlignRight().Text("Desc.").FontSize(FuenteEtiquetaPdf).SemiBold().FontColor(Colors.White);
                        header.Cell().Element(CellHeader).AlignRight().Text("IVA").FontSize(FuenteEtiquetaPdf).SemiBold().FontColor(Colors.White);
                        header.Cell().Element(CellHeader).AlignRight().Text("Total").FontSize(FuenteEtiquetaPdf).SemiBold().FontColor(Colors.White);
                    });

                    if (!lineas.Any())
                    {
                        table.Cell().ColumnSpan(8).Element(CellBody).AlignCenter().PaddingVertical(8)
                            .Text("No hay detalles registrados para esta factura.");
                    }
                    else
                    {
                        foreach (var linea in lineas)
                        {
                            table.Cell().Element(CellBody).Text(linea.Item.ToString(Cultura));
                            table.Cell().Element(CellBody).Text(linea.Codigo);
                            table.Cell().Element(CellBody).Text(linea.Descripcion);
                            table.Cell().Element(CellBody).AlignRight().Text(FormatearNumero(linea.Cantidad));
                            table.Cell().Element(CellBody).AlignRight().Text(FormatearMoneda(linea.PrecioUnitario));
                            table.Cell().Element(CellBody).AlignRight().Text(FormatearMoneda(linea.DescuentoValor));
                            table.Cell().Element(CellBody).AlignRight().Text(FormatearMoneda(linea.ValorIva));
                            table.Cell().Element(CellBody).AlignRight().Text(FormatearMoneda(linea.Total));
                        }
                    }

                    static IContainer CellHeader(IContainer container) =>
                        container
                            .Background(Colors.Blue.Darken3)
                            .PaddingVertical(3)
                            .PaddingHorizontal(4);

                    static IContainer CellBody(IContainer container) =>
                        container
                            .Border(1)
                            .BorderColor(Colors.Grey.Lighten1)
                            .PaddingVertical(3)
                            .PaddingHorizontal(4);
                });
            });
    }

    private static List<string> ObtenerNotasAdicionales(FacturaViewDto facturaView)
    {
        var factura = facturaView.Factura;
        var cliente = facturaView.Cliente;
        var notas = new List<string>();

        if (!string.IsNullOrWhiteSpace(cliente?.Referencia))
            notas.Add(cliente.Referencia!.Trim());

        if (!string.IsNullOrWhiteSpace(factura.Detalleextra))
            notas.Add(factura.Detalleextra!.Trim());

        if (!string.IsNullOrWhiteSpace(factura.Piepagina))
            notas.Add(factura.Piepagina!.Trim());

        if (!string.IsNullOrWhiteSpace(cliente?.Observaciones))
            notas.Add(cliente.Observaciones!.Trim());

        if (!string.IsNullOrWhiteSpace(factura.Correoad))
            notas.Add($"Correo adicional: {factura.Correoad.Trim()}");

        return notas;
    }

    private static void ComponerPie(IContainer container)
    {
        container.PaddingTop(6).Row(row =>
        {
            row.RelativeItem().Text($"Generado el {DateTime.Now:dd/MM/yyyy HH:mm}")
                .FontSize(FuenteEtiquetaPdf)
                .FontColor(Colors.Grey.Darken1);

            row.ConstantItem(90).AlignRight().Text(text =>
            {
                text.DefaultTextStyle(x => x.FontSize(FuenteEtiquetaPdf).FontColor(Colors.Grey.Darken1));
                text.Span("Pagina ");
                text.CurrentPageNumber();
                text.Span(" de ");
                text.TotalPages();
            });
        });
    }

    private static string ObtenerNumeroDocumento(Factura factura)
    {
        var serie = ObtenerSerieVisual(factura.Serie, null);
        var numero = (factura.Numfactura ?? factura.Codfactura.ToString(Cultura)).Trim();

        return string.IsNullOrWhiteSpace(serie) ? numero : $"{serie}-{numero}";
    }

    private static string ObtenerSerieVisual(string? serie, Emisor? emisor)
    {
        var valor = (serie ?? string.Empty).Trim();

        if (!string.IsNullOrWhiteSpace(valor))
        {
            var limpio = new string(valor.Where(char.IsLetterOrDigit).ToArray());
            if (limpio.Length == 6 && limpio.All(char.IsDigit))
                return $"{limpio[..3]}-{limpio[3..]}";

            if (valor.Contains('-'))
                return valor;
        }

        var establecimiento = new string((emisor?.CodEstablecimiento ?? string.Empty).Where(char.IsLetterOrDigit).ToArray());
        var punto = new string((emisor?.CodPuntoEmision ?? string.Empty).Where(char.IsLetterOrDigit).ToArray());

        if (establecimiento.Length > 0 || punto.Length > 0)
            return $"{establecimiento.PadLeft(3, '0')}-{punto.PadLeft(3, '0')}";

        return string.Empty;
    }

    private static string ObtenerNombreCliente(Cliente? cliente)
    {
        if (cliente == null)
            return "Cliente";

        if (!string.IsNullOrWhiteSpace(cliente.Nombrerazonsocial))
            return FormatearTextoCasing(cliente.Nombrerazonsocial);

        var nombre = $"{cliente.Nombres} {cliente.Apellidos}".Trim();
        if (!string.IsNullOrWhiteSpace(nombre))
            return FormatearTextoCasing(nombre);

        if (!string.IsNullOrWhiteSpace(cliente.Nombrecomercial))
            return FormatearTextoCasing(cliente.Nombrecomercial);

        return "Cliente";
    }

    private static void ComponerCampoDato(IContainer container, string etiqueta, string? valor)
    {
        container
            .Border(1)
            .BorderColor(Colors.Grey.Lighten1)
            .Background(Colors.White)
            .PaddingVertical(3)
            .PaddingHorizontal(5)
            .Column(column =>
            {
                column.Spacing(1);
                column.Item().Text(etiqueta)
                    .FontSize(FuenteEtiquetaPdf)
                    .FontColor(Colors.Grey.Darken1);
                column.Item().Text(ObtenerTextoOGuion(valor))
                    .FontSize(FuenteBasePdf)
                    .SemiBold();
            });
    }

    private static void ComponerParDato(IContainer container, string etiqueta, string? valor)
    {
        if (string.IsNullOrWhiteSpace(valor))
            return;

        container
            .BorderBottom(1)
            .BorderColor(Colors.Grey.Lighten2)
            .PaddingVertical(2)
            .Row(row =>
            {
                row.ConstantItem(82).Text(etiqueta)
                    .FontSize(FuenteEtiquetaPdf)
                    .SemiBold()
                    .FontColor(Colors.Grey.Darken1);
                row.RelativeItem().Text(ObtenerTextoOGuion(valor))
                    .FontSize(FuenteBasePdf)
                    .FontColor(Colors.Black);
            });
    }

    private static void ComponerLineaEncabezado(IContainer container, string? etiqueta, string? valor)
    {
        if (string.IsNullOrWhiteSpace(valor))
            return;

        container.Row(row =>
        {
            if (!string.IsNullOrWhiteSpace(etiqueta))
            {
                row.ConstantItem(116).Text(etiqueta)
                    .FontSize(FuenteBasePdf)
                    .SemiBold()
                    .FontColor(Colors.Grey.Darken2);
            }

            row.RelativeItem().Text(ObtenerTextoOGuion(valor))
                .FontSize(FuenteBasePdf)
                .FontColor(Colors.Grey.Darken2);
        });
    }

    private static void ComponerCajaDatoEncabezado(IContainer container, string titulo, string? valor)
    {
        if (string.IsNullOrWhiteSpace(valor))
            return;

        container.Border(1)
            .BorderColor(Colors.Blue.Lighten3)
            .Background(Colors.Blue.Lighten5)
            .Padding(4)
            .Column(column =>
            {
                column.Spacing(2);
                column.Item().Text(titulo)
                    .FontSize(FuenteEtiquetaPdf)
                    .SemiBold()
                    .FontColor(Colors.Blue.Darken3);
                column.Item().Text(ObtenerTextoOGuion(valor))
                    .FontSize(titulo.Contains("Clave", StringComparison.OrdinalIgnoreCase) ? FuenteEtiquetaPdf : FuenteBasePdf)
                    .FontColor(Colors.Black);
            });
    }

    private static void ComponerCajaTexto(IContainer container, string titulo, string valor)
    {
        if (string.IsNullOrWhiteSpace(valor))
            return;

        container.Border(1)
            .BorderColor(Colors.Blue.Lighten3)
            .Background(Colors.Blue.Lighten5)
            .Padding(6)
            .Column(column =>
            {
                column.Spacing(3);
                column.Item().Text(titulo)
                    .FontSize(FuenteBasePdf)
                    .SemiBold()
                    .FontColor(Colors.Blue.Darken3);
                column.Item().Text(valor)
                    .FontSize(FuenteBasePdf)
                    .LineHeight(1.2f)
                    .FontColor(Colors.Grey.Darken3);
            });
    }

    private static void ComponerCajaNotas(IContainer container, string titulo, IEnumerable<string> notas)
    {
        var items = notas
            .Where(n => !string.IsNullOrWhiteSpace(n))
            .Select(n => n.Trim())
            .ToList();

        if (items.Count == 0)
            return;

        container.Border(1)
            .BorderColor(Colors.Blue.Lighten3)
            .Background(Colors.Blue.Lighten5)
            .Padding(4)
            .Column(column =>
            {
                column.Spacing(1);
                column.Item().Text(titulo)
                    .FontSize(FuenteBasePdf)
                    .SemiBold()
                    .FontColor(Colors.Blue.Darken3);

                foreach (var item in items)
                    column.Item().Text($"- {item}").FontSize(FuenteBasePdf);
            });
    }

    private static string FormatearDireccionVisual(string? valor)
    {
        if (string.IsNullOrWhiteSpace(valor))
            return "-";

        var limpio = valor.Trim().ToLowerInvariant();
        var palabras = limpio
            .Split(' ', StringSplitOptions.RemoveEmptyEntries)
            .Select(FormatearPalabraDireccion);

        return string.Join(' ', palabras);
    }

    private static string FormatearPalabraDireccion(string palabra)
    {
        var segmentos = palabra.Split('/', StringSplitOptions.None);
        for (var i = 0; i < segmentos.Length; i++)
            segmentos[i] = CapitalizarSegmentoDireccion(segmentos[i]);

        return string.Join('/', segmentos);
    }

    private static string CapitalizarSegmentoDireccion(string segmento)
    {
        if (string.IsNullOrWhiteSpace(segmento))
            return segmento;

        var partes = segmento.Split('-', StringSplitOptions.None);
        for (var i = 0; i < partes.Length; i++)
        {
            var parte = partes[i];
            if (string.IsNullOrWhiteSpace(parte))
                continue;

            partes[i] = parte.Length == 1
                ? parte.ToUpperInvariant()
                : char.ToUpperInvariant(parte[0]) + parte[1..];
        }

        return string.Join('-', partes);
    }

    private static bool TieneContenidoAdicional(FacturaViewDto facturaView)
    {
        var factura = facturaView.Factura;
        var cliente = facturaView.Cliente;

        return !string.IsNullOrWhiteSpace(factura.Detalleextra)
            || !string.IsNullOrWhiteSpace(factura.Piepagina)
            || !string.IsNullOrWhiteSpace(factura.Correoad)
            || !string.IsNullOrWhiteSpace(cliente?.Observaciones)
            || !string.IsNullOrWhiteSpace(cliente?.Referencia);
    }

    private static DateTime ObtenerFechaEmisionFactura(Factura factura)
        => factura.Fechaentrega ?? factura.Fchautorizacion ?? DateTime.Now;

    private static string FormatearMoneda(decimal valor)
        => $"${valor.ToString("N2", Cultura)}";

    private static string FormatearNumero(decimal valor)
        => valor.ToString("N2", Cultura);

    private static string FormatearPorcentaje(decimal valor)
        => (valor * 100m).ToString("N2", Cultura) + "%";

    private static string ObtenerTextoOGuion(string? valor)
        => string.IsNullOrWhiteSpace(valor) ? "-" : valor.Trim();

    private static string ObtenerFormaPagoVisual(FacturaViewDto facturaView)
    {
        var nombre = facturaView.FormaPagoNombre?.Trim();
        if (!string.IsNullOrWhiteSpace(nombre))
            return nombre;

        return ObtenerFormaPagoVisual(facturaView.Factura.Tipopago);
    }

    private static string ObtenerFormaPagoVisual(string? codigo)
        => string.IsNullOrWhiteSpace(codigo)
            ? "-"
            : codigo.Trim() switch
            {
                "01" => "Sin utilizacion del sistema financiero",
                "19" => "Tarjeta de credito",
                "20" => "Otros con utilizacion del sistema financiero",
                _ => codigo.Trim()
            };

    private static string ObtenerTipoIdentificacionVisual(string? codigo, string? numeroIdentificacion)
    {
        var identificacion = new string((numeroIdentificacion ?? string.Empty).Where(char.IsDigit).ToArray());
        var codigoLimpio = (codigo ?? string.Empty).Trim();

        if (codigoLimpio == "07" || identificacion == "9999999999999")
            return "Consumidor final";

        if (identificacion.Length == 13)
            return "RUC";

        if (identificacion.Length == 10)
            return "Cedula";

        return codigoLimpio switch
        {
            "04" => "RUC",
            "05" => "Cedula",
            "06" => "Pasaporte",
            "08" => "Identificacion del exterior",
            _ => string.IsNullOrWhiteSpace(codigoLimpio) ? "-" : codigoLimpio
        };
    }

    private static byte[]? GenerarBarcodeNumeroAutorizacion(string? numeroAutorizacion)
    {
        var valor = LimpiarValorBarcode(numeroAutorizacion);
        if (string.IsNullOrWhiteSpace(valor))
            return null;

        try
        {
            var barcode = new Barcode
            {
                IncludeLabel = false
            };

            using var image = barcode.Encode(
                BarcodeStandard.Type.Code128,
                valor,
                SKColors.Black,
                SKColors.White,
                560,
                120);

            using var png = image.Encode(SKEncodedImageFormat.Png, 100);
            return png.ToArray();
        }
        catch
        {
            return GenerarBarcodeFallback(valor);
        }
    }

    private static string LimpiarValorBarcode(string? valor)
        => new((valor ?? string.Empty)
            .Trim()
            .Where(ch => !char.IsWhiteSpace(ch))
            .ToArray());

    private static byte[]? GenerarBarcodeFallback(string valor)
    {
        if (string.IsNullOrWhiteSpace(valor))
            return null;

        try
        {
            var barcode = new Barcode
            {
                IncludeLabel = false
            };

            using var image = barcode.Encode(
                BarcodeStandard.Type.Code128,
                valor,
                SKColors.Black,
                SKColors.White,
                600,
                140);

            using var png = image.Encode(SKEncodedImageFormat.Png, 100);
            return png.ToArray();
        }
        catch
        {
            return null;
        }
    }

    private static string ObtenerAmbienteVisual(int? ambiente)
        => ambiente switch
        {
            1 => "Pruebas",
            2 => "Producción",
            _ => "Producción"
        };

    private static string ObtenerTipoEmisionVisual(string? estadoSri)
    {
        if (string.IsNullOrWhiteSpace(estadoSri))
            return "Normal";

        return DocumentoAutorizacionHelper.EsEstadoAutorizado(estadoSri)
            ? "Normal"
            : "Pendiente";
    }

    private static string ObtenerAmbienteVisual(int? ambiente, string? ambienteEmisor)
        => int.TryParse(ambienteEmisor, out var ambienteConfig)
            ? ambienteConfig switch
            {
                1 => "Pruebas",
                2 => "Producción",
                _ => "Producción"
            }
            : ObtenerAmbienteVisual(ambiente);

    private static string ObtenerTipoEmisionVisualFromEmisor(string? tipoEmision)
    {
        if (string.IsNullOrWhiteSpace(tipoEmision))
            return "Normal";
        var t = tipoEmision.Trim();
        if (t.Equals("NORMAL", StringComparison.OrdinalIgnoreCase))
            return "Normal";
        return t;
    }

    private static ThermalTicketModel ConstruirTicketTermico(FacturaViewDto facturaView, IReadOnlyCollection<FacturaPdfLinea> lineas)
    {
        var factura = facturaView.Factura;
        var cliente = facturaView.Cliente;
        var subtotal = factura.Subtotal ?? lineas.Sum(x => x.BaseImponible);
        var descuentos = factura.Descuentos ?? lineas.Sum(x => x.DescuentoValor);
        var iva = factura.Iva ?? lineas.Sum(x => x.ValorIva);
        var total = factura.Valortotal ?? lineas.Sum(x => x.Total);

        return new ThermalTicketModel
        {
            TituloDocumento = "FACTURA ELECTRONICA",
            NumeroDocumento = ObtenerNumeroDocumento(factura),
            EstadoDocumento = DocumentoAutorizacionHelper.EstaAutorizado(factura.Autorizado, factura.Estadoenviosri) ? "AUTORIZADA" : "EMITIDA",
            FechaEmisionTexto = ObtenerFechaEmisionFactura(factura).ToString("dd/MM/yyyy", Cultura),
            NumeroAutorizacion = DocumentoAutorizacionHelper.ObtenerNumeroAutorizacionVisual(
                factura.Estadoenviosri,
                DocumentoAutorizacionHelper.EstaAutorizado(factura.Autorizado, factura.Estadoenviosri),
                factura.Numautorizacion),
            EtiquetaAcceso = "Clave de acceso",
            ClaveAcceso = ObtenerTextoOGuion(factura.Codclave),
            AmbienteTexto = ObtenerAmbienteVisual(factura.Ambiente, facturaView.Emisor?.TipoAmbiente),
            TipoEmisionTexto = ObtenerTipoEmisionVisualFromEmisor(facturaView.Emisor?.TipoEmision),
            EmisorNombre = facturaView.Emisor?.RazonSocial ?? "EMISOR",
            EmisorSecundario = $"RUC: {facturaView.Emisor?.Ruc ?? "-"}",
            TituloItems = "Items",
            Bloques =
            [
                new ThermalTicketBlock
                {
                    Titulo = "Cliente",
                    Lineas =
                    [
                        new ThermalTicketLine { Etiqueta = "Nombre", Valor = ObtenerNombreCliente(cliente) },
                        new ThermalTicketLine { Etiqueta = "Id", Valor = cliente?.Numeroidentificacion ?? "-" },
                        new ThermalTicketLine { Etiqueta = "Direccion", Valor = cliente?.Direccion ?? "-" }
                    ]
                }
            ],
            Items = lineas.Select(linea => new ThermalTicketItem
            {
                Descripcion = linea.Descripcion,
                DetalleSecundario = $"{linea.Codigo} | P.Unit {FormatearMoneda(linea.PrecioUnitario)}",
                CantidadTexto = FormatearNumero(linea.Cantidad),
                TotalTexto = FormatearMoneda(linea.Total)
            }).ToList(),
            Totales =
            [
                new ThermalTicketLine { Etiqueta = "Subtotal", Valor = FormatearMoneda(subtotal) },
                new ThermalTicketLine { Etiqueta = "Descuento", Valor = FormatearMoneda(descuentos) },
                new ThermalTicketLine { Etiqueta = "IVA", Valor = FormatearMoneda(iva) },
                new ThermalTicketLine { Etiqueta = "TOTAL", Valor = FormatearMoneda(total) }
            ]
        };
    }

    private static string LimpiarSegmentoArchivo(string? valor, string reemplazo)
    {
        var limpio = string.IsNullOrWhiteSpace(valor) ? reemplazo : valor.Trim();
        foreach (var caracter in Path.GetInvalidFileNameChars())
            limpio = limpio.Replace(caracter, '_');

        return limpio.Replace(" ", "_");
    }

    private static string FormatearTextoCasing(string? valor)
    {
        if (string.IsNullOrWhiteSpace(valor))
            return "-";

        var trim = valor.Trim();
        bool tieneMinusculas = trim.Any(char.IsLower);
        if (tieneMinusculas)
        {
            return trim;
        }

        var palabras = trim.ToLowerInvariant().Split(' ', StringSplitOptions.RemoveEmptyEntries);
        var palabrasCapitalizadas = palabras.Select(p => p.Length == 1 ? p.ToUpperInvariant() : char.ToUpperInvariant(p[0]) + p[1..]);
        return string.Join(' ', palabrasCapitalizadas);
    }

    private sealed class FacturaPdfLinea
    {
        public int Item { get; set; }
        public string Codigo { get; set; } = string.Empty;
        public string Descripcion { get; set; } = string.Empty;
        public decimal Cantidad { get; set; }
        public decimal PrecioUnitario { get; set; }
        public decimal DescuentoPorcentaje { get; set; }
        public decimal DescuentoValor { get; set; }
        public decimal BaseImponible { get; set; }
        public decimal ValorIva { get; set; }
        public int TarifaIva { get; set; }
        public decimal Total { get; set; }
    }
}

