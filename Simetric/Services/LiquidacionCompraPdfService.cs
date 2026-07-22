using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;
using Simetric.DTOs;
using System.Globalization;
using BarcodeStandard;
using SkiaSharp;

namespace Simetric.Services;

public interface ILiquidacionCompraPdfService
{
    Task<string> GenerarPdfLiquidacionAsync(LiquidacionCompraPreviewDto preview, FormatoImpresionDocumento formato = FormatoImpresionDocumento.A4);
}

public sealed class LiquidacionCompraPdfService : ILiquidacionCompraPdfService
{
    private static readonly CultureInfo Cultura = new("es-EC");
    private readonly IWebHostEnvironment _environment;

    private const float FuenteBasePdf = 8.2f;
    private const float FuenteEtiquetaPdf = 7.2f;
    private const float FuenteTituloSeccionPdf = 10.6f;
    private const float PaddingBloquePdf = 7f;
    private const float SpacingBloquePdf = 3f;
    private const float AlturaMinimaInfoEmisorPdf = 180f;

    private static TextStyle EstiloBasePdf(TextStyle style)
        => style
            .FontFamily("Arial")
            .FontSize(FuenteBasePdf)
            .LineHeight(1.1f);

    public LiquidacionCompraPdfService(IWebHostEnvironment environment)
    {
        _environment = environment;
    }

    public Task<string> GenerarPdfLiquidacionAsync(LiquidacionCompraPreviewDto preview, FormatoImpresionDocumento formato = FormatoImpresionDocumento.A4)
    {
        if (preview == null)
            throw new InvalidOperationException("No se encontro la informacion necesaria para generar el PDF de la liquidacion.");

        var carpeta = Path.Combine(ObtenerWebRootPath(), "comprobantes", "liquidaciones");
        Directory.CreateDirectory(carpeta);

        var nombreArchivo = $"LIQ_{LimpiarSegmentoArchivo(preview.ClaveAcceso, $"{preview.Serie}_{preview.Secuencial}")}{formato.ObtenerSufijoArchivo()}.pdf";
        var rutaPdf = Path.Combine(carpeta, nombreArchivo);

        var logoEmisor = CargarLogoEmisor(preview.LogoEmisor);
        var lineas = ConstruirLineas(preview);

        QuestPDF.Fluent.Document.Create(container =>
        {
            container.Page(page =>
            {
                if (formato.EsTermico())
                {
                    ThermalPdfComposer.ConfigurePage(page, formato);
                    page.Content().Element(content => ThermalPdfComposer.ComposeTicket(content, ConstruirTicketTermico(preview, lineas)));
                }
                else
                {
                    page.Size(PageSizes.A4);
                    page.Margin(0.6f, Unit.Centimetre);
                    page.PageColor(Colors.White);
                    page.DefaultTextStyle(EstiloBasePdf);

                    page.Header().Element(header => ComponerEncabezado(header, preview, logoEmisor));
                    page.Content().Element(content => ComponerContenido(content, preview, lineas));
                    page.Footer().Element(ComponerPie);
                }
            });
        }).GeneratePdf(rutaPdf);

        return Task.FromResult(rutaPdf);
    }

