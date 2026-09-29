using System.Globalization;
using Simetric.DTOs;

namespace Simetric.Services;

public interface IFacturasExcelService
{
    Task<ReporteArchivoDescargaDto> GenerarAsync(
        IReadOnlyCollection<FacturaListDto> items,
        IReadOnlyCollection<NotaCreditoListDto>? notasCredito = null);
}

public sealed class FacturasExcelService : IFacturasExcelService
{
    private const int TotalColumnCount = 20;
    private readonly ISimpleExcelExportService _excelExportService;

    public FacturasExcelService(ISimpleExcelExportService excelExportService)
    {
        _excelExportService = excelExportService;
    }

    public Task<ReporteArchivoDescargaDto> GenerarAsync(
        IReadOnlyCollection<FacturaListDto> items,
        IReadOnlyCollection<NotaCreditoListDto>? notasCredito = null)
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
                new ExcelCellData("FECHA CARGA SOLICITUD", 1),
                new ExcelCellData("FECHA EMISIÓN SRI", 1),
                new ExcelCellData("NÚMERO", 1),
                new ExcelCellData("DOCUMENTO MODIFICADO", 1),
                new ExcelCellData("SERVICIO", 1),
                new ExcelCellData("DETALLE", 1),
                new ExcelCellData("CLIENTE", 1),
                new ExcelCellData("IDENTIFICACIÓN", 1),
                new ExcelCellData("ESTADO FACTURA", 1),
                new ExcelCellData("NÚMERO AUTORIZACIÓN SRI", 1),
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

        var facturasIncluidas = items.Select(x => x.Codfactura).ToHashSet();
        var notas = (notasCredito ?? Array.Empty<NotaCreditoListDto>())
            .Where(x => DocumentoAutorizacionHelper.EstaAutorizado(x.Autorizado) &&
                        x.DocumentoModificadoId.HasValue &&
                        facturasIncluidas.Contains(x.DocumentoModificadoId.Value))
            .OrderBy(x => x.FechaAutorizacion ?? x.FechaDocumentoModificado)
            .ThenBy(x => x.NumeroCompleto)
            .ToList();
        var facturasConNotaCredito = notas
            .Select(x => x.DocumentoModificadoId!.Value)
            .ToHashSet();
        var facturasSinNotaCredito = items
            .Where(x => !facturasConNotaCredito.Contains(x.Codfactura))
            .ToList();

