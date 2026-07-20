using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;
using BarcodeStandard;
using Simetric.DTOs;
using Simetric.Models;
using System.Globalization;
using SkiaSharp;

namespace Simetric.Services;

public interface IRetencionPdfService
{
    Task<string> GenerarPdfRetencionAsync(RetencionGeneradaDetalleViewDto retencionView, FormatoImpresionDocumento formato = FormatoImpresionDocumento.A4);
    Task<string> GenerarPdfRetencionTemporalAsync(RetencionGeneradaDetalleViewDto retencionView, FormatoImpresionDocumento formato = FormatoImpresionDocumento.A4);
    Task<bool> EliminarPdfRetencionTemporalAsync(string rutaRelativaPdf);
}

public sealed class RetencionPdfService : IRetencionPdfService
{
    private static readonly CultureInfo Cultura = new("es-EC");
    private readonly IWebHostEnvironment _environment;
    private const float FuenteBasePdf = 8.3f;
    private const float FuenteEtiquetaPdf = 7.2f;
    private const float FuenteTituloPdf = 11f;
    private const float PaddingPanelPdf = 7f;
    private const float AlturaMinimaEncabezadoA4 = 230f;
    private const float AlturaMinimaPanelEmisorA4 = 150f;

    public RetencionPdfService(IWebHostEnvironment environment)
    {
        _environment = environment;
    }

    public Task<string> GenerarPdfRetencionAsync(RetencionGeneradaDetalleViewDto retencionView, FormatoImpresionDocumento formato = FormatoImpresionDocumento.A4)
    {
        if (retencionView?.RetencionInfo == null)
            throw new InvalidOperationException("No se encontró la información necesaria para generar el PDF de la retención.");

        var carpeta = Path.Combine(ObtenerWebRootPath(), "retenciones");
        Directory.CreateDirectory(carpeta);

        var ruc = LimpiarSegmentoArchivo(retencionView.Emisor?.Ruc, "retencion");
        var numero = LimpiarSegmentoArchivo(retencionView.RetencionInfo.NumRetencion, retencionView.RetencionInfo.Sec.ToString(Cultura));
        var rutaPdf = Path.Combine(carpeta, $"{ruc}_07_{numero.PadLeft(9, '0')}{formato.ObtenerSufijoArchivo()}.pdf");

        rutaPdf = GenerarPdfRetencionSeguro(retencionView, formato, rutaPdf);

        return Task.FromResult(rutaPdf);
    }

    public Task<string> GenerarPdfRetencionTemporalAsync(RetencionGeneradaDetalleViewDto retencionView, FormatoImpresionDocumento formato = FormatoImpresionDocumento.A4)
    {
        if (retencionView?.RetencionInfo == null)
            throw new InvalidOperationException("No se encontrÃ³ la informaciÃ³n necesaria para generar el PDF temporal de la retenciÃ³n.");

        var carpetaTemporal = Path.Combine(ObtenerWebRootPath(), "retenciones", "tmp");
        Directory.CreateDirectory(carpetaTemporal);

        var numero = LimpiarSegmentoArchivo(
            retencionView.RetencionInfo.NumRetencion,
            retencionView.RetencionInfo.Sec > 0 ? retencionView.RetencionInfo.Sec.ToString(Cultura) : Guid.NewGuid().ToString("N"));
        var nombreTemporal = $"{Guid.NewGuid():N}_{numero.PadLeft(9, '0')}{formato.ObtenerSufijoArchivo()}.pdf";
        var rutaPdf = Path.Combine(carpetaTemporal, nombreTemporal);

        GenerarPdfRetencionInterno(retencionView, formato, rutaPdf);

        return Task.FromResult($"/retenciones/tmp/{nombreTemporal}");
    }

    public Task<bool> EliminarPdfRetencionTemporalAsync(string rutaRelativaPdf)
    {
        if (string.IsNullOrWhiteSpace(rutaRelativaPdf))
            return Task.FromResult(false);

        var rutaNormalizada = rutaRelativaPdf.Replace('\\', '/').TrimStart('/');
        var rutaFisica = Path.Combine(ObtenerWebRootPath(), rutaNormalizada.Replace('/', Path.DirectorySeparatorChar));
        if (!rutaFisica.StartsWith(Path.Combine(ObtenerWebRootPath(), "retenciones"), StringComparison.OrdinalIgnoreCase))
            return Task.FromResult(false);

        if (File.Exists(rutaFisica))
        {
            File.Delete(rutaFisica);
            return Task.FromResult(true);
        }

        return Task.FromResult(false);
    }