    private string ObtenerWebRootPath()
    {
        if (!string.IsNullOrWhiteSpace(_environment.WebRootPath))
            return _environment.WebRootPath;

        return Path.Combine(Directory.GetCurrentDirectory(), "wwwroot");
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

    private static List<LiquidacionPdfLinea> ConstruirLineas(LiquidacionCompraPreviewDto preview)
    {
        var detalles = preview.Detalles ?? new List<LiquidacionCompraDetalleDto>();

        return detalles.Select((detalle, index) => new LiquidacionPdfLinea
        {
            Item = index + 1,
            Codigo = !string.IsNullOrWhiteSpace(detalle.CodPrincipal)
                ? detalle.CodPrincipal.Trim()
                : $"PROD-{detalle.CodProducto}",
            Descripcion = detalle.Descripcion?.Trim() ?? "Producto / servicio",
            Cantidad = detalle.Cantidad,
            PrecioUnitario = detalle.PrecioUnitario,
            DescuentoValor = detalle.Descuento,
            ValorIva = detalle.ValorIva,
            Total = detalle.ValorTotal
        }).ToList();
    }

    private static void ComponerEncabezado(IContainer container, LiquidacionCompraPreviewDto preview, byte[]? logoEmisor)
    {
        var numeroDocumento = ObtenerNumeroDocumento(preview);

        container.PaddingBottom(6).Row(row =>
        {
            row.Spacing(10);
            row.RelativeItem().Column(column =>
            {
                if (logoEmisor != null)
                {
                    column.Item()
                        .AlignCenter()
                        .MaxWidth(92)
                        .PaddingBottom(4)
                        .Image(logoEmisor)
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
                    .Column(emisorCol =>
                    {
                        emisorCol.Spacing(2);

                        emisorCol.Item()
                            .Background(Colors.Blue.Lighten5)
                            .PaddingVertical(2)
                            .PaddingHorizontal(5)
                            .Text("Facturación Electrónica")
                            .FontSize(FuenteEtiquetaPdf)
                            .SemiBold()
                            .FontColor(Colors.Blue.Darken3);

                        emisorCol.Item().Text(FormatearTextoCasing(preview.RazonSocialEmisor ?? "EMISOR"))
                            .FontSize(11f)
                            .SemiBold()
                            .FontColor(Colors.Blue.Darken3);

                        emisorCol.Item().Element(item => ComponerLineaEncabezado(item, "RUC:", preview.RucEmisor));
                        emisorCol.Item().Element(item => ComponerLineaEncabezado(item, "Direccion matriz:", FormatearTextoCasing(preview.DireccionMatriz)));

                        if (!string.IsNullOrWhiteSpace(preview.EmailEmisor))
                            emisorCol.Item().Element(item => ComponerLineaEncabezado(item, "Email:", preview.EmailEmisor));

                        if (!string.IsNullOrWhiteSpace(preview.TelefonoEmisor))
                            emisorCol.Item().Element(item => ComponerLineaEncabezado(item, "Teléfono:", preview.TelefonoEmisor));

                        emisorCol.Item().Element(item => ComponerLineaEncabezado(item, "Obligado a llevar contabilidad:", preview.ObligadoContabilidad ?? "NO"));
                    });
            });

            row.ConstantItem(220)
                .Border(1)
                .BorderColor(Colors.Blue.Lighten3)
                .Background(Colors.Blue.Lighten5)
                .Padding(PaddingBloquePdf)
                .Column(column =>
                {
                    column.Spacing(2);
                    column.Item().AlignCenter().Text("LIQUIDACIÓN DE COMPRA DE BIENES Y PRESTACIÓN DE SERVICIOS")
                        .FontSize(9f)
                        .Bold()
                        .FontColor(Colors.Blue.Darken3);

                    column.Item().PaddingTop(2).AlignCenter().Text("No. " + numeroDocumento)
                        .FontSize(11)
                        .Bold();

                    column.Item().PaddingTop(2).Element(item => ComponerCajaDatoEncabezado(item, "Número de autorización", string.IsNullOrWhiteSpace(preview.NumeroAutorizacion) ? "PENDIENTE" : preview.NumeroAutorizacion));

                    if (preview.EstaAutorizada)
                    {
                        var fechaAut = (preview.FechaEmision ?? DateTime.Today).ToString("dd/MM/yyyy HH:mm:ss");
                        column.Item().PaddingTop(2).Text($"Fecha y hora de autorización: {fechaAut}").FontSize(8);
                    }
                    else
                    {
                        column.Item().PaddingTop(2).Text("Fecha y hora de autorización: -").FontSize(8);
                    }

                    column.Item().PaddingTop(2).Element(item => ComponerLineaEncabezado(item, "Tipo emisión:", "NORMAL"));
                    column.Item().Element(item => ComponerLineaEncabezado(item, "Ambiente:", preview.Ambiente == 1 ? "PRUEBAS" : "PRODUCCIÓN"));

                    column.Item().PaddingTop(2).Element(item => ComponerCajaDatoEncabezado(item, "Clave de acceso", preview.ClaveAcceso));

                    var barcodeBytes = GenerarBarcode(preview.ClaveAcceso);
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

    private static void ComponerContenido(
        IContainer container,
        LiquidacionCompraPreviewDto preview,
        IReadOnlyCollection<LiquidacionPdfLinea> lineas)
    {
        container.Column(column =>
        {
            column.Spacing(6);

            column.Item().Row(row =>
            {
                row.Spacing(8);
                row.RelativeItem().Element(card => ComponerBloqueProveedor(card, preview));
                row.ConstantItem(220).Element(card => ComponerBloqueResumen(card, preview));
            });

            column.Item().Element(table => ComponerDetalle(table, lineas));

            column.Item().PaddingTop(1).Text("Este documento fue generado desde el registro manual de la liquidacion de compra.")
                .FontSize(FuenteEtiquetaPdf)
                .FontColor(Colors.Grey.Darken1);
        });
    }

    private static void ComponerBloqueProveedor(IContainer container, LiquidacionCompraPreviewDto preview)
    {
        container.Border(1)
            .BorderColor(Colors.Blue.Lighten4)
            .Background(Colors.White)
            .Padding(PaddingBloquePdf)
            .Column(column =>
            {
                column.Spacing(2);
                column.Item().Text("Datos del proveedor")
                    .FontSize(FuenteTituloSeccionPdf)
                    .SemiBold()
                    .FontColor(Colors.Blue.Darken3);

                column.Item().Text(string.IsNullOrWhiteSpace(preview.RazonSocialProveedor) ? "Proveedor" : FormatearTextoCasing(preview.RazonSocialProveedor))
                    .FontSize(10.8f)
                    .SemiBold();

                column.Item().Element(item => ComponerParDato(item, "Identificación", preview.IdentificacionProveedor));

                if (!string.IsNullOrWhiteSpace(preview.TipoIdentificacionProveedorNombre))
                    column.Item().Element(item => ComponerParDato(item, "Tipo", preview.TipoIdentificacionProveedorNombre));

                column.Item().Element(item => ComponerParDato(item, "Dirección", FormatearTextoCasing(preview.DireccionProveedor)));

                if (!string.IsNullOrWhiteSpace(preview.EmailProveedor))
                    column.Item().Element(item => ComponerParDato(item, "Correo", preview.EmailProveedor));

                var telefono = ObtenerTelefonoProveedor(preview);
                if (!string.IsNullOrWhiteSpace(telefono))
                    column.Item().Element(item => ComponerParDato(item, "Teléfono", telefono));
                column.Item().Element(item => ComponerParDato(item, "Emisión", (preview.FechaEmision ?? DateTime.Today).ToString("dd/MM/yyyy")));
            });
    }

    private static void ComponerBloqueResumen(IContainer container, LiquidacionCompraPreviewDto preview)
    {
        var subtotalBruto = preview.Detalles.Any()
            ? preview.Detalles.Sum(x => x.Cantidad * x.PrecioUnitario)
            : preview.TotalSinImpuestos + preview.TotalDescuento;
        container.Border(1)
            .BorderColor(Colors.Blue.Lighten4)
            .Background(Colors.White)
            .Padding(PaddingBloquePdf)
            .Column(column =>
            {
                column.Spacing(2);
                column.Item().Text("Resumen")
                    .FontSize(FuenteTituloSeccionPdf)
                    .SemiBold()
                    .FontColor(Colors.Blue.Darken3);

                column.Item().Element(item => ComponerParDato(item, "Serie", preview.SerieVisual));
                column.Item().Element(item => ComponerParDato(item, "Forma de pago", ObtenerFormaPago(preview)));

                if ((preview.Plazo ?? 0) > 0)
                    column.Item().Element(item => ComponerParDato(item, "Crédito", $"{preview.Plazo} {preview.UnidadTiempo}"));

                column.Item().Element(item => ComponerParDato(item, "Subtotal base gravada", FormatearMoneda(subtotalBruto)));
                column.Item().Element(item => ComponerParDato(item, "Descuentos", FormatearMoneda(preview.TotalDescuento)));
                column.Item().Element(item => ComponerParDato(item, "Subtotal con descuento", FormatearMoneda(preview.TotalSinImpuestos)));
                column.Item().Element(item => ComponerParDato(item, "IVA", FormatearMoneda(preview.IvaTotal)));

                column.Item().PaddingTop(4).Background(Colors.Blue.Lighten5).Padding(6)
                    .Row(row =>
                    {
                        row.RelativeItem().Text("TOTAL")
                            .SemiBold()
                            .FontSize(10)
                            .FontColor(Colors.Blue.Darken3);
                        row.RelativeItem().AlignRight().Text(FormatearMoneda(preview.ImporteTotal))
                            .SemiBold()
                            .FontSize(10)
                            .FontColor(Colors.Blue.Darken3);
                    });
            });
    }

    private static void ComponerDetalle(IContainer container, IReadOnlyCollection<LiquidacionPdfLinea> lineas)
    {
        container.Border(1)
            .BorderColor(Colors.Blue.Lighten4)
            .Padding(PaddingBloquePdf)
            .Column(column =>
            {
                column.Item().Text("Detalle de la liquidación")
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
                        header.Cell().Element(CellHeader).Text("Código").FontSize(FuenteEtiquetaPdf).SemiBold().FontColor(Colors.White);
                        header.Cell().Element(CellHeader).Text("Descripción").FontSize(FuenteEtiquetaPdf).SemiBold().FontColor(Colors.White);
                        header.Cell().Element(CellHeader).AlignRight().Text("Cant.").FontSize(FuenteEtiquetaPdf).SemiBold().FontColor(Colors.White);
                        header.Cell().Element(CellHeader).AlignRight().Text("P. Unit").FontSize(FuenteEtiquetaPdf).SemiBold().FontColor(Colors.White);
                        header.Cell().Element(CellHeader).AlignRight().Text("Desc.").FontSize(FuenteEtiquetaPdf).SemiBold().FontColor(Colors.White);
                        header.Cell().Element(CellHeader).AlignRight().Text("IVA").FontSize(FuenteEtiquetaPdf).SemiBold().FontColor(Colors.White);
                        header.Cell().Element(CellHeader).AlignRight().Text("Total").FontSize(FuenteEtiquetaPdf).SemiBold().FontColor(Colors.White);
                    });

                    if (!lineas.Any())
                    {
                        table.Cell().ColumnSpan(8).Element(CellBody).AlignCenter().PaddingVertical(8)
                            .Text("No hay detalles registrados para esta liquidación.").FontSize(FuenteEtiquetaPdf);
                    }
                    else
                    {
                        foreach (var linea in lineas)
                        {
                            table.Cell().Element(CellBody).Text(linea.Item.ToString(Cultura)).FontSize(FuenteEtiquetaPdf);
                            table.Cell().Element(CellBody).Text(linea.Codigo).FontSize(FuenteEtiquetaPdf);
                            table.Cell().Element(CellBody).Text(FormatearTextoCasing(linea.Descripcion)).FontSize(FuenteEtiquetaPdf);
                            table.Cell().Element(CellBody).AlignRight().Text(FormatearNumero(linea.Cantidad)).FontSize(FuenteEtiquetaPdf);
                            table.Cell().Element(CellBody).AlignRight().Text(FormatearMoneda(linea.PrecioUnitario)).FontSize(FuenteEtiquetaPdf);
                            table.Cell().Element(CellBody).AlignRight().Text(FormatearMoneda(linea.DescuentoValor)).FontSize(FuenteEtiquetaPdf);
                            table.Cell().Element(CellBody).AlignRight().Text(FormatearMoneda(linea.ValorIva)).FontSize(FuenteEtiquetaPdf);
                            table.Cell().Element(CellBody).AlignRight().Text(FormatearMoneda(linea.Total)).FontSize(FuenteEtiquetaPdf);
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
                text.Span("Página ");
                text.CurrentPageNumber();
                text.Span(" de ");
                text.TotalPages();
            });
        });
    }

    private static string ObtenerNumeroDocumento(LiquidacionCompraPreviewDto preview)
        => $"{preview.SerieVisual}-{(preview.Secuencial ?? "").PadLeft(9, '0')}";

    private static string ObtenerTelefonoProveedor(LiquidacionCompraPreviewDto preview)
    {
        if (!string.IsNullOrWhiteSpace(preview.TelefonoProveedor))
            return preview.TelefonoProveedor;

        return preview.TelefonoFijoProveedor ?? "";
    }

    private static string ObtenerFormaPago(LiquidacionCompraPreviewDto preview)
    {
        if (!string.IsNullOrWhiteSpace(preview.FormaPagoNombre))
            return preview.FormaPagoNombre;

        if (!string.IsNullOrWhiteSpace(preview.FormaPago))
            return preview.FormaPago;

        return "-";
    }

    private static string FormatearMoneda(decimal valor)
        => $"${valor.ToString("N2", Cultura)}";

    private static string FormatearNumero(decimal valor)
        => valor.ToString("N2", Cultura);

    private static string ObtenerTextoOGuion(string? valor)
    {
        return string.IsNullOrWhiteSpace(valor) ? "-" : valor.Trim();
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
                row.ConstantItem(85).Text(etiqueta)
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

    private static ThermalTicketModel ConstruirTicketTermico(LiquidacionCompraPreviewDto preview, IReadOnlyCollection<LiquidacionPdfLinea> lineas)
    {
        return new ThermalTicketModel
        {
            TituloDocumento = "LIQUIDACION DE COMPRA",
            NumeroDocumento = ObtenerNumeroDocumento(preview),
            EstadoDocumento = preview.EstaAutorizada ? "AUTORIZADA" : "REGISTRADA",
            FechaEmisionTexto = (preview.FechaEmision ?? DateTime.Today).ToString("dd/MM/yyyy", Cultura),
            EtiquetaAcceso = preview.EstaAutorizada ? "Numero de autorizacion" : "Clave temporal",
            ClaveAcceso = preview.EstaAutorizada ? (preview.NumeroAutorizacion ?? string.Empty) : (preview.ClaveAcceso ?? string.Empty),
            EmisorNombre = preview.RazonSocialEmisor ?? "EMISOR",
            EmisorSecundario = $"RUC: {preview.RucEmisor ?? "-"}",
            TituloItems = "Detalle",
            Bloques =
            [
                new ThermalTicketBlock
                {
                    Titulo = "Proveedor",
                    Lineas =
                    [
                        new ThermalTicketLine { Etiqueta = "Nombre", Valor = string.IsNullOrWhiteSpace(preview.RazonSocialProveedor) ? "Proveedor" : preview.RazonSocialProveedor },
                        new ThermalTicketLine { Etiqueta = "Id", Valor = preview.IdentificacionProveedor ?? "-" },
                        new ThermalTicketLine { Etiqueta = "Pago", Valor = ObtenerFormaPago(preview) }
                    ]
                }
            ],
            Items = lineas.Select(linea => new ThermalTicketItem
            {
                Descripcion = linea.Descripcion,
                DetalleSecundario = $"{linea.Codigo} | Unit {FormatearMoneda(linea.PrecioUnitario)}",
                CantidadTexto = FormatearNumero(linea.Cantidad),
                TotalTexto = FormatearMoneda(linea.Total)
            }).ToList(),
            Totales =
            [
                new ThermalTicketLine { Etiqueta = "Base gravada", Valor = FormatearMoneda(lineas.Sum(x => x.Cantidad * x.PrecioUnitario)) },
                new ThermalTicketLine { Etiqueta = "Descuentos", Valor = FormatearMoneda(preview.TotalDescuento) },
                new ThermalTicketLine { Etiqueta = "Subtotal c/desc.", Valor = FormatearMoneda(preview.TotalSinImpuestos) },
                new ThermalTicketLine { Etiqueta = "IVA", Valor = FormatearMoneda(preview.IvaTotal) },
                new ThermalTicketLine { Etiqueta = "TOTAL", Valor = FormatearMoneda(preview.ImporteTotal) }
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

    private static byte[]? GenerarBarcode(string? clave)
    {
        if (string.IsNullOrWhiteSpace(clave)) return null;
        var valor = clave.Trim();
        try
        {
            var barcode = new Barcode { IncludeLabel = false };
            using var image = barcode.Encode(BarcodeStandard.Type.Code128, valor, SKColors.Black, SKColors.White, 560, 120);
            using var png = image.Encode(SKEncodedImageFormat.Png, 100);
            return png.ToArray();
        }
        catch { return null; }
    }

    private sealed class LiquidacionPdfLinea
    {
        public int Item { get; set; }
        public string Codigo { get; set; } = string.Empty;
        public string Descripcion { get; set; } = string.Empty;
        public decimal Cantidad { get; set; }
        public decimal PrecioUnitario { get; set; }
        public decimal DescuentoValor { get; set; }
        public decimal ValorIva { get; set; }
        public decimal Total { get; set; }
    }
}
