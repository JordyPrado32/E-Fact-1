using System.Security.Claims;
using System.Text;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.JSInterop;
using Simetric.DTOs.EContax;
using Simetric.Services;

namespace Simetric.Components.Pages.EContax;

public partial class InventarioReportes
{
    private static readonly string[] TiposMovimiento =
    [
        "ENTRADA", "SALIDA", "DEVOLUCION_VENTA", "DEVOLUCION_COMPRA",
        "TRANSFER_ENTRADA", "TRANSFER_SALIDA", "TOMA_FISICA_ENTRADA",
        "TOMA_FISICA_SALIDA", "AJUSTE_ENTRADA", "AJUSTE_SALIDA",
        "BAJA_VENCIDO", "ANULACION_RECEPCION", "REVALORIZACION",
        "RESERVA", "RESERVA_LIBERADA", "DEVOLUCION_VENTA_REVERSA"
    ];

    private readonly EContaxInventarioReporteFiltroDto filtro = new();
    private EContaxInventarioReporteDto reporte = new();
    private int usuarioId;
    private bool cargando = true;
    private string? error;

    private string SucursalSeleccionadaTexto => (filtro.IdSucursal ?? 0).ToString();

    protected override async Task OnAfterRenderAsync(bool firstRender)
    {
        if (!firstRender)
            return;

        var user = (await AuthStateProvider.GetAuthenticationStateAsync()).User;
        usuarioId = int.TryParse(user.FindFirst("IdUsuario")?.Value ?? user.FindFirstValue(ClaimTypes.NameIdentifier), out var id) ? id : 0;
        if (usuarioId <= 0)
        {
            error = "Sesión no válida.";
            cargando = false;
            return;
        }

        await CargarAsync();
        await InvokeAsync(StateHasChanged);
    }

    private async Task CargarAsync()
    {
        if (usuarioId <= 0)
            return;

        cargando = true;
        error = null;
        reporte = new() { Sucursales = reporte.Sucursales };
        try
        {
            using var response = await Http.PostAsJsonAsync($"api/e-contax/inventarios/reportes?userId={usuarioId}", filtro);
            if (!response.IsSuccessStatusCode)
            {
                error = await response.Content.ReadAsStringAsync();
                return;
            }

            reporte = await response.Content.ReadFromJsonAsync<EContaxInventarioReporteDto>() ?? new();
        }
        catch (Exception ex)
        {
            error = ex.Message;
        }
        finally
        {
            cargando = false;
        }
    }

    private async Task CambiarSucursalAsync(ChangeEventArgs args)
    {
        filtro.IdSucursal = int.TryParse(args.Value?.ToString(), out var id) && id > 0 ? id : null;
        await CargarAsync();
    }

    private async Task LimpiarAsync()
    {
        filtro.FechaDesde = null;
        filtro.FechaHasta = null;
        filtro.IdSucursal = null;
        filtro.CodigoProducto = null;
        filtro.Producto = null;
        filtro.TipoMovimiento = null;
        await CargarAsync();
    }

    private async Task ExportarExcelAsync()
    {
        await CargarAsync();
        if (reporte.Movimientos.Count == 0)
            return;

        var resumen = ObtenerResumenExportacion();
        var rows = reporte.Movimientos.Select(x => (IReadOnlyList<string>)[
            x.FechaMovimiento.ToLocalTime().ToString("dd/MM/yyyy HH:mm"),
            x.NombreProducto ?? $"#{x.CodigoProducto}",
            x.CodigoPrincipal ?? string.Empty,
            x.NombreSucursal ?? string.Empty,
            x.TipoMovimiento,
            x.Cantidad.ToString("N4"),
            x.CostoUnitario.ToString("N4"),
            x.CostoTotal.ToString("N4"),
            x.SaldoCantidad.ToString("N4"),
            x.SaldoCosto.ToString("N4"),
            x.NumeroLote ?? string.Empty,
            x.FechaVencimiento?.ToString("dd/MM/yyyy") ?? string.Empty,
            x.NombreUsuario ?? $"Usuario #{x.IdUsuario}",
            x.Referencia ?? x.Observacion ?? string.Empty
        ]).ToList();

        var archivo = ExcelExportService.Create(
            $"reporte_inventario_{DateTime.Now:yyyyMMdd_HHmm}.xlsx",
            new ExcelSheetData("Resumen", ["Indicador", "Valor"], resumen),
            new ExcelSheetData(
                "Movimientos",
                ["Fecha", "Producto", "Código", "Sucursal", "Movimiento", "Cantidad", "Costo unitario", "Costo total", "Saldo cantidad", "Saldo costo", "Lote", "Vencimiento", "Usuario", "Referencia"],
                rows));

        await using var stream = new MemoryStream(archivo.Content);
        using var streamRef = new DotNetStreamReference(stream);
        await JS.InvokeVoidAsync("downloadFileFromStream", archivo.FileName, streamRef);
    }

