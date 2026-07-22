using System.Globalization;
using Simetric.DTOs;

namespace Simetric.Services;

public interface IReporteComprobantesExcelService
{
    Task<ReporteArchivoDescargaDto> GenerarExcelAsync(ReporteComprobantesPdfRequest request);
}

public sealed class ReporteComprobantesExcelService : IReporteComprobantesExcelService
{
    private static readonly CultureInfo Cultura = new("es-EC");
    private readonly ISimpleExcelExportService _excelExportService;

    public ReporteComprobantesExcelService(ISimpleExcelExportService excelExportService)
    {
        _excelExportService = excelExportService;
    }

    public Task<ReporteArchivoDescargaDto> GenerarExcelAsync(ReporteComprobantesPdfRequest request)
    {
        if (request.Items.Count == 0)
        {
            throw new InvalidOperationException("No hay documentos filtrados para exportar.");
        }

        var rows = BuildDocumentRows(request);
        var archivo = _excelExportService.Create(
            $"reporte_documentos_{DateTime.Now:yyyyMMdd_HHmmss}.xlsx",
            new ExcelSheetData(
                "Documentos",
                Array.Empty<string>(),
                Array.Empty<IReadOnlyList<string>>(),
                rows,
                [14, 20, 20, 16, 34, 18, 18, 42, 14, 14, 10, 12, 40]));

        return Task.FromResult(archivo);
    }

    private static IReadOnlyList<ExcelRowData> BuildDocumentRows(ReporteComprobantesPdfRequest request)
    {
        var rows = new List<ExcelRowData>();
        var grupos = request.Items
            .GroupBy(item => item.TipoDocumentoCodigo)
            .OrderBy(group => GetSectionOrder(group.Key))
            .ToList();

        foreach (var grupo in grupos)
        {
            var items = grupo
                .OrderBy(item => item.FechaEmision ?? DateTime.MinValue)
                .ThenBy(item => item.NumeroDocumento)
                .ThenBy(item => item.DocumentoId)
                .ToList();

            rows.Add(new ExcelRowData([
                new ExcelCellData(GetSectionTitle(grupo.Key), 2)
            ]));

            rows.Add(new ExcelRowData([
                new ExcelCellData("Fecha", 1),
                new ExcelCellData("Tipo documento", 1),
                new ExcelCellData("Numero", 1),
                new ExcelCellData("Estado", 1),
                new ExcelCellData("Tercero", 1),
                new ExcelCellData("Identificacion", 1),
                new ExcelCellData("Codigo", 1),
                new ExcelCellData("Detalle", 1),
                new ExcelCellData("Base Sin IVA", 1),
                new ExcelCellData("Base Con IVA", 1),
                new ExcelCellData("IVA", 1),
                new ExcelCellData("Total", 1),
                new ExcelCellData("Numero autorizacion", 1)
            ]));

            foreach (var item in items)
            {
                rows.Add(new ExcelRowData([
                    new ExcelCellData(item.FechaEmision?.ToString("dd/MM/yyyy", Cultura) ?? string.Empty),
                    new ExcelCellData(item.TipoDocumento ?? string.Empty),
                    new ExcelCellData(item.NumeroDocumento ?? string.Empty),
                    new ExcelCellData(item.EstadoDocumento ?? string.Empty),
                    new ExcelCellData(item.TerceroNombre ?? string.Empty),
                    new ExcelCellData(item.TerceroIdentificacion ?? string.Empty),
                    new ExcelCellData(BuildCodes(item)),
                    new ExcelCellData(BuildDetail(item)),
                    new ExcelCellData(FormatearMonto(item.BaseSinIva)),
                    new ExcelCellData(FormatearMonto(item.BaseConIva)),
                    new ExcelCellData(FormatearMonto(item.Iva)),
                    new ExcelCellData(FormatearMonto(item.Total)),
                    new ExcelCellData(item.NumeroAutorizacion ?? string.Empty)
                ]));
            }

            rows.Add(new ExcelRowData([
                new ExcelCellData(string.Empty),
                new ExcelCellData(string.Empty),
                new ExcelCellData(string.Empty),
                new ExcelCellData(string.Empty),
                new ExcelCellData(string.Empty),
                new ExcelCellData(string.Empty),
                new ExcelCellData(string.Empty),
                new ExcelCellData("TOTAL", 4),
                new ExcelCellData(FormatearMonto(items.Sum(x => x.BaseSinIva)), 4),
                new ExcelCellData(FormatearMonto(items.Sum(x => x.BaseConIva)), 4),
                new ExcelCellData(FormatearMonto(items.Sum(x => x.Iva)), 4),
                new ExcelCellData(FormatearMonto(items.Sum(x => x.Total)), 4),
                new ExcelCellData(string.Empty)
            ]));

            rows.Add(new ExcelRowData([
                new ExcelCellData(string.Empty)
            ]));
        }

        return rows;
    }

    private static string BuildCodes(ReporteComprobanteItemDto item)
    {
        if (item.CodigosRelacionados.Count == 0)
        {
            return string.Empty;
        }

        return string.Join(" | ", item.CodigosRelacionados);
    }

    private static string BuildDetail(ReporteComprobanteItemDto item)
    {
        if (item.ProductosRelacionados.Count > 0)
        {
            return string.Join(" | ", item.ProductosRelacionados);
        }

        if (!string.IsNullOrWhiteSpace(item.DocumentoRelacionado))
        {
            return item.DocumentoRelacionado;
        }

        return "Sin detalle";
    }

    private static string FormatearMonto(decimal value)
        => value == 0m ? string.Empty : value.ToString("N2", Cultura);

    private static string GetSectionTitle(string? tipoCodigo) => tipoCodigo switch
    {
        ReporteComprobantesTipos.Factura => "ventas",
        ReporteComprobantesTipos.NotaCredito => "notas de credito",
        ReporteComprobantesTipos.NotaDebito => "notas de debito",
        ReporteComprobantesTipos.GuiaRemision => "guias de remision",
        ReporteComprobantesTipos.Retencion => "retenciones",
        ReporteComprobantesTipos.LiquidacionCompra => "liquidaciones de compra",
        _ => "documentos"
    };

    private static int GetSectionOrder(string? tipoCodigo) => tipoCodigo switch
    {
        ReporteComprobantesTipos.Factura => 1,
        ReporteComprobantesTipos.NotaCredito => 2,
        ReporteComprobantesTipos.NotaDebito => 3,
        ReporteComprobantesTipos.GuiaRemision => 4,
        ReporteComprobantesTipos.Retencion => 5,
        ReporteComprobantesTipos.LiquidacionCompra => 6,
        _ => 99
    };
}