        foreach (var item in items.OrderBy(x => x.FechaEmision).ThenBy(x => x.NumeroCompleto))
        {
            rows.Add(new ExcelRowData([
                new ExcelCellData(item.FechaEmision?.ToString("dd/MM/yyyy", CultureInfo.InvariantCulture) ?? string.Empty),
                new ExcelCellData(item.FechaSolicitud?.ToString("dd/MM/yyyy HH:mm", CultureInfo.InvariantCulture) ?? string.Empty),
                new ExcelCellData(item.FechaEmisionSri?.ToString("dd/MM/yyyy HH:mm", CultureInfo.InvariantCulture) ?? string.Empty),
                new ExcelCellData(item.NumeroCompleto ?? string.Empty),
                new ExcelCellData(string.Empty),
                new ExcelCellData(item.Servicio),
                new ExcelCellData(item.Detalle ?? string.Empty),
                new ExcelCellData(item.Cliente ?? string.Empty),
                new ExcelCellData(item.IdentificacionCliente ?? string.Empty),
                new ExcelCellData(ObtenerEstadoFactura(item, facturasConNotaCredito)),
                new ExcelCellData(item.NumeroAutorizacion ?? string.Empty),
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
            new ExcelCellData(string.Empty), new ExcelCellData(string.Empty), new ExcelCellData(string.Empty),
            new ExcelCellData(string.Empty), new ExcelCellData(string.Empty), new ExcelCellData(string.Empty),
            new ExcelCellData("TOTAL CON ANULADAS NC", 4), new ExcelCellData(string.Empty),
            Monto(items.Sum(x => x.Subtotal), 6), Monto(items.Sum(x => x.SubtotalIva), 6),
            Monto(items.Sum(x => x.SubtotalCero), 6), Monto(items.Sum(x => x.SubtotalNoObjeto), 6),
            Monto(items.Sum(x => x.SubtotalExento), 6), Monto(items.Sum(x => x.Descuentos), 6),
            Monto(items.Sum(x => x.Iva), 6), Monto(items.Sum(x => x.Ice), 6), Monto(items.Sum(x => x.Total), 6)
        ]));

        rows.Add(new ExcelRowData([
            new ExcelCellData(string.Empty), new ExcelCellData(string.Empty), new ExcelCellData(string.Empty),
            new ExcelCellData(string.Empty), new ExcelCellData(string.Empty), new ExcelCellData(string.Empty),
            new ExcelCellData(string.Empty), new ExcelCellData(string.Empty), new ExcelCellData(string.Empty),
            new ExcelCellData("TOTAL SIN ANULADAS NC", 4), new ExcelCellData(string.Empty),
            Monto(facturasSinNotaCredito.Sum(x => x.Subtotal), 6), Monto(facturasSinNotaCredito.Sum(x => x.SubtotalIva), 6),
            Monto(facturasSinNotaCredito.Sum(x => x.SubtotalCero), 6), Monto(facturasSinNotaCredito.Sum(x => x.SubtotalNoObjeto), 6),
            Monto(facturasSinNotaCredito.Sum(x => x.SubtotalExento), 6), Monto(facturasSinNotaCredito.Sum(x => x.Descuentos), 6),
            Monto(facturasSinNotaCredito.Sum(x => x.Iva), 6), Monto(facturasSinNotaCredito.Sum(x => x.Ice), 6),
            Monto(facturasSinNotaCredito.Sum(x => x.Total), 6)
        ]));

        if (notas.Count > 0)
        {
            rows.Add(new ExcelRowData([
                new ExcelCellData(string.Empty), new ExcelCellData("NOTAS DE CRÉDITO", 3, ExcelCellType.Text, TotalColumnCount - 1)
            ]));

            foreach (var nota in notas)
            {
                rows.Add(new ExcelRowData([
                    new ExcelCellData((nota.FechaAutorizacion ?? nota.FechaDocumentoModificado)?.ToString("dd/MM/yyyy", CultureInfo.InvariantCulture) ?? string.Empty),
                    new ExcelCellData(string.Empty),
                    new ExcelCellData(string.Empty),
                    new ExcelCellData(nota.NumeroCompleto),
                    new ExcelCellData(nota.NumeroDocModificado),
                    new ExcelCellData("Nota de crédito"),
                    new ExcelCellData(nota.Motivo),
                    new ExcelCellData(nota.Cliente),
                    new ExcelCellData(nota.IdentificacionCliente),
                    new ExcelCellData("AUTORIZADA"),
                    new ExcelCellData(nota.NumeroAutorizacion),
                    Monto(-nota.Subtotal), Monto(-nota.SubtotalIva), Monto(-nota.SubtotalCero), Monto(0m), Monto(0m),
                    Monto(-nota.Descuentos), Monto(-nota.Iva), Monto(-nota.Ice), Monto(-nota.Total)
                ]));
            }

        }

        var archivo = _excelExportService.Create(
            $"facturas_{DateTime.Now:yyyyMMdd_HHmmss}.xlsx",
            new ExcelSheetData("FACTURAS", Array.Empty<string>(), Array.Empty<IReadOnlyList<string>>(), rows,
                [16, 22, 22, 20, 24, 22, 38, 34, 18, 16, 26, 16, 16, 16, 16, 16, 14, 14, 16, 16]));

        return Task.FromResult(archivo);
    }

    private static ExcelCellData Monto(decimal? value, int styleIndex = 5) =>
        new((value ?? 0m).ToString("0.00", CultureInfo.InvariantCulture), styleIndex, ExcelCellType.Number);

    private static string ObtenerEstadoFactura(FacturaListDto factura, IReadOnlySet<int> facturasConNotaCredito)
    {
        if (facturasConNotaCredito.Contains(factura.Codfactura))
            return "ANULADA CON NOTA DE CRÉDITO";

        if (factura.Estado == false)
            return "ANULADA";

        return DocumentoAutorizacionHelper.EstaAutorizado(factura.Autorizado, factura.EstadoSri)
            ? "AUTORIZADA"
            : "PENDIENTE";
    }
}
