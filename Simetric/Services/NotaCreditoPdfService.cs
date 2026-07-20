using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;
using BarcodeStandard;
using Simetric.DTOs;
using Simetric.Models;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using SkiaSharp;
using Microsoft.AspNetCore.Hosting;

namespace Simetric.Services;

public interface INotaCreditoPdfService
{
    Task<string> GenerarPdfNotaCreditoAsync(NotaCreditoDetalleViewDto notaCreditoView, FormatoImpresionDocumento formato = FormatoImpresionDocumento.A4);
    Task<string> GenerarPdfNotaCreditoTemporalAsync(NotaCreditoDetalleViewDto notaCreditoView, FormatoImpresionDocumento formato = FormatoImpresionDocumento.A4);
    Task<bool> EliminarPdfNotaCreditoTemporalAsync(string rutaRelativaPdf);
}

public sealed class NotaCreditoPdfService : INotaCreditoPdfService
{
    private static readonly CultureInfo Cultura = new("es-EC");
    private const float FuenteBasePdf = 8.2f;
    private const float FuenteEtiquetaPdf = 7.2f;
    private const float FuenteTituloSeccionPdf = 10.6f;
    private const float PaddingBloquePdf = 7f;
    private const float SpacingBloquePdf = 3f;
    private const float AlturaMinimaInfoEmisorPdf = 180f;
    private readonly IWebHostEnvironment _environment;

    public NotaCreditoPdfService(IWebHostEnvironment environment)
    {
        _environment = environment;
    }

    public Task<string> GenerarPdfNotaCreditoAsync(NotaCreditoDetalleViewDto notaCreditoView, FormatoImpresionDocumento formato = FormatoImpresionDocumento.A4)
    {
        if (notaCreditoView?.NotaCredito == null)
            throw new InvalidOperationException("No se encontró la información necesaria para generar el PDF de la nota de crédito.");

        ValidarDatosAutorizacion(notaCreditoView.NotaCredito);

        var carpeta = Path.Combine(ObtenerWebRootPath(), "notas_de_credito");
        Directory.CreateDirectory(carpeta);

        var ruc = LimpiarSegmentoArchivo(notaCreditoView.Emisor?.Ruc, "nota_credito");
        var numero = LimpiarSegmentoArchivo(notaCreditoView.NotaCredito.NumNotaCredito, notaCreditoView.NotaCredito.Sec.ToString(Cultura));
        var rutaPdf = Path.Combine(carpeta, $"{ruc}_04_{numero.PadLeft(9, '0')}{formato.ObtenerSufijoArchivo()}.pdf");

        var logoSistema = CargarLogoDocumento(notaCreditoView.Emisor);

        Document.Create(container =>
        {
            container.Page(page =>
            {
                if (formato.EsTermico())
                {
                    ThermalPdfComposer.ConfigurePage(page, formato);
                    page.Content().Element(content => ThermalPdfComposer.ComposeTicket(content, ConstruirTicketTermico(notaCreditoView)));
                }
                else
                {
                    page.Size(PageSizes.A4);
                    page.Margin(0.6f, Unit.Centimetre);
                    page.PageColor(Colors.White);
                    page.DefaultTextStyle(EstiloBasePdf);

                    page.Header().Element(header => ComponerEncabezado(header, notaCreditoView, logoSistema));
                    page.Content().Element(content => ComponerContenido(content, notaCreditoView));
                    page.Footer().Element(ComponerPie);
                }
            });
        }).GeneratePdf(rutaPdf);

        return Task.FromResult(rutaPdf);
    }

    public Task<string> GenerarPdfNotaCreditoTemporalAsync(NotaCreditoDetalleViewDto notaCreditoView, FormatoImpresionDocumento formato = FormatoImpresionDocumento.A4)
    {
        if (notaCreditoView?.NotaCredito == null)
            throw new InvalidOperationException("No se encontró la información necesaria para generar el PDF temporal de la nota de crédito.");

        var carpetaTemporal = Path.Combine(ObtenerWebRootPath(), "notas_de_credito", "tmp");
        Directory.CreateDirectory(carpetaTemporal);

        var numero = LimpiarSegmentoArchivo(notaCreditoView.NotaCredito.NumNotaCredito, notaCreditoView.NotaCredito.Sec.ToString(Cultura));
        var nombreTemporal = $"{Guid.NewGuid():N}_{numero.PadLeft(9, '0')}{formato.ObtenerSufijoArchivo()}.pdf";
        var rutaPdf = Path.Combine(carpetaTemporal, nombreTemporal);

        var logoSistema = CargarLogoDocumento(notaCreditoView.Emisor);

        Document.Create(container =>
        {
            container.Page(page =>
            {
                if (formato.EsTermico())
                {
                    ThermalPdfComposer.ConfigurePage(page, formato);
                    page.Content().Element(content => ThermalPdfComposer.ComposeTicket(content, ConstruirTicketTermico(notaCreditoView)));
                }
                else
                {
                    page.Size(PageSizes.A4);
                    page.Margin(0.6f, Unit.Centimetre);
                    page.PageColor(Colors.White);
                    page.DefaultTextStyle(EstiloBasePdf);

                    page.Header().Element(header => ComponerEncabezado(header, notaCreditoView, logoSistema));
                    page.Content().Element(content => ComponerContenido(content, notaCreditoView));
                    page.Footer().Element(ComponerPie);
                }
            });
        }).GeneratePdf(rutaPdf);

        return Task.FromResult($"/notas_de_credito/tmp/{nombreTemporal}");
    }