    private void GenerarPdfRetencionInterno(RetencionGeneradaDetalleViewDto retencionView, FormatoImpresionDocumento formato, string rutaPdf)
    {
        var logoSistema = CargarLogoRetencion(retencionView.Emisor?.LogoImagen);

        Document.Create(container =>
        {
            container.Page(page =>
            {
                if (formato.EsTermico())
                {
                    ThermalPdfComposer.ConfigurePage(page, formato);
                    page.Content().Element(content => ThermalPdfComposer.ComposeTicket(content, ConstruirTicketTermico(retencionView)));
                }
                else
                {
                    page.Size(PageSizes.A4);
                    page.Margin(0.75f, Unit.Centimetre);
                    page.PageColor(Colors.White);
                    page.DefaultTextStyle(x => x.FontFamily("Arial").FontSize(FuenteBasePdf).LineHeight(1.1f));

                    page.Header().Element(header => ComponerEncabezadoA4(header, retencionView, logoSistema));
                    page.Content().Element(content => ComponerContenidoA4(content, retencionView));
                    page.Footer().Element(ComponerPie);
                }
            });
        }).GeneratePdf(rutaPdf);
    }

    private string GenerarPdfRetencionSeguro(RetencionGeneradaDetalleViewDto retencionView, FormatoImpresionDocumento formato, string rutaPdf)
    {
        var carpeta = Path.GetDirectoryName(rutaPdf) ?? ObtenerWebRootPath();
        Directory.CreateDirectory(carpeta);

        var nombreBase = Path.GetFileNameWithoutExtension(rutaPdf);
        var extension = Path.GetExtension(rutaPdf);
        var rutaTemporal = Path.Combine(carpeta, $"{nombreBase}_{Guid.NewGuid():N}.tmp");

        GenerarPdfRetencionInterno(retencionView, formato, rutaTemporal);

        try
        {
            File.Copy(rutaTemporal, rutaPdf, overwrite: true);
            File.Delete(rutaTemporal);
            return rutaPdf;
        }
        catch (IOException)
        {
            var rutaVersionada = Path.Combine(carpeta, $"{nombreBase}_{DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()}{extension}");
            File.Move(rutaTemporal, rutaVersionada);
            return rutaVersionada;
        }
        catch (UnauthorizedAccessException)
        {
            var rutaVersionada = Path.Combine(carpeta, $"{nombreBase}_{DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()}{extension}");
            File.Move(rutaTemporal, rutaVersionada);
            return rutaVersionada;
        }
    }

    private string ObtenerWebRootPath()
    {
        if (!string.IsNullOrWhiteSpace(_environment.WebRootPath))
            return _environment.WebRootPath;

        return Path.Combine(Directory.GetCurrentDirectory(), "wwwroot");
    }

    private byte[]? CargarLogoRetencion(string? logoImagen)
    {
        if (!string.IsNullOrWhiteSpace(logoImagen))
        {
            var logo = logoImagen.Trim();
            var prefijosBase64 = new[]
            {
                "data:image/jpeg;base64,",
                "data:image/jpg;base64,",
                "data:image/png;base64,",
                "data:image/webp;base64,"
            };

            if (prefijosBase64.Any(prefijo => logo.StartsWith(prefijo, StringComparison.OrdinalIgnoreCase)))
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

            var normalizado = logo.Replace('/', Path.DirectorySeparatorChar).TrimStart(Path.DirectorySeparatorChar);
            var rutas = new[]
            {
                Path.Combine(ObtenerWebRootPath(), normalizado),
                Path.Combine(Directory.GetCurrentDirectory(), normalizado),
                logo
            };

            foreach (var ruta in rutas.Where(File.Exists))
                return File.ReadAllBytes(ruta);
        }

        return null;
    }

