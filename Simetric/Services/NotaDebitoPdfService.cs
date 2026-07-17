using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;
using BarcodeStandard;
using Simetric.DTOs;
using Simetric.Models;
using System.Globalization;
using SkiaSharp;

namespace Simetric.Services;

public interface INotaDebitoPdfService
{
    Task<string> GenerarPdfNotaDebitoAsync(NotaDebitoDetalleViewDto notaDebitoView, FormatoImpresionDocumento formato = FormatoImpresionDocumento.A4);
    Task<string> GenerarPdfNotaDebitoTemporalAsync(NotaDebitoDetalleViewDto notaDebitoView, FormatoImpresionDocumento formato = FormatoImpresionDocumento.A4);
    Task<bool> EliminarPdfNotaDebitoTemporalAsync(string rutaRelativaPdf);
}

public sealed class NotaDebitoPdfService : INotaDebitoPdfService
{
    private static readonly CultureInfo Cultura = new("es-EC");
    private readonly IWebHostEnvironment _environment;
    private const float FuenteBasePdf = 8.3f;
    private const float FuenteEtiquetaPdf = 7.2f;
    private const float FuenteTituloPdf = 11f;
    private const float PaddingPanelPdf = 7f;

    public NotaDebitoPdfService(IWebHostEnvironment environment)
    {
        _environment = environment;
    }

    public Task<string> GenerarPdfNotaDebitoAsync(NotaDebitoDetalleViewDto notaDebitoView, FormatoImpresionDocumento formato = FormatoImpresionDocumento.A4)
    {
        if (notaDebitoView?.NotaDebito == null)
            throw new InvalidOperationException("No se encontro la informacion necesaria para generar el PDF de la nota de debito.");

        var carpeta = Path.Combine(ObtenerWebRootPath(), "notas_de_debito");
        Directory.CreateDirectory(carpeta);

        var ruc = LimpiarSegmentoArchivo(notaDebitoView.Emisor?.Ruc, "nota_debito");
        var numero = LimpiarSegmentoArchivo(
            notaDebitoView.NotaDebito.NumNotaDebito,
            notaDebitoView.NotaDebito.Sec.ToString(Cultura));
        var rutaPdf = Path.Combine(carpeta, $"{ruc}_05_{numero.PadLeft(9, '0')}{formato.ObtenerSufijoArchivo()}.pdf");

        var logoSistema = CargarLogoDocumento(notaDebitoView.Emisor);

        Document.Create(container =>
        {
            container.Page(page =>
            {
                if (formato.EsTermico())
                {
                    ThermalPdfComposer.ConfigurePage(page, formato);
                    page.Content().Element(content => ThermalPdfComposer.ComposeTicket(content, ConstruirTicketTermico(notaDebitoView)));
                }
                else
                {
                    page.Size(PageSizes.A4);
                    page.Margin(0.75f, Unit.Centimetre);
                    page.PageColor(Colors.White);
                    page.DefaultTextStyle(x => x.FontFamily("Arial").FontSize(FuenteBasePdf).LineHeight(1.1f));

                    page.Content().Element(content => ComponerDocumentoA4(content, notaDebitoView, logoSistema));
                    page.Footer().Element(ComponerPie);
                }
            });
        }).GeneratePdf(rutaPdf);

        return Task.FromResult(rutaPdf);
    }

