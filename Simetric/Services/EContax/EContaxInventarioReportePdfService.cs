using System.Globalization;
using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;
using Simetric.DTOs.EContax;

namespace Simetric.Services.EContax;

public interface IEContaxInventarioReportePdfService
{
    byte[] Generar(EContaxInventarioReporteDto reporte);
}

public sealed class EContaxInventarioReportePdfService : IEContaxInventarioReportePdfService
{
    private static readonly CultureInfo Cultura = new("es-EC");

    public byte[] Generar(EContaxInventarioReporteDto reporte)
    {
        if (reporte.Movimientos.Count == 0)
            throw new InvalidOperationException("No hay movimientos filtrados para exportar.");

        return Document.Create(container =>
        {
            container.Page(page =>
            {
                page.Size(PageSizes.A4.Landscape());
                page.Margin(1.1f, Unit.Centimetre);
                page.DefaultTextStyle(x => x.FontSize(7).FontColor(Colors.Grey.Darken4));
                page.Header().Column(column =>
                {
                    column.Item().Text("REPORTE DE INVENTARIO").FontSize(18).SemiBold().FontColor("#0B5B97");
                    column.Item().PaddingTop(3).Text($"Generado: {reporte.GeneradoEn:dd/MM/yyyy HH:mm}");
                });
                page.Content().PaddingTop(10).Column(column =>
                {
                    column.Item().Row(row =>
                    {
                        row.RelativeItem().Background("#EEF6FC").Padding(8).Text($"Entradas: {reporte.TotalEntradas.ToString("N2", Cultura)} ({reporte.ValorEntradas.ToString("C2", Cultura)})");
                        row.RelativeItem().Background("#FFF4EC").Padding(8).Text($"Salidas: {reporte.TotalSalidas.ToString("N2", Cultura)} ({reporte.ValorSalidas.ToString("C2", Cultura)})");
                        row.RelativeItem().Background("#F3F4F6").Padding(8).Text($"Movimientos: {reporte.Movimientos.Count:N0}");
                    });
                    column.Item().PaddingTop(10).Table(table =>
                    {
                        table.ColumnsDefinition(columns =>
                        {
                            columns.ConstantColumn(58);
                            columns.RelativeColumn(2);
                            columns.RelativeColumn(1.2f);
                            columns.RelativeColumn(1.2f);
                            columns.ConstantColumn(52);
                            columns.ConstantColumn(62);
                            columns.ConstantColumn(62);
                            columns.RelativeColumn(1.3f);
                        });
                        table.Header(header =>
                        {
                            foreach (var titulo in new[] { "Fecha", "Producto", "Sucursal", "Movimiento", "Cantidad", "Costo unit.", "Costo total", "Lote / referencia" })
                                header.Cell().Background("#0B5B97").Padding(4).Text(titulo).FontColor(Colors.White).SemiBold();
                        });
                        foreach (var item in reporte.Movimientos)
                        {
                            table.Cell().Padding(3).Text(item.FechaMovimiento.ToLocalTime().ToString("dd/MM/yy HH:mm"));
                            table.Cell().Padding(3).Text($"{item.NombreProducto ?? $"#{item.CodigoProducto}"}\n{item.CodigoPrincipal}");
                            table.Cell().Padding(3).Text(item.NombreSucursal ?? string.Empty);
                            table.Cell().Padding(3).Text(item.TipoMovimiento);
                            table.Cell().Padding(3).AlignRight().Text(item.Cantidad.ToString("N2", Cultura));
                            table.Cell().Padding(3).AlignRight().Text(item.CostoUnitario.ToString("C2", Cultura));
                            table.Cell().Padding(3).AlignRight().Text(item.CostoTotal.ToString("C2", Cultura));
                            table.Cell().Padding(3).Text($"{item.NumeroLote ?? "-"}\n{item.Referencia ?? item.Observacion ?? "-"}");
                        }
                    });
                });
                page.Footer().AlignRight().Text("E-Contax · Inventarios");
            });
        }).GeneratePdf();
    }
}
