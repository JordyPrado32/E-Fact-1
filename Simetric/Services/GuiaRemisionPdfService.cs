using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;
using BarcodeStandard;
using Simetric.DTOs;
using System.Globalization;
using SkiaSharp;

namespace Simetric.Services;

public interface IGuiaRemisionPdfService
{
    Task<string> GenerarPdfGuiaRemisionAsync(GuiaRemisionDetalleViewDto view, FormatoImpresionDocumento formato = FormatoImpresionDocumento.A4);
}

public sealed class GuiaRemisionPdfService : IGuiaRemisionPdfService
{
    private static readonly CultureInfo Cultura = new("es-EC");
    private const float FuenteBase = 8.2f;
    private const float FuenteEtiqueta = 7.2f;
    private const float FuenteTituloSeccion = 10.6f;
    private const float PaddingBloque = 7f;
    private readonly IWebHostEnvironment _environment;

    public GuiaRemisionPdfService(IWebHostEnvironment environment)
    {
        _environment = environment;
    }

    public Task<string> GenerarPdfGuiaRemisionAsync(GuiaRemisionDetalleViewDto view, FormatoImpresionDocumento formato = FormatoImpresionDocumento.A4)
    {
        if (view?.Guia == null)
            throw new InvalidOperationException("No se encontró la información necesaria para generar el PDF de la guía de remisión.");

        var carpeta = Path.Combine(ObtenerWebRootPath(), "comprobantes", "guias_remision");
        Directory.CreateDirectory(carpeta);

        var rutaPdf = Path.Combine(
            carpeta,
            ConstruirNombreArchivo(view.Emisor?.Ruc, view.Guia.Serie, view.Guia.NumGuiaRemision, formato));

        var logoBytes = CargarLogoEmisor(view.Emisor?.LogoImagen);

        Document.Create(container =>
        {
            container.Page(page =>
            {
                if (formato.EsTermico())
                {
                    ThermalPdfComposer.ConfigurePage(page, formato);
                    page.Content().Element(content => ThermalPdfComposer.ComposeTicket(content, ConstruirTicketTermico(view)));
                }
                else
                {
                    page.Size(PageSizes.A4);
                    page.Margin(0.6f, Unit.Centimetre);
                    page.PageColor(Colors.White);
                    page.DefaultTextStyle(EstiloBase);

                    page.Header().Element(header => ComponerEncabezado(header, view, logoBytes));
                    page.Content().Element(content => ComponerContenido(content, view));
                    page.Footer().Element(ComponerPie);
                }
            });
        }).GeneratePdf(rutaPdf);

        return Task.FromResult(rutaPdf);
    }

    // ── Helpers de infraestructura ─────────────────────────────────────────────

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
        var prefixes = new[]
        {
            "data:image/jpeg;base64,",
            "data:image/jpg;base64,",
            "data:image/png;base64,",
            "data:image/webp;base64,"
        };