    private static void ComponerEncabezadoA4(IContainer container, RetencionGeneradaDetalleViewDto view, byte[]? logoSistema)
    {
        var retencion = view.RetencionInfo;
        var emisor = view.Emisor;
        var estaAutorizada = RetencionAutorizada(retencion.Autorizado, retencion.Estado);
        var etiquetaAcceso = DocumentoAutorizacionHelper.ObtenerEtiquetaAcceso(estaAutorizada, retencion.NumAutorizacion);
        var valorAcceso = DocumentoAutorizacionHelper.ObtenerValorAcceso(estaAutorizada, retencion.NumAutorizacion, retencion.Clave);
        var barcodeAcceso = GenerarBarcodeAcceso(valorAcceso);

        container.PaddingBottom(8).Row(row =>
        {
            row.Spacing(12);

            row.RelativeItem().MinHeight(AlturaMinimaEncabezadoA4).Column(column =>
            {
                if (logoSistema != null)
                {
                    column.Item()
                        .AlignCenter()
                        .MaxHeight(78)
                        .MaxWidth(160)
                        .PaddingBottom(8)
                        .Image(logoSistema)
                        .FitArea();
                }
                else
                {
                    column.Item()
                        .AlignCenter()
                        .PaddingBottom(8)
                        .Text("NO TIENE LOGO")
                        .FontSize(28f)
                        .Bold()
                        .FontColor(Colors.Red.Medium);
                }

                column.Item().Element(card => ComponerPanelEmisorA4(card, emisor));
            });

            row.ConstantItem(285).MinHeight(AlturaMinimaEncabezadoA4).Column(column =>
            {
                column.Spacing(5);
                column.Item()
                    .Background(Colors.Blue.Lighten5)
                    .PaddingVertical(6)
                    .PaddingHorizontal(8)
                    .Row(titulo =>
                    {
                        titulo.RelativeItem().Text("COMPROBANTE DE RETENCION")
                            .FontSize(FuenteTituloPdf)
                            .Bold()
                            .FontColor(Colors.Blue.Darken3);
                        titulo.RelativeItem().AlignRight().Text($"No. {ObtenerTextoOGuion(view.NumeroCompleto)}")
                            .FontSize(10.5f)
                            .Bold();
                    });

                column.Item()
                    .Border(1)
                    .BorderColor(Colors.Blue.Lighten4)
                    .Background(Colors.Blue.Lighten5)
                    .Padding(PaddingPanelPdf)
                    .Column(autorizacion =>
                    {
                        autorizacion.Spacing(3);
                        autorizacion.Item().Element(item => ComponerDatoApiladoA4(item, etiquetaAcceso, valorAcceso));
                        autorizacion.Item().Element(item => ComponerDatoApiladoA4(item, "Fecha y hora de Autorizacion:", ObtenerFechaAutorizacionTexto(retencion)));
                        autorizacion.Item().Element(item => ComponerDatoLineaA4(item, "Ambiente:", ObtenerAmbiente(retencion, view.Compra)));
                        autorizacion.Item().Element(item => ComponerDatoLineaA4(item, "Emision:", "NORMAL"));

                        if (!string.IsNullOrWhiteSpace(valorAcceso))
                        {
                            autorizacion.Item().PaddingTop(2).Text("Clave de Acceso:")
                                .FontSize(FuenteBasePdf)
                                .Bold();

                            if (estaAutorizada && barcodeAcceso != null)
                            {
                                autorizacion.Item()
                                    .Background(Colors.White)
                                    .Padding(3)
                                    .Height(62)
                                    .Image(barcodeAcceso)
                                    .FitWidth();
                            }

                            autorizacion.Item().Text(valorAcceso)
                                .FontSize(6.6f)
                                .FontColor(Colors.Grey.Darken2);
                        }
                    });
            });
        });
    }

    private static void ComponerPanelEmisorA4(IContainer container, Simetric.Models.Emisor? emisor)
    {
        container.Border(1)
            .BorderColor(Colors.Blue.Lighten4)
            .Background(Colors.Blue.Lighten5)
            .MinHeight(AlturaMinimaPanelEmisorA4)
            .Padding(PaddingPanelPdf)
            .Column(column =>
            {
                column.Spacing(2);
                column.Item().Element(item => ComponerDatoLineaA4(item, "Emisor:", emisor?.RazonSocial ?? "EMISOR"));
                column.Item().Element(item => ComponerDatoLineaA4(item, "RUC:", emisor?.Ruc));
                column.Item().Element(item => ComponerDatoLineaA4(item, "Matriz:", emisor?.DireccionMatriz ?? emisor?.Direccion));

                if (!string.IsNullOrWhiteSpace(emisor?.Email))
                    column.Item().Element(item => ComponerDatoLineaA4(item, "Correo:", emisor.Email));

                if (!string.IsNullOrWhiteSpace(emisor?.Telefono))
                    column.Item().Element(item => ComponerDatoLineaA4(item, "Telefono:", emisor.Telefono));

                column.Item().Element(item => ComponerDatoLineaA4(item, "Obligado a llevar contabilidad:", emisor?.LlevaContabilidad ?? "NO"));

                if (!string.IsNullOrWhiteSpace(emisor?.Resolusion))
                {
                    column.Item().Element(item => ComponerDatoLineaA4(item, "Agente de Retencion", string.Empty));
                    column.Item().Element(item => ComponerDatoLineaA4(item, "Resolucion Nro.", emisor.Resolusion));
                }
            });
    }

    private static void ComponerContenidoA4(IContainer container, RetencionGeneradaDetalleViewDto view)
    {
        container.Column(column =>
        {
            column.Spacing(8);
            column.Item().Element(card => ComponerBloqueProveedorA4(card, view));
            column.Item().Element(card => ComponerDetalleA4(card, view));
        });
    }

    private static void ComponerBloqueProveedorA4(IContainer container, RetencionGeneradaDetalleViewDto view)
    {
        var retencion = view.RetencionInfo;
        var proveedor = view.Proveedor;

        container.Border(1)
            .BorderColor(Colors.Blue.Lighten4)
            .Background(Colors.Blue.Lighten5)
            .Padding(PaddingPanelPdf)
            .Row(row =>
            {
                row.Spacing(14);
                row.RelativeItem(1.55f).Column(column =>
                {
                    column.Spacing(3);
                    column.Item().Element(item => ComponerDatoLineaA4(item, "Razon Social:", ObtenerNombreProveedor(view)));
                    column.Item().Element(item => ComponerDatoLineaA4(item, "Direccion:", proveedor?.direccion));
                    column.Item().Element(item => ComponerDatoLineaA4(item, "Fecha Emision:", FormatearFecha(retencion.Fecha)));
                    column.Item().Element(item => ComponerDatoLineaA4(item, "Ejercicio Fiscal:", retencion.PeriodoFiscal));
                });

                row.RelativeItem().Column(column =>
                {
                    column.Spacing(3);
                    column.Item().Element(item => ComponerDatoLineaA4(item, "RUC/CI:", proveedor?.ruc ?? retencion.IdCliente));
                    column.Item().Element(item => ComponerDatoLineaA4(item, "Telefono:", ObtenerTelefonoProveedor(proveedor)));
                    column.Item().Element(item => ComponerDatoLineaA4(item, "Correo:", proveedor?.email));
                });
            });
    }

