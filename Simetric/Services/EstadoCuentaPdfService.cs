using Microsoft.AspNetCore.Hosting;
using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;
using Simetric.DTOs;
using Simetric.ViewModels;
using System.Globalization;

namespace Simetric.Services;

public interface IEstadoCuentaPdfService
{
    Task<ReporteArchivoDescargaDto> GenerarDetalleAsync(EstadoCuentaDetalleVM detalle);
}

public sealed class EstadoCuentaPdfService : IEstadoCuentaPdfService
{
    private static readonly CultureInfo Cultura = new("es-EC");
    private readonly IWebHostEnvironment _environment;

    public EstadoCuentaPdfService(IWebHostEnvironment environment)
    {
        _environment = environment;
    }

    public Task<ReporteArchivoDescargaDto> GenerarDetalleAsync(EstadoCuentaDetalleVM detalle)
    {
        if (detalle == null)
            throw new InvalidOperationException("No hay informacion del estado de cuenta para generar el PDF.");

        using var stream = new MemoryStream();
        var logoSistema = CargarLogoSistema();

        Document.Create(container =>
        {
            container.Page(page =>
            {
                page.Size(PageSizes.A4);
                page.Margin(0.75f, Unit.Centimetre);
                page.PageColor(Colors.White);
                page.DefaultTextStyle(x => x.FontFamily("Arial").FontSize(8.5f).LineHeight(1.15f));

                page.Header().Element(header => ComposeHeader(header, detalle, logoSistema));
                page.Content().Element(content => ComposeContent(content, detalle));
                page.Footer().Element(ComposeFooter);
            });
        }).GeneratePdf(stream);

        return Task.FromResult(new ReporteArchivoDescargaDto
        {
            FileName = $"estado-cuenta-{detalle.IdCliente}-{DateTime.Now:yyyyMMdd_HHmmss}.pdf",
            ContentType = "application/pdf",
            Content = stream.ToArray()
        });
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

    private static void ComposeHeader(IContainer container, EstadoCuentaDetalleVM detalle, byte[]? logoSistema)
    {
        container.PaddingBottom(8).Row(row =>
        {
            row.RelativeItem().Row(innerRow =>
            {
                if (logoSistema != null)
                    innerRow.ConstantItem(110).MaxHeight(55).AlignMiddle().Image(logoSistema).FitArea();

                innerRow.RelativeItem().Column(column =>
                {
                    column.Spacing(2);
                    column.Item().Text("Estado de cuenta")
                        .FontSize(16)
                        .SemiBold()
                        .FontColor(Colors.Blue.Darken3);
                    column.Item().Text($"Cliente: {detalle.NombreCliente}")
                        .FontSize(10.5f)
                        .SemiBold();
                    column.Item().Text($"Identificacion: {TextoSeguro(detalle.NumeroIdentificacion)}");
                    column.Item().Text($"Correo: {TextoSeguro(detalle.Correo)}");
                });
            });

            row.ConstantItem(215).Border(1).BorderColor(Colors.Blue.Lighten3).Background(Colors.Blue.Lighten5).Padding(8).Column(column =>
            {
                column.Spacing(3);
                column.Item().AlignCenter().Text("Resumen")
                    .SemiBold()
                    .FontColor(Colors.Blue.Darken3);
                column.Item().Text($"Corte: {DateTime.Now:dd/MM/yyyy HH:mm}");
                column.Item().Text($"Saldo pendiente: {Moneda(detalle.SaldoTotal)}");
                column.Item().Text($"Facturas pendientes: {detalle.FacturasPendientes}");
                column.Item().Text($"Saldo a favor: {Moneda(detalle.SaldoAFavorDisponible)}");
                column.Item().Text($"Max. dias vencidos: {detalle.DiasVencidosMaximos}");
            });
        });
    }

    private static void ComposeContent(IContainer container, EstadoCuentaDetalleVM detalle)
    {
        container.Column(column =>
        {
            column.Spacing(8);
            column.Item().Element(card => ComposeSummary(card, detalle));
            column.Item().Element(card => ComposeFacturas(card, detalle.Facturas));
            column.Item().Element(card => ComposeAbonos(card, detalle.Abonos));
            column.Item().Element(card => ComposeMovimientos(card, detalle.Movimientos));
        });
    }

    private static void ComposeSummary(IContainer container, EstadoCuentaDetalleVM detalle)
    {
        container.Border(1).BorderColor(Colors.Blue.Lighten4).Padding(8).Column(column =>
        {
            column.Spacing(4);
            column.Item().Text("Resumen general")
                .SemiBold()
                .FontColor(Colors.Blue.Darken3);

            column.Item().Table(table =>
            {
                table.ColumnsDefinition(columns =>
                {
                    columns.RelativeColumn();
                    columns.RelativeColumn();
                    columns.RelativeColumn();
                });

                AddSummaryCell(table, "Saldo total", Moneda(detalle.SaldoTotal));
                AddSummaryCell(table, "Facturas pendientes", detalle.FacturasPendientes.ToString());
                AddSummaryCell(table, "Ultimo abono", Moneda(detalle.MontoUltimoAbono));
                AddSummaryCell(table, "Fecha ultimo abono", detalle.FechaUltimoAbono?.ToString("dd/MM/yyyy", Cultura) ?? "Sin registros");
                AddSummaryCell(table, "Saldo a favor", Moneda(detalle.SaldoAFavorDisponible));
                AddSummaryCell(table, "Max. dias vencidos", detalle.DiasVencidosMaximos.ToString());
            });
        });
    }

    private static void AddSummaryCell(TableDescriptor table, string label, string value)
    {
        table.Cell().Element(CellBox).Column(column =>
        {
            column.Item().Text(label).FontSize(7.2f).FontColor(Colors.Grey.Darken1);
            column.Item().Text(value).SemiBold().FontSize(9f);
        });

        static IContainer CellBox(IContainer container) =>
            container.Border(1).BorderColor(Colors.Grey.Lighten2).Padding(6).Background(Colors.White);
    }

    private static void ComposeFacturas(IContainer container, IReadOnlyCollection<EstadoCuentaFacturaVM> facturas)
    {
        container.Border(1).BorderColor(Colors.Blue.Lighten4).Padding(8).Column(column =>
        {
            column.Spacing(4);
            column.Item().Text("Facturas").SemiBold().FontColor(Colors.Blue.Darken3);
            column.Item().Table(table =>
            {
                table.ColumnsDefinition(columns =>
                {
                    columns.RelativeColumn(1.2f);
                    columns.ConstantColumn(58);
                    columns.ConstantColumn(58);
                    columns.ConstantColumn(62);
                    columns.ConstantColumn(62);
                    columns.ConstantColumn(62);
                    columns.ConstantColumn(56);
                });

                table.Header(header =>
                {
                    HeaderCell(header, "Factura");
                    HeaderCell(header, "Emision");
                    HeaderCell(header, "Vence");
                    HeaderCell(header, "Facturado", true);
                    HeaderCell(header, "Abonos", true);
                    HeaderCell(header, "Saldo", true);
                    HeaderCell(header, "Estado");
                });

                if (!facturas.Any())
                {
                    table.Cell().ColumnSpan(7).Element(BodyCell).AlignCenter().Text("No hay facturas pendientes para este cliente.");
                    return;
                }

                foreach (var item in facturas.OrderBy(x => x.FechaEmision))
                {
                    table.Cell().Element(BodyCell).Text(TextoSeguro(item.NumeroFactura));
                    table.Cell().Element(BodyCell).Text(item.FechaEmision?.ToString("dd/MM/yyyy", Cultura) ?? "-");
                    table.Cell().Element(BodyCell).Text(item.FechaVencimiento?.ToString("dd/MM/yyyy", Cultura) ?? "-");
                    table.Cell().Element(BodyCell).AlignRight().Text(Moneda(item.ValorFacturado));
                    table.Cell().Element(BodyCell).AlignRight().Text(Moneda(item.TotalAbonos));
                    table.Cell().Element(BodyCell).AlignRight().Text(Moneda(item.SaldoActual));
                    table.Cell().Element(BodyCell).Text(TextoSeguro(item.Estado));
                }
            });
        });
    }

    private static void ComposeAbonos(IContainer container, IReadOnlyCollection<EstadoCuentaAbonoVM> abonos)
    {
        container.Border(1).BorderColor(Colors.Blue.Lighten4).Padding(8).Column(column =>
        {
            column.Spacing(4);
            column.Item().Text("Abonos").SemiBold().FontColor(Colors.Blue.Darken3);
            column.Item().Table(table =>
            {
                table.ColumnsDefinition(columns =>
                {
                    columns.ConstantColumn(62);
                    columns.RelativeColumn(1.1f);
                    columns.ConstantColumn(62);
                    columns.RelativeColumn(0.95f);
                    columns.RelativeColumn(1.25f);
                });

                table.Header(header =>
                {
                    HeaderCell(header, "Fecha");
                    HeaderCell(header, "Factura");
                    HeaderCell(header, "Monto", true);
                    HeaderCell(header, "Forma pago");
                    HeaderCell(header, "Observacion");
                });

                if (!abonos.Any())
                {
                    table.Cell().ColumnSpan(5).Element(BodyCell).AlignCenter().Text("No hay abonos registrados.");
                    return;
                }

                foreach (var item in abonos.OrderByDescending(x => x.FechaPago))
                {
                    table.Cell().Element(BodyCell).Text(item.FechaPago?.ToString("dd/MM/yyyy", Cultura) ?? "-");
                    table.Cell().Element(BodyCell).Text(TextoSeguro(item.NumeroFactura));
                    table.Cell().Element(BodyCell).AlignRight().Text(Moneda(item.Monto));
                    table.Cell().Element(BodyCell).Text(TextoSeguro(item.FormaPago));
                    table.Cell().Element(BodyCell).Text(TextoSeguro(item.Observacion));
                }
            });
        });
    }

    private static void ComposeMovimientos(IContainer container, IReadOnlyCollection<EstadoCuentaMovimientoVM> movimientos)
    {
        container.Border(1).BorderColor(Colors.Blue.Lighten4).Padding(8).Column(column =>
        {
            column.Spacing(4);
            column.Item().Text("Historial de movimientos").SemiBold().FontColor(Colors.Blue.Darken3);
            column.Item().Table(table =>
            {
                table.ColumnsDefinition(columns =>
                {
                    columns.ConstantColumn(62);
                    columns.RelativeColumn(1f);
                    columns.RelativeColumn(1.45f);
                    columns.ConstantColumn(58);
                    columns.ConstantColumn(58);
                    columns.ConstantColumn(62);
                });

                table.Header(header =>
                {
                    HeaderCell(header, "Fecha");
                    HeaderCell(header, "Documento");
                    HeaderCell(header, "Concepto");
                    HeaderCell(header, "Debito", true);
                    HeaderCell(header, "Credito", true);
                    HeaderCell(header, "Saldo", true);
                });

                if (!movimientos.Any())
                {
                    table.Cell().ColumnSpan(6).Element(BodyCell).AlignCenter().Text("No hay movimientos registrados.");
                    return;
                }

                foreach (var item in movimientos.OrderBy(x => x.Fecha))
                {
                    table.Cell().Element(BodyCell).Text(item.Fecha.ToString("dd/MM/yyyy", Cultura));
                    table.Cell().Element(BodyCell).Text(TextoSeguro(item.Documento));
                    table.Cell().Element(BodyCell).Text(TextoSeguro(item.Concepto));
                    table.Cell().Element(BodyCell).AlignRight().Text(item.Debito > 0 ? Moneda(item.Debito) : "-");
                    table.Cell().Element(BodyCell).AlignRight().Text(item.Credito > 0 ? Moneda(item.Credito) : "-");
                    table.Cell().Element(BodyCell).AlignRight().Text(Moneda(item.Saldo));
                }
            });
        });
    }

    private static void HeaderCell(TableCellDescriptor descriptor, string text, bool alignRight = false)
    {
        var cell = descriptor.Cell().Element(container =>
            container.Background(Colors.Blue.Darken3).PaddingVertical(5).PaddingHorizontal(4));

        if (alignRight)
            cell.AlignRight().Text(text).FontColor(Colors.White).SemiBold().FontSize(7.4f);
        else
            cell.Text(text).FontColor(Colors.White).SemiBold().FontSize(7.4f);
    }

    private static IContainer BodyCell(IContainer container) =>
        container.BorderBottom(1).BorderColor(Colors.Grey.Lighten2).PaddingVertical(5).PaddingHorizontal(4);

    private static void ComposeFooter(IContainer container)
    {
        container.PaddingTop(4).Row(row =>
        {
            row.RelativeItem().Text($"Generado el {DateTime.Now:dd/MM/yyyy HH:mm}")
                .FontSize(7)
                .FontColor(Colors.Grey.Darken1);

            row.ConstantItem(86).AlignRight().Text(text =>
            {
                text.DefaultTextStyle(x => x.FontSize(7).FontColor(Colors.Grey.Darken1));
                text.Span("Pagina ");
                text.CurrentPageNumber();
                text.Span(" de ");
                text.TotalPages();
            });
        });
    }

    private static string Moneda(decimal valor) => $"${valor.ToString("N2", Cultura)}";

    private static string TextoSeguro(string? valor) =>
        string.IsNullOrWhiteSpace(valor) ? "-" : valor.Trim();
}