    public Task<string> GenerarPdfNotaDebitoTemporalAsync(NotaDebitoDetalleViewDto notaDebitoView, FormatoImpresionDocumento formato = FormatoImpresionDocumento.A4)
    {
        if (notaDebitoView?.NotaDebito == null)
            throw new InvalidOperationException("No se encontro la informacion necesaria para generar el PDF temporal de la nota de debito.");

        var carpetaTemporal = Path.Combine(ObtenerWebRootPath(), "notas_de_debito", "tmp");
        Directory.CreateDirectory(carpetaTemporal);

        var numero = LimpiarSegmentoArchivo(
            notaDebitoView.NotaDebito.NumNotaDebito,
            notaDebitoView.NotaDebito.Sec.ToString(Cultura));
        var nombreTemporal = $"{Guid.NewGuid():N}_{numero.PadLeft(9, '0')}{formato.ObtenerSufijoArchivo()}.pdf";
        var rutaPdf = Path.Combine(carpetaTemporal, nombreTemporal);

        var logoSistema = CargarLogoDocumento(notaDebitoView.Emisor);

        Document.Create(container =>
        {
            container.Page(page =>
            {
                if (formato.EsTermico())
                {
                    ThermalPdfComposer.ConfigurePage(page, formato);
                    page.Content().Element(content => ThermalPdfComposer.ComposeTicket(content, ConstruirTicketTermico(notaDebitoView)));
                }
                else
                {
                    page.Size(PageSizes.A4);
                    page.Margin(0.75f, Unit.Centimetre);
                    page.PageColor(Colors.White);
                    page.DefaultTextStyle(x => x.FontFamily("Arial").FontSize(FuenteBasePdf).LineHeight(1.1f));

                    page.Content().Element(content => ComponerDocumentoA4(content, notaDebitoView, logoSistema));
                    page.Footer().Element(ComponerPie);
                }
            });
        }).GeneratePdf(rutaPdf);

        return Task.FromResult($"/notas_de_debito/tmp/{nombreTemporal}");
    }