    private static void ComponerDetalleA4(IContainer container, RetencionGeneradaDetalleViewDto view)
    {
        var detalles = OrdenarDetallesRetencion(view.Retenciones).ToList();

        container.Border(1)
            .BorderColor(Colors.Blue.Lighten4)
            .Padding(0)
            .Table(table =>
            {
                table.ColumnsDefinition(columns =>
                {
                    columns.ConstantColumn(72);
                    columns.ConstantColumn(88);
                    columns.ConstantColumn(58);
                    columns.ConstantColumn(76);
                    columns.ConstantColumn(58);
                    columns.ConstantColumn(46);
                    columns.ConstantColumn(70);
                    columns.ConstantColumn(84);
                });

                table.Header(header =>
                {
                    header.Cell().Element(CellHeader).Text("Comprobante").SemiBold();
                    header.Cell().Element(CellHeader).Text("Numero").SemiBold();
                    header.Cell().Element(CellHeader).Text("Fecha Emision").SemiBold();
                    header.Cell().Element(CellHeader).AlignRight().Text("Base Imponible para la Retencion").SemiBold();
                    header.Cell().Element(CellHeader).Text("Impuesto").SemiBold();
                    header.Cell().Element(CellHeader).AlignCenter().Text("Codigo").SemiBold();
                    header.Cell().Element(CellHeader).AlignRight().Text("Porcentaje Retencion").SemiBold();
                    header.Cell().Element(CellHeader).AlignRight().Text("Valor Retenido").SemiBold();
                });

                if (!detalles.Any())
                {
                    table.Cell().ColumnSpan(8).Element(CellBody).AlignCenter().PaddingVertical(16)
                        .Text("No hay lineas registradas para esta retencion.");
                    return;
                }

                var comprobante = ObtenerTipoComprobanteSustento(view);
                var numeroSustento = ObtenerNumeroSustento(view);
                var fechaSustento = FormatearFecha(view.FechaEmisionDocumentoSustento);

                foreach (var detalle in detalles)
                {
                    table.Cell().Element(CellBody).Text(comprobante);
                    table.Cell().Element(CellBody).Text(numeroSustento);
                    table.Cell().Element(CellBody).Text(fechaSustento);
                    table.Cell().Element(CellBody).AlignRight().Text(FormatearNumero(detalle.BaseImponible));
                    table.Cell().Element(CellBody).Text(ObtenerNombreImpuesto(detalle.Tipo));
                    table.Cell().Element(CellBody).AlignCenter().Text(detalle.CodigoRetencion);
                    table.Cell().Element(CellBody).AlignRight().Text(FormatearPorcentaje(detalle.PorcentajeRetener));
                    table.Cell().Element(CellBody).AlignRight().Text(FormatearMoneda(detalle.ValorRetenido));
                }

                table.Cell().ColumnSpan(6).Element(CellTotalSpacer).Text(string.Empty);
                table.Cell().Element(CellTotalLabel).AlignRight().Text("Total Retenido:")
                    .SemiBold()
                    .FontSize(FuenteBasePdf);
                table.Cell().Element(CellTotalValue).AlignRight().Text(FormatearMoneda(view.TotalRetenido))
                    .SemiBold()
                    .FontSize(FuenteBasePdf);

                static IContainer CellHeader(IContainer container) =>
                    container
                        .Background(Colors.Blue.Lighten5)
                        .BorderBottom(1)
                        .BorderColor(Colors.Blue.Lighten3)
                        .PaddingVertical(5)
                        .PaddingHorizontal(4)
                        .DefaultTextStyle(x => x.FontSize(FuenteEtiquetaPdf).FontColor(Colors.Black));

                static IContainer CellBody(IContainer container) =>
                    container
                        .BorderRight(1)
                        .BorderBottom(1)
                        .BorderColor(Colors.Grey.Lighten2)
                        .PaddingVertical(5)
                        .PaddingHorizontal(4)
                        .DefaultTextStyle(x => x.FontSize(FuenteEtiquetaPdf));

                static IContainer CellTotalSpacer(IContainer container) =>
                    container
                        .BorderTop(1)
                        .BorderColor(Colors.Blue.Lighten3)
                        .PaddingVertical(5)
                        .PaddingHorizontal(4);

                static IContainer CellTotalLabel(IContainer container) =>
                    container
                        .Background(Colors.Blue.Lighten5)
                        .BorderTop(1)
                        .BorderColor(Colors.Blue.Lighten3)
                        .PaddingVertical(5)
                        .PaddingHorizontal(4);

                static IContainer CellTotalValue(IContainer container) =>
                    container
                        .Background(Colors.Blue.Lighten5)
                        .BorderTop(1)
                        .BorderColor(Colors.Blue.Lighten3)
                        .PaddingVertical(5)
                        .PaddingHorizontal(4);
            });
    }