    public Task<bool> EliminarPdfNotaCreditoTemporalAsync(string rutaRelativaPdf)
    {
        if (string.IsNullOrWhiteSpace(rutaRelativaPdf))
            return Task.FromResult(false);

        var rutaSinQuery = rutaRelativaPdf.Split('?', 2)[0];
        var rutaNormalizada = rutaSinQuery.Replace('\\', '/').TrimStart('/');
        var rutaFisica = Path.Combine(ObtenerWebRootPath(), rutaNormalizada.Replace('/', Path.DirectorySeparatorChar));
        if (!rutaFisica.StartsWith(Path.Combine(ObtenerWebRootPath(), "notas_de_credito"), StringComparison.OrdinalIgnoreCase))
            return Task.FromResult(false);

        if (File.Exists(rutaFisica))
        {
            File.Delete(rutaFisica);
            return Task.FromResult(true);
        }

        return Task.FromResult(false);
    }

    private string ObtenerWebRootPath()
    {
        if (!string.IsNullOrWhiteSpace(_environment.WebRootPath))
            return _environment.WebRootPath;

        return Path.Combine(Directory.GetCurrentDirectory(), "wwwroot");
    }

    private byte[]? CargarLogoDocumento(Emisor? emisor)
        => CargarLogoEmisor(emisor?.LogoImagen);

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

    private static TextStyle EstiloBasePdf(TextStyle style)
        => style
            .FontFamily("Arial")
            .FontSize(FuenteBasePdf)
            .LineHeight(1.1f);

    private static void ComponerEncabezado(IContainer container, NotaCreditoDetalleViewDto view, byte[]? logoSistema)
    {
        var nota = view.NotaCredito;
        var emisor = view.Emisor;
        var estaAutorizada = NotaAutorizada(nota.Autorizado);
        var numeroDocumento = view.NumeroCompleto;
        var numeroAutorizacion = string.IsNullOrWhiteSpace(nota.NumAutorizacion) ? "-" : nota.NumAutorizacion;
        var claveAcceso = ObtenerTextoOGuion(nota.CodClave);
        var ambiente = ObtenerAmbienteVisual(emisor?.TipoAmbiente);
        var tipoEmision = ObtenerTipoEmisionVisualFromEmisor(emisor?.TipoEmision);
        var estadoDocumento = estaAutorizada ? "Autorizada" : "Emitida";

        container.PaddingBottom(6).Row(row =>
        {
            row.Spacing(10);
            row.RelativeItem().Column(column =>
            {
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
                        text.Span("Nota de Crédito").FontColor(Colors.Blue.Darken3).FontSize(12.2f);
                        text.Span($"  No. {numeroDocumento}").FontColor(Colors.Black);
                    });

                    column.Item().Element(item => ComponerLineaEncabezado(item, "Estado del documento:", estadoDocumento));
                    column.Item().PaddingTop(2).Element(item => ComponerCajaDatoEncabezado(item, "Clave de acceso", ObtenerTextoOGuion(claveAcceso)));
                    column.Item().PaddingTop(2).Element(item => ComponerCajaDatoEncabezado(item, "Número de autorización", ObtenerTextoOGuion(numeroAutorizacion)));

                    if (nota.FchAutorizacion.HasValue)
                        column.Item().PaddingTop(2).Text($"Fecha y hora de autorización: {nota.FchAutorizacion.Value:dd/MM/yyyy HH:mm}")
                            .FontSize(8);

                    column.Item().PaddingTop(2).Element(item => ComponerLineaEncabezado(item, "Ambiente:", ambiente));
                    column.Item().Element(item => ComponerLineaEncabezado(item, "Tipo emisión:", tipoEmision));

                    column.Item().PaddingTop(2).Element(item => ComponerLineaEncabezado(item, "Documento modificado:", view.NumeroDocModificadoVisual));
                    if (nota.FechaEmiDocModificado.HasValue)
                        column.Item().Element(item => ComponerLineaEncabezado(item, "Fecha sustento:", nota.FechaEmiDocModificado.Value.ToString("dd/MM/yyyy")));

                    var barcodeBytes = GenerarBarcodeNumeroAutorizacion(nota.CodClave);
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

    private static void ComponerContenido(IContainer container, NotaCreditoDetalleViewDto view)
    {
        var nota = view.NotaCredito;
        var cliente = view.Cliente;
        var detalles = view.Detalles;

        container.Column(column =>
        {
            column.Spacing(4);

            column.Item().Element(card => ComponerBloqueCliente(card, cliente, view));

            column.Item().Element(table => ComponerDetalle(table, detalles));

            column.Item().ShowEntire().Row(row =>
            {
                row.Spacing(8);
                row.RelativeItem(0.95f).Element(card => ComponerBloqueMotivoYAdicional(card, view));

                row.RelativeItem(1.25f).Element(card => ComponerBloqueResumen(
                    card,
                    nota,
                    detalles));
            });

            column.Item().PaddingTop(1).Text("Documento generado por el sistema de facturacion electronica.")
                .FontSize(FuenteEtiquetaPdf)
                .FontColor(Colors.Grey.Darken1);
        });
    }

    private static void ComponerBloqueCliente(IContainer container, Cliente? cliente, NotaCreditoDetalleViewDto view)
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
                column.Item().Element(item => ComponerParDato(item, "Fecha emision", (view.NotaCredito.FchAutorizacion ?? DateTime.Now).ToString("dd/MM/yyyy")));
            });
    }