        if (prefixes.Any(prefix => logo.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)))
        {
            var commaIndex = logo.IndexOf(',');
            if (commaIndex > -1 && commaIndex < logo.Length - 1)
            {
                try { return Convert.FromBase64String(logo[(commaIndex + 1)..]); }
                catch { return null; }
            }
        }

        var normalized = logo.Replace('/', Path.DirectorySeparatorChar).TrimStart(Path.DirectorySeparatorChar);
        var candidates = new[]
        {
            Path.Combine(ObtenerWebRootPath(), normalized),
            Path.Combine(Directory.GetCurrentDirectory(), normalized),
            logo
        };

        foreach (var candidate in candidates.Where(File.Exists))
            return File.ReadAllBytes(candidate);

        return null;
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

    private static TextStyle EstiloBase(TextStyle s)
        => s.FontFamily("Arial").FontSize(FuenteBase).LineHeight(1.1f);

    // ── Encabezado ─────────────────────────────────────────────────────────────

    private static void ComponerEncabezado(IContainer container, GuiaRemisionDetalleViewDto view, byte[]? logo)
    {
        var guia = view.Guia;
        var emisor = view.Emisor;
        var esAutorizada = DocumentoAutorizacionHelper.EsEstadoAutorizado(guia.EstadoSRI);
        var estadoDocumento = esAutorizada ? "Autorizada" : "Emitida";
        var ambiente = (guia.Ambiente ?? 1) == 1 ? "Pruebas" : "Producción";
        var numeroAutorizacion = string.IsNullOrWhiteSpace(guia.NumAutorizacion) ? "-" : guia.NumAutorizacion;
        var claveAcceso = string.IsNullOrWhiteSpace(guia.CodClave) ? "-" : guia.CodClave;

        container.PaddingBottom(6).Row(row =>
        {
            row.Spacing(10);

            // ── Izquierda: logo + datos emisor ──────────────────────────────
            row.RelativeItem().Column(column =>
            {
                if (logo != null)
                {
                    column.Item()
                        .AlignCenter()
                        .MaxWidth(92)
                        .PaddingBottom(4)
                        .Image(logo)
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
                    .Padding(PaddingBloque)
                    .Column(info =>
                    {
                        info.Spacing(2);

                        info.Item()
                            .Background(Colors.Blue.Lighten5)
                            .PaddingVertical(2)
                            .PaddingHorizontal(5)
                            .Text("Facturación Electrónica")
                            .FontSize(FuenteEtiqueta)
                            .SemiBold()
                            .FontColor(Colors.Blue.Darken3);

                        info.Item().Text(FormatearTextoCasing(emisor?.RazonSocial ?? "Emisor"))
                            .FontSize(13f)
                            .SemiBold()
                            .FontColor(Colors.Blue.Darken3);

                        if (!string.IsNullOrWhiteSpace(emisor?.Ruc))
                            info.Item().Element(item => ComponerLineaEncabezado(item, "RUC:", emisor.Ruc));

                        if (!string.IsNullOrWhiteSpace(emisor?.DireccionMatriz))
                            info.Item().Element(item => ComponerLineaEncabezado(item, "Direccion matriz:", FormatearTextoCasing(emisor.DireccionMatriz)));

                        if (!string.IsNullOrWhiteSpace(emisor?.Telefono))
                            info.Item().Element(item => ComponerLineaEncabezado(item, "Telefono:", emisor.Telefono));

                        info.Item().Element(item => ComponerLineaEncabezado(item, "Fecha emision:", (guia.Fecha ?? DateTime.Now).ToString("dd/MM/yyyy")));
                    });
            });

            // ── Derecha: identificación del documento ───────────────────────
            row.RelativeItem()
                .Border(1)
                .BorderColor(Colors.Blue.Lighten3)
                .Background(Colors.Blue.Lighten5)
                .Padding(PaddingBloque)
                .Column(column =>
                {
                    column.Spacing(2);

                    column.Item().AlignCenter().Text(text =>
                    {
                        text.DefaultTextStyle(x => x.FontSize(10.2f).SemiBold());
                        text.Span("Guía de Remisión").FontColor(Colors.Blue.Darken3).FontSize(12.2f);
                        text.Span($"  No. {view.NumeroCompleto}").FontColor(Colors.Black);
                    });

                    column.Item().Element(item => ComponerLineaEncabezado(item, "Estado del documento:", estadoDocumento));
                    column.Item().PaddingTop(2).Element(item => ComponerCajaDatoEncabezado(item, "Clave de acceso", claveAcceso));
                    column.Item().PaddingTop(2).Element(item => ComponerCajaDatoEncabezado(item, "Número de autorización", numeroAutorizacion));

                    column.Item().PaddingTop(2).Element(item => ComponerLineaEncabezado(item, "Ambiente:", ambiente));
                    column.Item().Element(item => ComponerLineaEncabezado(item, "Tipo emisión:", "Normal"));
                    if (!string.IsNullOrWhiteSpace(view.NumeroDocumentoSustentoVisual))
                        column.Item().PaddingTop(2).Element(item => ComponerLineaEncabezado(item, "Documento sustento:", view.NumeroDocumentoSustentoVisual));
                    column.Item().Element(item => ComponerLineaEncabezado(item, "Fecha inicio transporte:", (guia.FechaIniTransporte ?? guia.Fecha ?? DateTime.Now).ToString("dd/MM/yyyy")));

                    var barcodeBytes = GenerarBarcode(guia.CodClave);
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

    // ── Contenido ──────────────────────────────────────────────────────────────

    private static void ComponerContenido(IContainer container, GuiaRemisionDetalleViewDto view)
    {
        container.Column(column =>
        {
            column.Spacing(4);

            column.Item().Element(card => ComponerBloqueTransportista(card, view));
            column.Item().Element(card => ComponerBloqueDestinatario(card, view));
            column.Item().Element(table => ComponerDetalle(table, view.Detalles));

            if (!string.IsNullOrWhiteSpace(view.Guia.Mensaje))
                column.Item().Element(card => ComponerBloqueObservacion(card, view.Guia.Mensaje));

            column.Item().PaddingTop(1).Text("Documento generado por el sistema de facturacion electronica.")
                .FontSize(FuenteEtiqueta)
                .FontColor(Colors.Grey.Darken1);
        });
    }

    // ── Bloque Transportista ───────────────────────────────────────────────────

    private static void ComponerBloqueTransportista(IContainer container, GuiaRemisionDetalleViewDto view)
    {
        container.Border(1)
            .BorderColor(Colors.Blue.Lighten4)
            .Background(Colors.White)
            .Padding(PaddingBloque)
            .Column(column =>
            {
                column.Spacing(2);
                column.Item().Text("Datos del transportista")
                    .FontSize(FuenteTituloSeccion)
                    .SemiBold()
                    .FontColor(Colors.Blue.Darken3);

                column.Item().Text(FormatearTextoCasing(view.Transportista?.RazonSocial ?? "Transportista"))
                    .FontSize(10.8f)
                    .SemiBold();

                column.Item().Element(item => ComponerParDato(item, "Identificacion", view.Transportista?.NumeroIdentificacion));
                column.Item().Element(item => ComponerParDato(item, "Tipo identificacion", view.Transportista?.TipoIdentificacion));
                column.Item().Element(item => ComponerParDato(item, "Placa", view.Guia.Placa ?? view.Transportista?.Placa));
                column.Item().Element(item => ComponerParDato(item, "Fecha inicio transporte", (view.Guia.FechaIniTransporte ?? DateTime.Today).ToString("dd/MM/yyyy")));
                column.Item().Element(item => ComponerParDato(item, "Fecha fin transporte", (view.Guia.FechaFinTransporte ?? view.Guia.FechaIniTransporte ?? DateTime.Today).ToString("dd/MM/yyyy")));
                if (!string.IsNullOrWhiteSpace(view.Transportista?.Telefono))
                    column.Item().Element(item => ComponerParDato(item, "Telefono", view.Transportista.Telefono));
                if (!string.IsNullOrWhiteSpace(view.Guia.DireccionPartida))
                    column.Item().Element(item => ComponerParDato(item, "Direccion de partida", FormatearTextoCasing(view.Guia.DireccionPartida)));
            });
    }

    // ── Bloque Destinatario ────────────────────────────────────────────────────

    private static void ComponerBloqueDestinatario(IContainer container, GuiaRemisionDetalleViewDto view)
    {
        container.Border(1)
            .BorderColor(Colors.Blue.Lighten4)
            .Background(Colors.White)
            .Padding(PaddingBloque)
            .Column(column =>
            {
                column.Spacing(2);
                column.Item().Text("Datos del destinatario")
                    .FontSize(FuenteTituloSeccion)
                    .SemiBold()
                    .FontColor(Colors.Blue.Darken3);

                column.Item().Text(FormatearTextoCasing(view.Destinatario?.RazonSocial ?? "Destinatario"))
                    .FontSize(10.8f)
                    .SemiBold();

                column.Item().Element(item => ComponerParDato(item, "Identificacion", view.Destinatario?.IdDestinatario));
                column.Item().Element(item => ComponerParDato(item, "Direccion de entrega", FormatearTextoCasing(view.Destinatario?.Direccion)));
                column.Item().Element(item => ComponerParDato(item, "Motivo de traslado", view.Destinatario?.MotivoTraslado));
                if (!string.IsNullOrWhiteSpace(view.NumeroDocumentoSustentoVisual))
                    column.Item().Element(item => ComponerParDato(item, "Documento sustento", view.NumeroDocumentoSustentoVisual));
                if (view.Destinatario?.FechaEmiSustento.HasValue == true)
                    column.Item().Element(item => ComponerParDato(item, "Fecha sustento", view.Destinatario.FechaEmiSustento.Value.ToString("dd/MM/yyyy")));
            });
    }

    // ── Detalle ────────────────────────────────────────────────────────────────

    private static void ComponerDetalle(IContainer container, IReadOnlyCollection<GuiaRemisionDetalleLineaDto> detalles)
    {
        container.Border(1)
            .BorderColor(Colors.Blue.Lighten4)
            .Padding(PaddingBloque)
            .Column(column =>
            {
                column.Item().Text("Detalle de traslado")
                    .FontSize(FuenteTituloSeccion)
                    .SemiBold()
                    .FontColor(Colors.Blue.Darken3);

                column.Item().PaddingTop(3).Table(table =>
                {
                    table.ColumnsDefinition(columns =>
                    {
                        columns.ConstantColumn(24);
                        columns.RelativeColumn(1.0f);
                        columns.RelativeColumn(1.0f);
                        columns.RelativeColumn(3.0f);
                        columns.ConstantColumn(60);
                    });

                    table.Header(header =>
                    {
                        header.Cell().Element(CellHeader).AlignCenter().Text("#").SemiBold().FontColor(Colors.White);
                        header.Cell().Element(CellHeader).Text("Código").SemiBold().FontColor(Colors.White);
                        header.Cell().Element(CellHeader).Text("Cód. Adicional").SemiBold().FontColor(Colors.White);
                        header.Cell().Element(CellHeader).Text("Descripción").SemiBold().FontColor(Colors.White);
                        header.Cell().Element(CellHeader).AlignRight().Text("Cantidad").SemiBold().FontColor(Colors.White);
                    });

                    if (!detalles.Any())
                    {
                        table.Cell().ColumnSpan(5).Element(CellBody).AlignCenter().PaddingVertical(16)
                            .Text("No hay detalles registrados para esta guía.");
                    }
                    else
                    {
                        var i = 1;
                        foreach (var d in detalles)
                        {
                            table.Cell().Element(CellBody).AlignCenter().Text(i.ToString());
                            table.Cell().Element(CellBody).Text(d.CodigoInterno);
                            table.Cell().Element(CellBody).Text(d.CodigoAdicional);
                            table.Cell().Element(CellBody).Text(FormatearTextoCasing(d.Descripcion));
                            table.Cell().Element(CellBody).AlignRight().Text(d.Cantidad.ToString("N2", Cultura));
                            i++;
                        }
                    }

                    static IContainer CellHeader(IContainer c) =>
                        c.Background(Colors.Blue.Darken3).PaddingVertical(7).PaddingHorizontal(5);

                    static IContainer CellBody(IContainer c) =>
                        c.BorderBottom(1).BorderColor(Colors.Grey.Lighten2).PaddingVertical(7).PaddingHorizontal(5);
                });
            });
    }

    // ── Observación ────────────────────────────────────────────────────────────

    private static void ComponerBloqueObservacion(IContainer container, string mensaje)
    {
        container.Border(1)
            .BorderColor(Colors.Blue.Lighten4)
            .Background(Colors.White)
            .Padding(PaddingBloque)
            .Column(column =>
            {
                column.Spacing(2);
                column.Item().Text("Observación")
                    .FontSize(FuenteTituloSeccion)
                    .SemiBold()
                    .FontColor(Colors.Blue.Darken3);

                column.Item().Text(mensaje).FontSize(FuenteBase);
            });
    }

    // ── Pie de página ──────────────────────────────────────────────────────────

    private static void ComponerPie(IContainer container)
    {
        container.PaddingTop(10).Row(row =>
        {
            row.RelativeItem().Text($"Generado el {DateTime.Now:dd/MM/yyyy HH:mm}")
                .FontSize(8)
                .FontColor(Colors.Grey.Darken1);

            row.ConstantItem(90).AlignRight().Text(text =>
            {
                text.DefaultTextStyle(x => x.FontSize(8).FontColor(Colors.Grey.Darken1));
                text.Span("Página ");
                text.CurrentPageNumber();
                text.Span(" de ");
                text.TotalPages();
            });
        });
    }

    // ── Helpers visuales ───────────────────────────────────────────────────────

    private static void ComponerLineaEncabezado(IContainer container, string etiqueta, string? valor)
    {
        container.Row(row =>
        {
            row.ConstantItem(100).Text(etiqueta)
                .FontSize(FuenteEtiqueta)
                .FontColor(Colors.Blue.Darken2);
            row.RelativeItem().Text(valor ?? "-")
                .FontSize(FuenteEtiqueta);
        });
    }

    private static void ComponerCajaDatoEncabezado(IContainer container, string titulo, string valor)
    {
        container.Border(1)
            .BorderColor(Colors.Blue.Lighten3)
            .Background(Colors.White)
            .Padding(3)
            .Column(c =>
            {
                c.Item().Text(titulo).FontSize(FuenteEtiqueta).SemiBold().FontColor(Colors.Blue.Darken3);
                c.Item().Text(valor).FontSize(FuenteEtiqueta);
            });
    }

    private static void ComponerParDato(IContainer container, string etiqueta, string? valor)
    {
        if (string.IsNullOrWhiteSpace(valor) || valor == "-")
            return;

        container.Row(row =>
        {
            row.ConstantItem(130).Text(etiqueta)
                .FontSize(FuenteEtiqueta)
                .FontColor(Colors.Blue.Darken2);
            row.RelativeItem().Text(valor)
                .FontSize(FuenteBase);
        });
    }

    // ── Utiles de texto ────────────────────────────────────────────────────────

    private static string FormatearTextoCasing(string? valor)
    {
        if (string.IsNullOrWhiteSpace(valor)) return "-";
        var trim = valor.Trim();
        if (trim.Any(char.IsLower)) return trim;
        var palabras = trim.ToLowerInvariant().Split(' ', StringSplitOptions.RemoveEmptyEntries);
        return string.Join(' ', palabras.Select(p => p.Length == 1 ? p.ToUpperInvariant() : char.ToUpperInvariant(p[0]) + p[1..]));
    }

    // ── Nombres de archivo ─────────────────────────────────────────────────────

    private static string ConstruirNombreArchivo(string? ruc, string? serie, string? secuencial, FormatoImpresionDocumento formato)
        => $"{LimpiarSegmentoArchivo(ruc, "SIN_RUC")}_06_{NormalizarSerie(serie).PadLeft(6, '0')}{SoloDigitos(secuencial).PadLeft(9, '0')}{formato.ObtenerSufijoArchivo()}.pdf";

    private static string LimpiarSegmentoArchivo(string? valor, string reemplazo)
    {
        var limpio = string.IsNullOrWhiteSpace(valor) ? reemplazo : valor.Trim();
        foreach (var c in Path.GetInvalidFileNameChars())
            limpio = limpio.Replace(c, '_');
        return limpio.Replace(" ", "_");
    }

    private static string SoloDigitos(string? valor) => new string((valor ?? string.Empty).Where(char.IsDigit).ToArray());
    private static string NormalizarSerie(string? serie) => string.IsNullOrWhiteSpace(serie) ? string.Empty : serie.Replace("-", string.Empty).Trim();

    // ── Ticket térmico ─────────────────────────────────────────────────────────

    private static ThermalTicketModel ConstruirTicketTermico(GuiaRemisionDetalleViewDto view)
    {
        return new ThermalTicketModel
        {
            TituloDocumento = "GUIA DE REMISION",
            NumeroDocumento = view.NumeroCompleto,
            EstadoDocumento = DocumentoAutorizacionHelper.EsEstadoAutorizado(view.Guia.EstadoSRI) ? "Autorizada" : "Emitida",
            FechaEmisionTexto = (view.Guia.Fecha ?? DateTime.Today).ToString("dd/MM/yyyy", Cultura),
            EtiquetaAcceso = DocumentoAutorizacionHelper.ObtenerEtiquetaAcceso(
                DocumentoAutorizacionHelper.EsEstadoAutorizado(view.Guia.EstadoSRI),
                view.Guia.NumAutorizacion),
            ClaveAcceso = DocumentoAutorizacionHelper.ObtenerValorAcceso(
                DocumentoAutorizacionHelper.EsEstadoAutorizado(view.Guia.EstadoSRI),
                view.Guia.NumAutorizacion,
                view.Guia.CodClave),
            EmisorNombre = view.Emisor?.RazonSocial ?? "EMISOR",
            EmisorSecundario = $"RUC: {view.Emisor?.Ruc ?? "-"}",
            TituloItems = "Traslado",
            Bloques =
            [
                new ThermalTicketBlock
                {
                    Titulo = "Transportista",
                    Lineas =
                    [
                        new ThermalTicketLine { Etiqueta = "Nombre", Valor = view.Transportista?.RazonSocial ?? "Transportista" },
                        new ThermalTicketLine { Etiqueta = "Id", Valor = view.Transportista?.NumeroIdentificacion ?? "-" },
                        new ThermalTicketLine { Etiqueta = "Placa", Valor = view.Guia.Placa ?? view.Transportista?.Placa ?? "-" }
                    ]
                },
                new ThermalTicketBlock
                {
                    Titulo = "Destinatario",
                    Lineas =
                    [
                        new ThermalTicketLine { Etiqueta = "Nombre", Valor = view.Destinatario?.RazonSocial ?? "Destinatario" },
                        new ThermalTicketLine { Etiqueta = "Id", Valor = view.Destinatario?.IdDestinatario ?? "-" },
                        new ThermalTicketLine { Etiqueta = "Motivo", Valor = view.Destinatario?.MotivoTraslado ?? "-" }
                    ]
                }
            ],
            Items = view.Detalles.Select(detalle => new ThermalTicketItem
            {
                Descripcion = detalle.Descripcion,
                DetalleSecundario = $"{detalle.CodigoInterno} | Adic. {detalle.CodigoAdicional}",
                CantidadTexto = detalle.Cantidad.ToString("N2", Cultura),
                TotalTexto = detalle.Cantidad.ToString("N2", Cultura)
            }).ToList(),
            Totales =
            [
                new ThermalTicketLine { Etiqueta = "Items", Valor = view.Detalles.Count.ToString(Cultura) },
                new ThermalTicketLine { Etiqueta = "Sustento", Valor = view.NumeroDocumentoSustentoVisual }
            ],
            Notas = view.Guia.Mensaje ?? string.Empty
        };
    }
}