    public Task<bool> EliminarPdfNotaDebitoTemporalAsync(string rutaRelativaPdf)
    {
        if (string.IsNullOrWhiteSpace(rutaRelativaPdf))
            return Task.FromResult(false);

        var rutaSinQuery = rutaRelativaPdf.Split('?', 2)[0];
        var rutaNormalizada = rutaSinQuery.Replace('\\', '/').TrimStart('/');
        var rutaFisica = Path.Combine(ObtenerWebRootPath(), rutaNormalizada.Replace('/', Path.DirectorySeparatorChar));
        if (!rutaFisica.StartsWith(Path.Combine(ObtenerWebRootPath(), "notas_de_debito"), StringComparison.OrdinalIgnoreCase))
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

    private byte[]? CargarLogoSistema()
    {
        var rutaLogo = Path.Combine(ObtenerWebRootPath(), "images", "logo.png");
        return File.Exists(rutaLogo) ? File.ReadAllBytes(rutaLogo) : null;
    }

    private byte[]? CargarLogoDocumento(Emisor? emisor)
    {
        if (string.IsNullOrWhiteSpace(emisor?.LogoImagen))
            return CargarLogoSistema();

        var logo = emisor.LogoImagen.Trim();
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
                    return CargarLogoSistema();
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

        return CargarLogoSistema();
    }

    private static void ComponerEncabezadoA4(IContainer container, NotaDebitoDetalleViewDto view, byte[]? logoSistema)
    {
        var nota = view.NotaDebito;
        var emisor = view.Emisor;
        var estaAutorizada = DocumentoAutorizacionHelper.EstaAutorizado(nota.Autorizado);
        var etiquetaAcceso = estaAutorizada ? "Numero de Autorizacion:" : "Clave de Acceso:";
        var valorAcceso = estaAutorizada && !string.IsNullOrWhiteSpace(nota.NumAutorizacion)
            ? nota.NumAutorizacion
            : nota.CodClave;
        var barcodeBytes = GenerarBarcode(valorAcceso);

        container.PaddingBottom(8).Row(row =>
        {
            row.Spacing(12);

            row.RelativeItem().MinHeight(208).Column(column =>
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
                        .Text(emisor?.NomComercial ?? emisor?.RazonSocial ?? "NUMERICA")
                        .FontSize(22)
                        .Bold()
                        .FontColor(Colors.Blue.Darken3);
                }

                column.Item().Element(card => ComponerPanelEmisorA4(card, emisor));
            });

            row.ConstantItem(285).MinHeight(208).Column(column =>
            {
                column.Spacing(5);
                column.Item()
                    .Background(Colors.Blue.Lighten5)
                    .PaddingVertical(6)
                    .PaddingHorizontal(8)
                    .Row(titulo =>
                    {
                        titulo.RelativeItem().Text("NOTA DE DEBITO ELECTRONICA")
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
                        autorizacion.Item().Element(item => ComponerDatoApiladoA4(item, "Fecha y hora de Autorizacion:", ObtenerFechaAutorizacionTexto(nota)));
                        autorizacion.Item().Element(item => ComponerDatoLineaA4(item, "Ambiente:", ObtenerAmbiente(nota)));
                        autorizacion.Item().Element(item => ComponerDatoLineaA4(item, "Emision:", "NORMAL"));

                        if (!string.IsNullOrWhiteSpace(nota.CodClave))
                        {
                            autorizacion.Item().PaddingTop(2).Text("Clave de Acceso:")
                                .FontSize(FuenteBasePdf)
                                .Bold();

                            if (barcodeBytes != null)
                            {
                                autorizacion.Item()
                                    .Background(Colors.White)
                                    .Padding(3)
                                    .Height(62)
                                    .Image(barcodeBytes)
                                    .FitWidth();
                            }

                            autorizacion.Item().Text(nota.CodClave)
                                .FontSize(6.6f)
                                .FontColor(Colors.Grey.Darken2);
                        }
                    });
            });
        });
    }

    private static void ComponerDocumentoA4(IContainer container, NotaDebitoDetalleViewDto view, byte[]? logoSistema)
    {
        container.Column(column =>
        {
            column.Spacing(8);
            column.Item().Element(header => ComponerEncabezadoA4(header, view, logoSistema));
            column.Item().Element(content => ComponerContenido(content, view));
        });
    }

    private static void ComponerPanelEmisorA4(IContainer container, Emisor? emisor)
    {
        container.Border(1)
            .BorderColor(Colors.Blue.Lighten4)
            .Background(Colors.Blue.Lighten5)
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

    private static void ComponerEncabezado(IContainer container, NotaDebitoDetalleViewDto view, byte[]? logoSistema)
    {
        var nota = view.NotaDebito;
        var emisor = view.Emisor;
        var estaAutorizada = DocumentoAutorizacionHelper.EstaAutorizado(nota.Autorizado);

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

                column.Item().PaddingTop(logoSistema != null ? 8 : 0)
                    .Text(emisor?.RazonSocial ?? "EMISOR")
                    .FontSize(16)
                    .SemiBold()
                    .FontColor(Colors.Blue.Darken3);

                if (!string.IsNullOrWhiteSpace(emisor?.NomComercial))
                    column.Item().PaddingTop(4).Text($"Nombre comercial: {emisor.NomComercial}").FontColor(Colors.Grey.Darken2);

                if (!string.IsNullOrWhiteSpace(emisor?.Ruc))
                    column.Item().PaddingTop(2).Text($"RUC: {emisor.Ruc}").FontColor(Colors.Grey.Darken2);

                if (!string.IsNullOrWhiteSpace(emisor?.DireccionMatriz))
                    column.Item().PaddingTop(2).Text($"Direccion matriz: {emisor.DireccionMatriz}").FontColor(Colors.Grey.Darken2);
            });

            row.ConstantItem(220).Border(1).BorderColor(Colors.Grey.Lighten2).Padding(14).Column(column =>
            {
                column.Item().AlignCenter().Text("NOTA DE DEBITO ELECTRONICA")
                    .FontSize(14)
                    .SemiBold()
                    .FontColor(Colors.Blue.Darken3);

                column.Item().PaddingTop(8).AlignCenter().Text(view.NumeroCompleto)
                    .FontSize(12)
                    .SemiBold();

                column.Item().PaddingTop(10).Text($"Estado del documento: {(estaAutorizada ? "AUTORIZADA" : "EMITIDA")}");
                column.Item().PaddingTop(2).Text($"Documento modificado: {view.NumeroDocModificadoVisual}");

                if (nota.FechaEmiDocModificado.HasValue)
                    column.Item().PaddingTop(2).Text($"Fecha sustento: {nota.FechaEmiDocModificado.Value:dd/MM/yyyy}");

                if (nota.FechaVence.HasValue)
                    column.Item().PaddingTop(2).Text($"Fecha vence: {nota.FechaVence.Value:dd/MM/yyyy}");

                if (estaAutorizada && !string.IsNullOrWhiteSpace(nota.NumAutorizacion))
                    column.Item().PaddingTop(2).Text($"Numero de autorizacion: {nota.NumAutorizacion}");

                if (!string.IsNullOrWhiteSpace(nota.CodClave))
                {
                    var barcodeBytes = GenerarBarcode(nota.CodClave);
                    if (barcodeBytes != null)
                    {
                        column.Item().PaddingTop(8).AlignCenter().Image(barcodeBytes).FitWidth();
                    }
                }
            });
        });
    }

    private static void ComponerContenido(IContainer container, NotaDebitoDetalleViewDto view)
    {
        var nota = view.NotaDebito;

        container.Column(column =>
        {
            column.Spacing(10);

            column.Item().Element(card => ComponerBloqueClienteA4(card, view));
            column.Item().Element(card => ComponerDetalle(card, view.Detalles));
        });
    }

    private static void ComponerBloqueClienteA4(IContainer container, NotaDebitoDetalleViewDto view)
    {
        var cliente = view.Cliente;
        var nota = view.NotaDebito;

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
                    column.Item().Element(item => ComponerDatoLineaA4(item, "Razon Social:", ObtenerNombreCliente(cliente)));
                    column.Item().Element(item => ComponerDatoLineaA4(item, "Direccion:", cliente?.Direccion));
                    column.Item().Element(item => ComponerDatoLineaA4(item, "Fecha Emision:", FormatearFecha(ObtenerFechaEmision(nota))));
                    column.Item().Element(item => ComponerDatoLineaA4(item, "Documento modificado:", view.NumeroDocModificadoVisual));
                });

                row.RelativeItem().Column(column =>
                {
                    column.Spacing(3);
                    column.Item().Element(item => ComponerDatoLineaA4(item, "RUC/CI:", cliente?.Numeroidentificacion));
                    column.Item().Element(item => ComponerDatoLineaA4(item, "Telefono:", ObtenerTelefonoCliente(cliente)));
                    column.Item().Element(item => ComponerDatoLineaA4(item, "Correo:", cliente?.Correo));

                    if (nota.FechaEmiDocModificado.HasValue)
                        column.Item().Element(item => ComponerDatoLineaA4(item, "Fecha sustento:", FormatearFecha(nota.FechaEmiDocModificado)));
                });
            });
    }

    private static void ComponerBloqueCliente(IContainer container, NotaDebitoDetalleViewDto view)
    {
        var cliente = view.Cliente;

        container.Border(1)
            .BorderColor(Colors.Grey.Lighten2)
            .Padding(14)
            .Column(column =>
            {
                column.Spacing(6);
                column.Item().Text("Datos del cliente")
                    .FontSize(12)
                    .SemiBold()
                    .FontColor(Colors.Blue.Darken3);

                column.Item().Text(ObtenerNombreCliente(cliente))
                    .FontSize(13)
                    .SemiBold();

                column.Item().Text($"Identificacion: {cliente?.Numeroidentificacion ?? "-"}");
                column.Item().Text($"Tipo identificacion: {view.TipoIdentificacionCliente ?? "-"}");
                column.Item().Text($"Direccion: {cliente?.Direccion ?? "-"}");

                if (!string.IsNullOrWhiteSpace(cliente?.Correo))
                    column.Item().Text($"Correo: {cliente.Correo}");
            });
    }

    private static void ComponerBloqueMotivo(IContainer container, string? motivo)
    {
        container.Border(1)
            .BorderColor(Colors.Grey.Lighten2)
            .Padding(14)
            .Column(column =>
            {
                column.Spacing(6);
                column.Item().Text("Motivo de la nota de debito")
                    .FontSize(12)
                    .SemiBold()
                    .FontColor(Colors.Blue.Darken3);

                column.Item().Text(string.IsNullOrWhiteSpace(motivo) ? "No especificado" : motivo);
            });
    }

    private static void ComponerDetalle(IContainer container, IReadOnlyCollection<NotaDebitoDetalleLineaDto> detalles)
    {
        container.Border(1)
            .BorderColor(Colors.Grey.Lighten2)
            .Padding(10)
            .Column(column =>
            {
                column.Item().Text("Detalle de la nota de debito")
                    .FontSize(12)
                    .SemiBold()
                    .FontColor(Colors.Blue.Darken3);

                column.Item().PaddingTop(8).Table(table =>
                {
                    table.ColumnsDefinition(columns =>
                    {
                        columns.RelativeColumn(1.1f);
                        columns.RelativeColumn(3.2f);
                        columns.ConstantColumn(52);
                        columns.ConstantColumn(70);
                        columns.ConstantColumn(64);
                        columns.ConstantColumn(60);
                        columns.ConstantColumn(68);
                    });

                    table.Header(header =>
                    {
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
                        table.Cell().ColumnSpan(7).Element(CellBody).AlignCenter().PaddingVertical(16)
                            .Text("No hay detalles registrados para esta nota de debito.");
                    }
                    else
                    {
                        foreach (var detalle in detalles)
                        {
                            table.Cell().Element(CellBody).Text(detalle.CodigoInterno);
                            table.Cell().Element(CellBody).Text(detalle.Descripcion);
                            table.Cell().Element(CellBody).AlignRight().Text(FormatearNumero(detalle.Cantidad));
                            table.Cell().Element(CellBody).AlignRight().Text(FormatearMoneda(detalle.PrecioUnitario));
                            table.Cell().Element(CellBody).AlignRight().Text(FormatearMoneda(detalle.Descuento));
                            table.Cell().Element(CellBody).AlignRight().Text(FormatearMoneda(detalle.ValorIva));
                            table.Cell().Element(CellBody).AlignRight().Text(FormatearMoneda(detalle.Total));
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
                text.Span("Pagina ");
                text.CurrentPageNumber();
                text.Span(" de ");
                text.TotalPages();
            });
        });
    }

    private static string ObtenerNombreCliente(Simetric.Models.Cliente? cliente)
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

    private static string FormatearMoneda(decimal valor)
        => $"${valor.ToString("N2", Cultura)}";

    private static string FormatearNumero(decimal valor)
        => valor.ToString("N2", Cultura);

    private static string FormatearFecha(DateTime? valor)
        => valor?.ToString("dd/MM/yyyy", Cultura) ?? "-";

    private static string ObtenerTextoOGuion(string? valor)
        => string.IsNullOrWhiteSpace(valor) ? "-" : valor.Trim();

    private static string ObtenerTelefonoCliente(Cliente? cliente)
    {
        if (!string.IsNullOrWhiteSpace(cliente?.Celular))
            return cliente.Celular;

        return cliente?.Telefonoconvencional ?? string.Empty;
    }

    private static DateTime? ObtenerFechaEmision(Simetric.Models.NotaDebito nota)
        => nota.FchAutorizacion ?? nota.FechaEmiDocModificado ?? nota.FechaVence;

    private static string ObtenerFechaAutorizacionTexto(Simetric.Models.NotaDebito nota)
    {
        if (!string.IsNullOrWhiteSpace(nota.FechaAutoSri))
            return nota.FechaAutoSri;

        return nota.FchAutorizacion?.ToString("dd/MM/yyyy HH:mm:ss", Cultura) ?? "-";
    }

    private static string ObtenerAmbiente(Simetric.Models.NotaDebito nota)
        => nota.Ambiente == 2 ? "PRODUCCION" : "PRUEBAS";

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

    private static ThermalTicketModel ConstruirTicketTermico(NotaDebitoDetalleViewDto view)
    {
        var nota = view.NotaDebito;
        var estaAutorizada = DocumentoAutorizacionHelper.EstaAutorizado(nota.Autorizado);

        return new ThermalTicketModel
        {
            TituloDocumento = "NOTA DE DEBITO",
            NumeroDocumento = view.NumeroCompleto,
            EstadoDocumento = estaAutorizada ? "AUTORIZADA" : "EMITIDA",
            FechaEmisionTexto = nota.FechaEmiDocModificado?.ToString("dd/MM/yyyy", Cultura) ?? string.Empty,
            EtiquetaAcceso = DocumentoAutorizacionHelper.ObtenerEtiquetaAcceso(estaAutorizada, nota.NumAutorizacion),
            ClaveAcceso = DocumentoAutorizacionHelper.ObtenerValorAcceso(estaAutorizada, nota.NumAutorizacion, nota.CodClave),
            EmisorNombre = view.Emisor?.RazonSocial ?? "EMISOR",
            EmisorSecundario = $"RUC: {view.Emisor?.Ruc ?? "-"}",
            TituloItems = "Cargos",
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
            Notas = nota.Motivo ?? string.Empty
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
        var valor = (clave ?? string.Empty).Trim();
        if (string.IsNullOrWhiteSpace(valor))
            return null;

        try
        {
            var barcode = new Barcode { IncludeLabel = false };
            using var image = barcode.Encode(BarcodeStandard.Type.Code128, valor, SKColors.Black, SKColors.White, 560, 120);
            using var data = image.Encode(SKEncodedImageFormat.Png, 100);
            return data.ToArray();
        }
        catch
        {
            return null;
        }
    }
}
