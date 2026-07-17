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

        var resumenRows = new List<IReadOnlyList<string>>
        {
            new[]
            {
                "Emisor",
                request.NombreEmisor ?? string.Empty,
                "RUC",
                request.RucEmisor ?? string.Empty,
                "Usuario",
                request.NombreUsuario ?? string.Empty
            },
            new[]
            {
                "Generado",
                request.GeneradoEn.ToString("dd/MM/yyyy HH:mm", Cultura),
                "Desde",
                request.Filtros.FechaDesde?.ToString("dd/MM/yyyy", Cultura) ?? "Sin limite",
                "Hasta",
                request.Filtros.FechaHasta?.ToString("dd/MM/yyyy", Cultura) ?? "Sin limite"
            },
            new[]
            {
                "Documentos",
                request.Items.Count.ToString(Cultura),
                "Base imponible",
                request.Items.Sum(x => x.BaseImponible).ToString("N2", Cultura),
                "IVA",
                request.Items.Sum(x => x.Iva).ToString("N2", Cultura)
            },
            new[]
            {
                "Total",
                request.Items.Sum(x => x.Total).ToString("N2", Cultura),
                "Tipo principal",
                request.Items.GroupBy(x => x.TipoDocumento).OrderByDescending(x => x.Count()).Select(x => x.Key).FirstOrDefault() ?? string.Empty,
                "Tercero principal",
                request.Items.Where(x => !string.IsNullOrWhiteSpace(x.TerceroNombre)).GroupBy(x => x.TerceroNombre).OrderByDescending(x => x.Count()).Select(x => x.Key ?? string.Empty).FirstOrDefault() ?? string.Empty
            }
        };

        var documentosRows = request.Items
            .Select(item => (IReadOnlyList<string>)new[]
            {
                item.FechaEmision?.ToString("dd/MM/yyyy", Cultura) ?? string.Empty,
                item.TipoDocumento ?? string.Empty,
                item.NumeroDocumento ?? string.Empty,
                item.EstadoDocumento ?? string.Empty,
                item.TerceroNombre ?? string.Empty,
                item.TerceroIdentificacion ?? string.Empty,
                item.BaseImponible.ToString("N2", Cultura),
                item.Iva.ToString("N2", Cultura),
                item.Total.ToString("N2", Cultura),
                item.NumeroAutorizacion ?? string.Empty
            })
            .ToList();

        var archivo = _excelExportService.Create(
            $"reporte_documentos_{DateTime.Now:yyyyMMdd_HHmmss}.xlsx",
            new ExcelSheetData(
                "Resumen",
                new[] { "Campo", "Valor 1", "Campo 2", "Valor 2", "Campo 3", "Valor 3" },
                resumenRows),
            new ExcelSheetData(
                "Documentos",
                new[]
                {
                    "Fecha",
                    "Tipo documento",
                    "Numero",
                    "Estado",
                    "Tercero",
                    "Identificacion",
                    "Base imponible",
                    "IVA",
                    "Total",
                    "Numero autorizacion"
                },
                documentosRows));

        return Task.FromResult(archivo);
    }
}