    private static void ComponerBloqueMotivoYAdicional(IContainer container, NotaCreditoDetalleViewDto view)
    {
        var nota = view.NotaCredito;

        container.Column(column =>
        {
            column.Spacing(4);

            column.Item().Element(card => ComponerCajaTexto(
                card, 
                "Motivo de la nota de crédito", 
                string.IsNullOrWhiteSpace(nota.Motivo) ? "No especificado" : nota.Motivo));
        });
    }

    private static void ComponerBloqueResumen(
        IContainer container,
        NotaCredito nota,
        IReadOnlyCollection<NotaCreditoDetalleLineaDto> detalles)
    {
        var subtotal15 = detalles.Where(x => x.TarifaIva > 0 || x.ValorIva > 0).Sum(x => x.Subtotal);
        var subtotal0 = detalles.Where(x => x.TarifaIva == 0 && x.ValorIva == 0).Sum(x => x.Subtotal);
        
        if (!detalles.Any())
        {
            subtotal15 = (nota.Iva ?? 0m) > 0m ? (nota.Subtotal ?? 0m) : 0m;
            subtotal0 = (nota.Iva ?? 0m) == 0m ? (nota.Subtotal ?? 0m) : 0m;
        }

        var subtotalNoObjeto = 0m;
        var subtotalExento = 0m;
        var subtotalSinImpuestos = subtotal15 + subtotal0 + subtotalNoObjeto + subtotalExento;
        var descuentos = nota.Descuentos ?? detalles.Sum(x => x.Descuento);
        var subtotalConDescuento = subtotalSinImpuestos - descuentos;
        var ice = 0m;
        var iva15 = nota.Iva ?? detalles.Sum(x => x.ValorIva);
        var servicio10 = 0m;
        var total = nota.ValorTotal ?? detalles.Sum(x => x.Total);

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

                    AgregarFila("Subtotal 15%", FormatearMoneda(subtotal15));
                    AgregarFila("Subtotal 0%", FormatearMoneda(subtotal0));
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

    private static void ComponerDetalle(IContainer container, IReadOnlyCollection<NotaCreditoDetalleLineaDto> detalles)
    {
        container.Border(1)
            .BorderColor(Colors.Blue.Lighten4)
            .Padding(PaddingBloquePdf)
            .Column(column =>
            {
                column.Item().Text("Detalle de la nota de crédito")
                    .FontSize(FuenteTituloSeccionPdf)
                    .SemiBold()
                    .FontColor(Colors.Blue.Darken3);

                column.Item().PaddingTop(3).Table(table =>
                {
                    table.ColumnsDefinition(columns =>
                    {
                        columns.ConstantColumn(24);
                        columns.RelativeColumn(1.0f);
                        columns.RelativeColumn(2.5f);
                        columns.RelativeColumn(0.8f);
                        columns.RelativeColumn(0.9f);
                        columns.RelativeColumn(0.9f);
                        columns.RelativeColumn(0.9f);
                        columns.RelativeColumn(1.0f);
                    });

                    table.Header(header =>
                    {
                        header.Cell().Element(CellHeader).AlignCenter().Text("#").SemiBold().FontColor(Colors.White);
                        header.Cell().Element(CellHeader).Text("Codigo").SemiBold().FontColor(Colors.White);
                        header.Cell().Element(CellHeader).Text("Descripcion").SemiBold().FontColor(Colors.White);
                        header.Cell().Element(CellHeader).AlignRight().Text("Cant.").SemiBold().FontColor(Colors.White);
                        header.Cell().Element(CellHeader).AlignRight().Text("P. Unit").SemiBold().FontColor(Colors.White);
                        header.Cell().Element(CellHeader).AlignRight().Text("Desc.").SemiBold().FontColor(Colors.White);
                        header.Cell().Element(CellHeader).AlignRight().Text("IVA").SemiBold().FontColor(Colors.White);
                        header.Cell().Element(CellHeader).AlignRight().Text("Total").SemiBold().FontColor(Colors.White);
                    });

                    if (!detalles.Any())
                    {
                        table.Cell().ColumnSpan(8).Element(CellBody).AlignCenter().PaddingVertical(16)
                            .Text("No hay detalles registrados para esta nota de crédito.");
                    }
                    else
                    {
                        var index = 1;
                        foreach (var detalle in detalles)
                        {
                            table.Cell().Element(CellBody).AlignCenter().Text(index.ToString());
                            table.Cell().Element(CellBody).Text(detalle.CodigoInterno);
                            table.Cell().Element(CellBody).Text(detalle.Descripcion);
                            table.Cell().Element(CellBody).AlignRight().Text(FormatearNumero(detalle.Cantidad));
                            table.Cell().Element(CellBody).AlignRight().Text(FormatearMoneda(detalle.PrecioUnitario));
                            table.Cell().Element(CellBody).AlignRight().Text(FormatearMoneda(detalle.Descuento));
                            table.Cell().Element(CellBody).AlignRight().Text(FormatearMoneda(detalle.ValorIva));
                            table.Cell().Element(CellBody).AlignRight().Text(FormatearMoneda(detalle.Total));
                            index++;
                        }
                    }

                    static IContainer CellHeader(IContainer container) =>
                        container.Background(Colors.Blue.Darken3).PaddingVertical(7).PaddingHorizontal(5);

                    static IContainer CellBody(IContainer container) =>
                        container.BorderBottom(1).BorderColor(Colors.Grey.Lighten2).PaddingVertical(7).PaddingHorizontal(5);
                });
            });
    }

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

    private static bool NotaAutorizada(string? autorizado)
    {
        if (string.IsNullOrWhiteSpace(autorizado))
            return false;

        var valor = autorizado.Trim().ToLowerInvariant();
        return valor is "1" or "true" or "t" or "s" or "si" or "sí" or "a" or "autorizado";
    }

    private static string ObtenerNombreCliente(Cliente? cliente)
    {
        if (cliente == null)
            return "Cliente";

        if (!string.IsNullOrWhiteSpace(cliente.Nombrerazonsocial))
            return cliente.Nombrerazonsocial.Trim();

        var nombre = $"{cliente.Nombres} {cliente.Apellidos}".Trim();
        if (!string.IsNullOrWhiteSpace(nombre))
            return nombre;

        if (!string.IsNullOrWhiteSpace(cliente.Nombrecomercial))
            return cliente.Nombrecomercial.Trim();

        return "Cliente";
    }

    private static void ValidarDatosAutorizacion(NotaCredito notaCredito)
    {
        if (!NotaAutorizada(notaCredito.Autorizado))
            return;

        if (string.IsNullOrWhiteSpace(notaCredito.CodClave) ||
            string.IsNullOrWhiteSpace(notaCredito.NumAutorizacion) ||
            !notaCredito.FchAutorizacion.HasValue)
        {
            throw new InvalidOperationException(
                "La nota de crédito está autorizada pero no tiene completos los datos de autorización (clave de acceso, número o fecha).");
        }
    }

    private static string FormatearMoneda(decimal valor)
        => $"${valor.ToString("N2", Cultura)}";

    private static string FormatearNumero(decimal valor)
        => valor.ToString("N2", Cultura);

    private static ThermalTicketModel ConstruirTicketTermico(NotaCreditoDetalleViewDto view)
    {
        var nota = view.NotaCredito;

        return new ThermalTicketModel
        {
            TituloDocumento = "NOTA DE CREDITO",
            NumeroDocumento = view.NumeroCompleto,
            EstadoDocumento = NotaAutorizada(nota.Autorizado) ? "AUTORIZADA" : "EMITIDA",
            FechaEmisionTexto = nota.FechaEmiDocModificado?.ToString("dd/MM/yyyy", Cultura) ?? string.Empty,
            EtiquetaAcceso = DocumentoAutorizacionHelper.ObtenerEtiquetaAcceso(
                NotaAutorizada(nota.Autorizado),
                nota.NumAutorizacion),
            ClaveAcceso = DocumentoAutorizacionHelper.ObtenerValorAcceso(
                NotaAutorizada(nota.Autorizado),
                nota.NumAutorizacion,
                nota.CodClave),
            EmisorNombre = view.Emisor?.RazonSocial ?? "EMISOR",
            EmisorSecundario = $"RUC: {view.Emisor?.Ruc ?? "-"}",
            TituloItems = "Ajustes",
            Bloques =
            [
                new ThermalTicketBlock
                {
                    Titulo = "Cliente",
                    Lineas =
                    [
                        new ThermalTicketLine { Etiqueta = "Nombre", Valor = ObtenerNombreCliente(view.Cliente) },
                        new ThermalTicketLine { Etiqueta = "Id", Valor = view.Cliente?.Numeroidentificacion ?? "-" },
                        new ThermalTicketLine { Etiqueta = "Doc. mod.", Valor = view.NumeroDocModificadoVisual }
                    ]
                }
            ],
            Items = view.Detalles.Select(detalle => new ThermalTicketItem
            {
                Descripcion = detalle.Descripcion,
                DetalleSecundario = $"{detalle.CodigoInterno} | Unit {FormatearMoneda(detalle.PrecioUnitario)}",
                CantidadTexto = FormatearNumero(detalle.Cantidad),
                TotalTexto = FormatearMoneda(detalle.Total)
            }).ToList(),
            Totales =
            [
                new ThermalTicketLine { Etiqueta = "Subtotal", Valor = FormatearMoneda(nota.Subtotal ?? 0m) },
                new ThermalTicketLine { Etiqueta = "Descuentos", Valor = FormatearMoneda(nota.Descuentos ?? 0m) },
                new ThermalTicketLine { Etiqueta = "IVA", Valor = FormatearMoneda(nota.Iva ?? 0m) },
                new ThermalTicketLine { Etiqueta = "TOTAL", Valor = FormatearMoneda(nota.ValorTotal ?? 0m) }
            ],
            Notas = string.IsNullOrWhiteSpace(nota.Observacion)
                ? (nota.Motivo ?? string.Empty)
                : $"{nota.Motivo} | {nota.Observacion}"
        };
    }

    private static string LimpiarSegmentoArchivo(string? valor, string reemplazo)
    {
        var limpio = string.IsNullOrWhiteSpace(valor) ? reemplazo : valor.Trim();
        foreach (var caracter in Path.GetInvalidFileNameChars())
            limpio = limpio.Replace(caracter, '_');

        return limpio.Replace(" ", "_");
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

    private static string ObtenerTextoOGuion(string? valor)
        => string.IsNullOrWhiteSpace(valor) ? "-" : valor.Trim();

    private static string ObtenerAmbienteVisual(string? ambienteEmisor)
        => int.TryParse(ambienteEmisor, out var ambienteConfig)
            ? ambienteConfig == 1 ? "Pruebas" : ambienteConfig == 2 ? "Producción" : "-"
            : "-";

    private static string ObtenerTipoEmisionVisualFromEmisor(string? tipoEmision)
    {
        if (string.IsNullOrWhiteSpace(tipoEmision))
            return "Normal";
        var t = tipoEmision.Trim();
        if (t.Equals("NORMAL", StringComparison.OrdinalIgnoreCase))
            return "Normal";
        return t;
    }

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

    private static string FormatearTextoCasing(string? valor)
    {
        if (string.IsNullOrWhiteSpace(valor))
            return "-";

        var trim = valor.Trim();
        bool tieneMinusculas = trim.Any(char.IsLower);
        if (tieneMinusculas)
            return trim;

        var palabras = trim.ToLowerInvariant().Split(' ', StringSplitOptions.RemoveEmptyEntries);
        var palabrasCapitalizadas = palabras.Select(p => p.Length == 1 ? p.ToUpperInvariant() : char.ToUpperInvariant(p[0]) + p[1..]);
        return string.Join(' ', palabrasCapitalizadas);
    }
}