    private static void ComponerEncabezado(IContainer container, RetencionGeneradaDetalleViewDto view, byte[]? logoSistema)
    {
        var retencion = view.RetencionInfo;
        var emisor = view.Emisor;
        var estaAutorizada = RetencionAutorizada(retencion.Autorizado, retencion.Estado);
        var estadoDocumento = estaAutorizada ? "AUTORIZADA" : "EMITIDA";
        var etiquetaAcceso = DocumentoAutorizacionHelper.ObtenerEtiquetaAcceso(estaAutorizada, retencion.NumAutorizacion);
        var valorAcceso = DocumentoAutorizacionHelper.ObtenerValorAcceso(estaAutorizada, retencion.NumAutorizacion, retencion.Clave);
        var barcodeAcceso = GenerarBarcodeAcceso(valorAcceso);

        container.PaddingBottom(14).Row(row =>
        {
            row.RelativeItem().Column(column =>
            {
                if (logoSistema != null)
                {
                    column.Item()
                        .MaxWidth(110)
                        .Image(logoSistema)
                        .FitWidth();
                }

                column.Item().PaddingTop(logoSistema != null ? 8 : 0).Text(emisor?.RazonSocial ?? "EMISOR")
                    .FontSize(16)
                    .SemiBold()
                    .FontColor(Colors.Blue.Darken3);

                if (!string.IsNullOrWhiteSpace(emisor?.NomComercial))
                    column.Item().PaddingTop(4).Text($"Nombre comercial: {emisor.NomComercial}").FontColor(Colors.Grey.Darken2);

                if (!string.IsNullOrWhiteSpace(emisor?.Ruc))
                    column.Item().PaddingTop(2).Text($"RUC: {emisor.Ruc}").FontColor(Colors.Grey.Darken2);

                if (!string.IsNullOrWhiteSpace(emisor?.DireccionMatriz))
                    column.Item().PaddingTop(2).Text($"Dirección matriz: {emisor.DireccionMatriz}").FontColor(Colors.Grey.Darken2);
            });

            row.ConstantItem(230).Border(1).BorderColor(Colors.Grey.Lighten2).Padding(14).Column(column =>
            {
                column.Item().AlignCenter().Text("COMPROBANTE DE RETENCIÓN")
                    .FontSize(14)
                    .SemiBold()
                    .FontColor(Colors.Blue.Darken3);

                column.Item().PaddingTop(8).AlignCenter().Text(view.NumeroCompleto)
                    .FontSize(12)
                    .SemiBold();

                column.Item().PaddingTop(10).Text($"Estado del documento: {estadoDocumento}");
                column.Item().PaddingTop(2).Text($"Fecha emisión: {(retencion.Fecha ?? DateTime.Today):dd/MM/yyyy}");
                column.Item().PaddingTop(2).Text($"Periodo fiscal: {retencion.PeriodoFiscal ?? "-"}");

                if (MostrarNumeroAutorizacion(retencion.NumAutorizacion))
                    column.Item().PaddingTop(2).Text($"Numero de autorizacion: {retencion.NumAutorizacion}");

                if (!string.IsNullOrWhiteSpace(valorAcceso))
                {
                    column.Item().PaddingTop(8).Background(Colors.Grey.Lighten4).Padding(8).Column(acceso =>
                    {
                        acceso.Item().Text(etiquetaAcceso)
                            .FontSize(8)
                            .SemiBold()
                            .FontColor(Colors.Blue.Darken3);
                        acceso.Item().PaddingTop(2).Text(valorAcceso).FontSize(8);

                        if (barcodeAcceso != null)
                        {
                            acceso.Item().PaddingTop(6)
                                .Height(44)
                                .Image(barcodeAcceso)
                                .FitWidth();
                        }
                    });
                }
            });
        });
    }

    private static void ComponerContenido(IContainer container, RetencionGeneradaDetalleViewDto view)
    {
        container.Column(column =>
        {
            column.Spacing(14);

            column.Item().Row(row =>
            {
                row.RelativeItem().Element(card => ComponerBloqueProveedor(card, view));
                row.ConstantItem(220).Element(card => ComponerBloqueResumen(card, view));
            });

            column.Item().Element(card => ComponerBloqueDocumentoSustento(card, view));
            column.Item().Element(card => ComponerDetalle(card, view));
        });
    }