    private async Task ExportarCsvAsync()
    {
        await CargarAsync();
        if (reporte.Movimientos.Count == 0)
            return;

        var builder = new StringBuilder();
        builder.AppendLine("Filtro;Valor");
        foreach (var item in ObtenerResumenExportacion())
            builder.AppendLine(string.Join(";", item.Select(EscapeCsv)));
        builder.AppendLine();
        builder.AppendLine(string.Join(";", ["Fecha", "Producto", "Código", "Sucursal", "Movimiento", "Cantidad", "Costo unitario", "Costo total", "Saldo cantidad", "Saldo costo", "Lote", "Vencimiento", "Usuario", "Referencia"]));
        foreach (var item in reporte.Movimientos)
        {
            var values = new[]
            {
                item.FechaMovimiento.ToLocalTime().ToString("dd/MM/yyyy HH:mm"),
                item.NombreProducto ?? $"#{item.CodigoProducto}",
                item.CodigoPrincipal ?? string.Empty,
                item.NombreSucursal ?? string.Empty,
                item.TipoMovimiento,
                item.Cantidad.ToString("N4"),
                item.CostoUnitario.ToString("N4"),
                item.CostoTotal.ToString("N4"),
                item.SaldoCantidad.ToString("N4"),
                item.SaldoCosto.ToString("N4"),
                item.NumeroLote ?? string.Empty,
                item.FechaVencimiento?.ToString("dd/MM/yyyy") ?? string.Empty,
                item.NombreUsuario ?? $"Usuario #{item.IdUsuario}",
                item.Referencia ?? item.Observacion ?? string.Empty
            };
            builder.AppendLine(string.Join(";", values.Select(EscapeCsv)));
        }

        await DescargarBytesAsync(
            $"reporte_inventario_{DateTime.Now:yyyyMMdd_HHmm}.csv",
            Encoding.UTF8.GetBytes("\uFEFF" + builder));
    }

    private IReadOnlyList<IReadOnlyList<string>> ObtenerResumenExportacion() =>
    [
        ["Fecha desde", filtro.FechaDesde?.ToString("dd/MM/yyyy") ?? "Todas"],
        ["Fecha hasta", filtro.FechaHasta?.ToString("dd/MM/yyyy") ?? "Todas"],
        ["Sucursal", filtro.IdSucursal?.ToString() ?? "Todas"],
        ["Producto", string.IsNullOrWhiteSpace(filtro.Producto) ? "Todos" : filtro.Producto],
        ["Código producto", filtro.CodigoProducto?.ToString() ?? "Todos"],
        ["Movimiento", string.IsNullOrWhiteSpace(filtro.TipoMovimiento) ? "Todos" : filtro.TipoMovimiento],
        ["Movimientos exportados", reporte.Movimientos.Count.ToString("N0")],
        ["Total entradas", reporte.TotalEntradas.ToString("N4")],
        ["Valor entradas", reporte.ValorEntradas.ToString("N4")],
        ["Total salidas", reporte.TotalSalidas.ToString("N4")],
        ["Valor salidas", reporte.ValorSalidas.ToString("N4")]
    ];

    private async Task ExportarPdfAsync()
    {
        if (reporte.Movimientos.Count == 0 || usuarioId <= 0)
            return;

        try
        {
            using var response = await Http.PostAsJsonAsync($"api/e-contax/inventarios/reportes/pdf?userId={usuarioId}", filtro);
            if (!response.IsSuccessStatusCode)
            {
                error = await response.Content.ReadAsStringAsync();
                return;
            }

            await DescargarBytesAsync(
                $"reporte_inventario_{DateTime.Now:yyyyMMdd_HHmm}.pdf",
                await response.Content.ReadAsByteArrayAsync());
        }
        catch (Exception ex)
        {
            error = ex.Message;
        }
    }

    private async Task DescargarBytesAsync(string fileName, byte[] content)
    {
        await using var stream = new MemoryStream(content);
        using var streamRef = new DotNetStreamReference(stream);
        await JS.InvokeVoidAsync("downloadFileFromStream", fileName, streamRef);
    }

    private static string EscapeCsv(string value)
    {
        var safe = value.Replace("\"", "\"\"");
        return safe.Contains(';') || safe.Contains('"') || safe.Contains('\n') || safe.Contains('\r')
            ? $"\"{safe}\""
            : safe;
    }

    private static bool EsSalida(string tipo) => tipo.Contains("SALIDA", StringComparison.OrdinalIgnoreCase) || tipo.Contains("BAJA", StringComparison.OrdinalIgnoreCase) ||
        tipo.Equals("SALIDA", StringComparison.OrdinalIgnoreCase) ||
        tipo.Equals("DEVOLUCION_COMPRA", StringComparison.OrdinalIgnoreCase) ||
        tipo.Equals("ANULACION_RECEPCION", StringComparison.OrdinalIgnoreCase) ||
        tipo.Equals("DEVOLUCION_VENTA_REVERSA", StringComparison.OrdinalIgnoreCase);
}
