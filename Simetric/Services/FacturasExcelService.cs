using System.Globalization;
using Simetric.DTOs;

namespace Simetric.Services;

public interface IFacturasExcelService
{
    Task<ReporteArchivoDescargaDto> GenerarAsync(IReadOnlyCollection<FacturaListDto> items);
}

public sealed class FacturasExcelService : IFacturasExcelService
{
    private const int TotalColumnCount = 14;
    private readonly ISimpleExcelExportService _excelExportService;

    public FacturasExcelService(ISimpleExcelExportService excelExportService)
    {
        _excelExportService = excelExportService;
    }

    public Task<ReporteArchivoDescargaDto> GenerarAsync(IReadOnlyCollection<FacturaListDto> items)
    {
        if (items.Count == 0)
        {
            throw new InvalidOperationException("No hay facturas para exportar.");
        }

        var rows = new List<ExcelRowData>
        {
            new([new ExcelCellData("REPORTE GENERAL DE FACTURAS", 2, ExcelCellType.Text, TotalColumnCount - 1)]),
            new([new ExcelCellData($"Generado: {DateTime.Now.ToString("dd/MM/yyyy HH:mm", new CultureInfo("es-EC"))}", 3, ExcelCellType.Text, TotalColumnCount - 1)]),
            new([
                new ExcelCellData("FECHA", 1),
                new ExcelCellData("NÚMERO", 1),
                new ExcelCellData("CLIENTE", 1),
                new ExcelCellData("IDENTIFICACIÓN", 1),
                new ExcelCellData("ESTADO SRI", 1),
                new ExcelCellData("SUBTOTAL", 1),
                new ExcelCellData("SUBTOTAL IVA", 1),
                new ExcelCellData("SUBTOTAL 0", 1),
                new ExcelCellData("NO OBJETO", 1),
                new ExcelCellData("EXENTO", 1),
                new ExcelCellData("DESCUENTOS", 1),
                new ExcelCellData("IVA", 1),
                new ExcelCellData("ICE", 1),
                new ExcelCellData("TOTAL", 1)
            ])
        };

        foreach (var item in items.OrderBy(x => x.FechaEmision).ThenBy(x => x.NumeroCompleto))
        {
            rows.Add(new ExcelRowData([
                new ExcelCellData(item.FechaEmision?.ToString("dd/MM/yyyy", CultureInfo.InvariantCulture) ?? string.Empty),
                new ExcelCellData(item.NumeroCompleto ?? string.Empty),
                new ExcelCellData(item.Cliente ?? string.Empty),
                new ExcelCellData(item.IdentificacionCliente ?? string.Empty),
                new ExcelCellData((item.Autorizado ?? false) ? "AUTORIZADO" : "PENDIENTE"),
                Monto(item.Subtotal),
                Monto(item.SubtotalIva),
                Monto(item.SubtotalCero),
                Monto(item.SubtotalNoObjeto),
                Monto(item.SubtotalExento),
                Monto(item.Descuentos),
                Monto(item.Iva),
                Monto(item.Ice),
                Monto(item.Total)
            ]));
        }

        rows.Add(new ExcelRowData([
            new ExcelCellData(string.Empty), new ExcelCellData(string.Empty), new ExcelCellData(string.Empty),
            new ExcelCellData(string.Empty), new ExcelCellData("TOTAL GENERAL", 4),
            Monto(items.Sum(x => x.Subtotal), 6), Monto(items.Sum(x => x.SubtotalIva), 6),
            Monto(items.Sum(x => x.SubtotalCero), 6), Monto(items.Sum(x => x.SubtotalNoObjeto), 6),
            Monto(items.Sum(x => x.SubtotalExento), 6), Monto(items.Sum(x => x.Descuentos), 6),
            Monto(items.Sum(x => x.Iva), 6), Monto(items.Sum(x => x.Ice), 6), Monto(items.Sum(x => x.Total), 6)
        ]));

        var archivo = _excelExportService.Create(
            $"facturas_{DateTime.Now:yyyyMMdd_HHmmss}.xlsx",
            new ExcelSheetData("FACTURAS", Array.Empty<string>(), Array.Empty<IReadOnlyList<string>>(), rows,
                [16, 20, 34, 18, 16, 16, 16, 16, 16, 16, 16, 14, 14, 16]));

        return Task.FromResult(archivo);
    }

    private static ExcelCellData Monto(decimal? value, int styleIndex = 5) =>
        new((value ?? 0m).ToString("0.00", CultureInfo.InvariantCulture), styleIndex, ExcelCellType.Number);
}