    private static void ComponerBloqueProveedor(IContainer container, RetencionGeneradaDetalleViewDto view)
    {
        container.Border(1)
            .BorderColor(Colors.Grey.Lighten2)
            .Padding(14)
            .Column(column =>
            {
                column.Spacing(6);
                column.Item().Text("Datos del proveedor")
                    .FontSize(12)
                    .SemiBold()
                    .FontColor(Colors.Blue.Darken3);

                column.Item().Text(ObtenerNombreProveedor(view))
                    .FontSize(13)
                    .SemiBold();

                column.Item().Text($"Identificación: {view.Proveedor?.ruc ?? view.RetencionInfo.IdCliente ?? "-"}");
                column.Item().Text($"Tipo identificación: {view.TipoIdentificacionProveedor ?? "-"}");

                if (!string.IsNullOrWhiteSpace(view.Proveedor?.direccion))
                    column.Item().Text($"Dirección: {view.Proveedor.direccion}");

                if (!string.IsNullOrWhiteSpace(view.Proveedor?.email))
                    column.Item().Text($"Correo: {view.Proveedor.email}");
            });
    }

    private static void ComponerBloqueResumen(IContainer container, RetencionGeneradaDetalleViewDto view)
    {
        container.Border(1)
            .BorderColor(Colors.Grey.Lighten2)
            .Padding(14)
            .Column(column =>
            {
                column.Spacing(6);
                column.Item().Text("Resumen")
                    .FontSize(12)
                    .SemiBold()
                    .FontColor(Colors.Blue.Darken3);

                column.Item().Text($"Base total: {FormatearMoneda(view.BaseTotal)}");

                column.Item().PaddingTop(6).Background(Colors.Blue.Lighten5).Padding(10)
                    .Text($"TOTAL RETENIDO: {FormatearMoneda(view.TotalRetenido)}")
                    .SemiBold()
                    .FontSize(12)
                    .FontColor(Colors.Blue.Darken3);
            });
    }

    private static void ComponerBloqueDocumentoSustento(IContainer container, RetencionGeneradaDetalleViewDto view)
    {
        container.Border(1)
            .BorderColor(Colors.Grey.Lighten2)
            .Padding(14)
            .Column(column =>
            {
                column.Spacing(6);
                column.Item().Text("Documento sustento")
                    .FontSize(12)
                    .SemiBold()
                    .FontColor(Colors.Blue.Darken3);

                column.Item().Text($"Factura sustento: {view.DocumentoSustentoVisual}");
                column.Item().Text($"Total sin impuestos: {FormatearMoneda(view.Compra?.Subtotal ?? 0m)}");
                column.Item().Text($"Total factura: {FormatearMoneda(view.Compra?.ValorTotal ?? 0m)}");
            });
    }

    private static void ComponerDetalle(IContainer container, RetencionGeneradaDetalleViewDto view)
    {
        var detalles = OrdenarDetallesRetencion(view.Retenciones).ToList();

        container.Border(1)
            .BorderColor(Colors.Grey.Lighten2)
            .Padding(10)
            .Column(column =>
            {
                column.Item().Text("Detalle de retenciones")
                    .FontSize(12)
                    .SemiBold()
                    .FontColor(Colors.Blue.Darken3);

                column.Item().PaddingTop(8).Table(table =>
                {
                    table.ColumnsDefinition(columns =>
                    {
                        columns.ConstantColumn(78);
                        columns.ConstantColumn(60);
                        columns.ConstantColumn(42);
                        columns.ConstantColumn(45);
                        columns.RelativeColumn(2.2f);
                        columns.ConstantColumn(52);
                        columns.ConstantColumn(46);
                        columns.ConstantColumn(58);
                    });

                    table.Header(header =>
                    {
                        header.Cell().Element(CellHeader).Text("Numero").SemiBold().FontColor(Colors.White);
                        header.Cell().Element(CellHeader).Text("Fecha Emision").SemiBold().FontColor(Colors.White);
                        header.Cell().Element(CellHeader).Text("Tipo").SemiBold().FontColor(Colors.White);
                        header.Cell().Element(CellHeader).Text("Código").SemiBold().FontColor(Colors.White);
                        header.Cell().Element(CellHeader).Text("Descripción").SemiBold().FontColor(Colors.White);
                        header.Cell().Element(CellHeader).AlignRight().Text("Base").SemiBold().FontColor(Colors.White);
                        header.Cell().Element(CellHeader).AlignRight().Text("% Ret.").SemiBold().FontColor(Colors.White);
                        header.Cell().Element(CellHeader).AlignRight().Text("Valor").SemiBold().FontColor(Colors.White);
                    });

                    if (!detalles.Any())
                    {
                        table.Cell().ColumnSpan(8).Element(CellBody).AlignCenter().PaddingVertical(16)
                            .Text("No hay líneas registradas para esta retención.");
                    }
                    else
                    {
                        var numeroSustento = string.IsNullOrWhiteSpace(view.DocumentoSustentoVisual)
                            ? "-"
                            : view.DocumentoSustentoVisual;
                        var fechaSustento = FormatearFecha(view.FechaEmisionDocumentoSustento);

                        foreach (var detalle in detalles)
                        {
                            table.Cell().Element(CellBody).Text(numeroSustento);
                            table.Cell().Element(CellBody).Text(fechaSustento);
                            table.Cell().Element(CellBody).Text(detalle.Tipo);
                            table.Cell().Element(CellBody).Text(detalle.CodigoRetencion);
                            table.Cell().Element(CellBody).Text(detalle.Descripcion);
                            table.Cell().Element(CellBody).AlignRight().Text(FormatearNumero(detalle.BaseImponible));
                            table.Cell().Element(CellBody).AlignRight().Text(FormatearNumero(detalle.PorcentajeRetener));
                            table.Cell().Element(CellBody).AlignRight().Text(FormatearMoneda(detalle.ValorRetenido));
                        }
                    }

                    static IContainer CellHeader(IContainer container) =>
                        container.Background(Colors.Blue.Darken3).PaddingVertical(7).PaddingHorizontal(4).DefaultTextStyle(x => x.FontSize(8));

                    static IContainer CellBody(IContainer container) =>
                        container.BorderBottom(1).BorderColor(Colors.Grey.Lighten2).PaddingVertical(7).PaddingHorizontal(4).DefaultTextStyle(x => x.FontSize(8));
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

    private static bool RetencionAutorizada(string? autorizado, string? estadoSri = null)
    {
        if (string.IsNullOrWhiteSpace(autorizado))
            return false;

        var valor = autorizado.Trim().ToLowerInvariant();
        return valor is "1" or "true" or "t" or "s" or "si" or "sí" or "a" or "autorizado";
    }

    private static string ObtenerNombreProveedor(RetencionGeneradaDetalleViewDto view)
    {
        if (!string.IsNullOrWhiteSpace(view.Proveedor?.nombre))
            return view.Proveedor.nombre.Trim();

        var nombre = string.Join(" ", new[]
        {
            view.Proveedor?.primerNombre,
            view.Proveedor?.segundoNombre,
            view.Proveedor?.primerApellido,
            view.Proveedor?.segundoApellido
        }.Where(x => !string.IsNullOrWhiteSpace(x))).Trim();

        return !string.IsNullOrWhiteSpace(nombre)
            ? nombre
            : (view.RetencionInfo.IdCliente ?? "Proveedor");
    }

    private static bool MostrarNumeroAutorizacion(string? numeroAutorizacion)
        => !string.IsNullOrWhiteSpace(numeroAutorizacion);

    private static string FormatearMoneda(decimal valor)
        => $"${valor.ToString("N2", Cultura)}";

    private static string FormatearNumero(decimal valor)
        => valor.ToString("N2", Cultura);

    private static string FormatearFecha(DateTime? valor)
        => valor?.ToString("dd/MM/yyyy", Cultura) ?? "-";

    private static string FormatearPorcentaje(decimal valor)
        => valor.ToString("N2", Cultura);

    private static string ObtenerTextoOGuion(string? valor)
        => string.IsNullOrWhiteSpace(valor) ? "-" : valor.Trim();

    private static string ObtenerTelefonoProveedor(Simetric.Models.Proveedor? proveedor)
    {
        if (!string.IsNullOrWhiteSpace(proveedor?.telefonoMovil))
            return proveedor.telefonoMovil;

        return proveedor?.telefono ?? string.Empty;
    }

    private static string ObtenerFechaAutorizacionTexto(RetencionInfo retencion)
    {
        if (!string.IsNullOrWhiteSpace(retencion.FechaAutorizaSri))
            return retencion.FechaAutorizaSri;

        return retencion.Fecha?.ToString("dd/MM/yyyy HH:mm:ss", Cultura) ?? "-";
    }

    private static string ObtenerAmbiente(RetencionInfo retencion, ComprasFactura? compra)
    {
        var ambiente = retencion.Ambiente ?? compra?.Ambiente;
        return ambiente == 2 ? "PRODUCCION" : "PRUEBAS";
    }

    private static string ObtenerTipoComprobanteSustento(RetencionGeneradaDetalleViewDto view)
    {
        var tipo = view.Compra?.TipoDocumento ?? view.Compra?.CodDocumento ?? view.RetencionInfo.TipoDocumento;

        return (tipo ?? string.Empty).Trim() switch
        {
            "01" => "FACTURA",
            "03" => "LIQUIDACION",
            "04" => "NOTA CREDITO",
            "05" => "NOTA DEBITO",
            _ when !string.IsNullOrWhiteSpace(tipo) => tipo.Trim().ToUpperInvariant(),
            _ => "LIQUIDACION"
        };
    }

    private static string ObtenerNumeroSustento(RetencionGeneradaDetalleViewDto view)
    {
        if (!string.IsNullOrWhiteSpace(view.DocumentoSustentoVisual))
            return view.DocumentoSustentoVisual.Trim();

        if (!string.IsNullOrWhiteSpace(view.Compra?.NumFactura))
            return view.Compra.NumFactura.Trim();

        return "-";
    }

    private static string ObtenerNombreImpuesto(string? tipo)
    {
        return (tipo ?? string.Empty).Trim().ToUpperInvariant() switch
        {
            "RENTA" => "RENTA",
            "IVA" => "IVA",
            "ISD" => "ISD",
            _ => ObtenerTextoOGuion(tipo).ToUpperInvariant()
        };
    }

    private static void ComponerDatoLineaA4(IContainer container, string etiqueta, string? valor)
    {
        container.Text(text =>
        {
            text.DefaultTextStyle(x => x.FontSize(FuenteBasePdf));
            text.Span(etiqueta).Bold();
            if (!string.IsNullOrWhiteSpace(valor))
                text.Span($" {valor.Trim()}");
        });
    }

    private static void ComponerDatoApiladoA4(IContainer container, string etiqueta, string? valor)
    {
        container.Column(column =>
        {
            column.Spacing(1);
            column.Item().Text(etiqueta)
                .FontSize(FuenteBasePdf)
                .Bold();
            column.Item().Text(ObtenerTextoOGuion(valor))
                .FontSize(FuenteBasePdf);
        });
    }

    private static IEnumerable<RetencionGeneradaDetalleLineaDto> OrdenarDetallesRetencion(IEnumerable<RetencionGeneradaDetalleLineaDto> detalles)
        => detalles
            .OrderBy(detalle => OrdenTipoRetencion(detalle.Tipo))
            .ThenBy(detalle => detalle.CodigoRetencion);

    private static int OrdenTipoRetencion(string? tipo)
    {
        return (tipo ?? string.Empty).Trim().ToUpperInvariant() switch
        {
            "RENTA" => 0,
            "IVA" => 1,
            "ISD" => 2,
            _ => 3
        };
    }

    private static ThermalTicketModel ConstruirTicketTermico(RetencionGeneradaDetalleViewDto view)
    {
        var estaAutorizada = RetencionAutorizada(view.RetencionInfo.Autorizado, view.RetencionInfo.Estado);

        return new ThermalTicketModel
        {
            TituloDocumento = "COMPROBANTE DE RETENCION",
            NumeroDocumento = view.NumeroCompleto,
            EstadoDocumento = estaAutorizada ? "AUTORIZADA" : "EMITIDA",
            FechaEmisionTexto = (view.RetencionInfo.Fecha ?? DateTime.Today).ToString("dd/MM/yyyy", Cultura),
            EtiquetaAcceso = DocumentoAutorizacionHelper.ObtenerEtiquetaAcceso(
                estaAutorizada,
                view.RetencionInfo.NumAutorizacion),
            ClaveAcceso = DocumentoAutorizacionHelper.ObtenerValorAcceso(
                estaAutorizada,
                view.RetencionInfo.NumAutorizacion,
                view.RetencionInfo.Clave),
            EmisorNombre = view.Emisor?.RazonSocial ?? "EMISOR",
            EmisorSecundario = $"RUC: {view.Emisor?.Ruc ?? "-"}",
            TituloItems = "Retenciones",
            Bloques =
            [
                new ThermalTicketBlock
                {
                    Titulo = "Proveedor",
                    Lineas =
                    [
                        new ThermalTicketLine { Etiqueta = "Nombre", Valor = ObtenerNombreProveedor(view) },
                        new ThermalTicketLine { Etiqueta = "Id", Valor = view.Proveedor?.ruc ?? view.RetencionInfo.IdCliente ?? "-" },
                        new ThermalTicketLine { Etiqueta = "Sustento", Valor = view.DocumentoSustentoVisual }
                    ]
                }
            ],
            Items = OrdenarDetallesRetencion(view.Retenciones).Select(detalle => new ThermalTicketItem
            {
                Descripcion = detalle.Descripcion,
                DetalleSecundario = $"{detalle.Tipo} {detalle.CodigoRetencion} | {FormatearNumero(detalle.PorcentajeRetener)}%",
                CantidadTexto = FormatearNumero(detalle.BaseImponible),
                TotalTexto = FormatearMoneda(detalle.ValorRetenido)
            }).ToList(),
            Totales =
            [
                new ThermalTicketLine { Etiqueta = "Base total", Valor = FormatearMoneda(view.BaseTotal) },
                new ThermalTicketLine { Etiqueta = "TOTAL", Valor = FormatearMoneda(view.TotalRetenido) }
            ]
        };
    }

    private static byte[]? GenerarBarcodeAcceso(string? valorAcceso)
    {
        var valor = LimpiarValorBarcode(valorAcceso);
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
            return null;
        }
    }

    private static string LimpiarValorBarcode(string? valor)
        => new((valor ?? string.Empty)
            .Trim()
            .Where(ch => !char.IsWhiteSpace(ch))
            .ToArray());

    private static string LimpiarSegmentoArchivo(string? valor, string reemplazo)
    {
        var limpio = string.IsNullOrWhiteSpace(valor) ? reemplazo : valor.Trim();
        foreach (var caracter in Path.GetInvalidFileNameChars())
            limpio = limpio.Replace(caracter, '_');

        return limpio.Replace(" ", "_");
    }
}
