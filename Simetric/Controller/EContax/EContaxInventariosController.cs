using Microsoft.AspNetCore.Mvc;
using Simetric.DTOs.EContax;
using Simetric.Services.EContax;

namespace Simetric.Controllers.EContax;

[ApiController]
[Route("api/e-contax/inventarios")]
public sealed class EContaxInventariosController : EContaxApiControllerBase
{
    protected override string RutaPermiso => EContaxRoutes.Inventarios;
    private readonly EContaxInventarioService _inventario;
    private readonly IEContaxInventarioReportePdfService _reportePdf;

    public EContaxInventariosController(EContaxInventarioService inventario, IEContaxInventarioReportePdfService reportePdf)
    {
        _inventario = inventario;
        _reportePdf = reportePdf;
    }

    [HttpGet]
    public async Task<ActionResult<EContaxInventarioDashboardDto>> GetDashboard(
        [FromQuery] int userId,
        [FromQuery] int? sucursalId = null,
        [FromQuery] string? filtro = null)
    {
        try
        {
            return Ok(await _inventario.GetDashboardAsync(userId, sucursalId, filtro));
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(ex.Message);
        }
    }

    [HttpGet("{codigoProducto:int}/kardex")]
    public async Task<ActionResult<EContaxInventarioKardexDto>> GetKardex(
        int codigoProducto,
        [FromQuery] int userId,
        [FromQuery] int? sucursalId = null)
    {
        try
        {
            return Ok(await _inventario.GetKardexAsync(userId, codigoProducto, sucursalId));
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(ex.Message);
        }
    }

    [HttpPut("configuracion")]
    public async Task<IActionResult> GuardarConfiguracion(
        [FromQuery] int userId,
        [FromBody] EContaxInventarioConfiguracionDto model)
    {
        try
        {
            await _inventario.GuardarConfiguracionAsync(userId, model);
            return NoContent();
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(ex.Message);
        }
    }

    [HttpPost("ajuste")]
    public async Task<IActionResult> Ajustar(
        [FromQuery] int userId,
        [FromBody] EContaxInventarioAjusteDto model)
    {
        try
        {
            await _inventario.AplicarAjusteAsync(userId, model);
            return NoContent();
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(ex.Message);
        }
    }

    [HttpPost("transferencias")]
    public async Task<ActionResult<EContaxInventarioOperacionResultadoDto>> Transferir(
        [FromQuery] int userId, [FromBody] EContaxInventarioTransferenciaDto model)
    {
        try { return Ok(await _inventario.CrearTransferenciaAsync(userId, model)); }
        catch (InvalidOperationException ex) { return BadRequest(ex.Message); }
    }

    [HttpPost("devoluciones-compra")]
    public async Task<ActionResult<EContaxInventarioOperacionResultadoDto>> DevolverCompra(
        [FromQuery] int userId, [FromBody] EContaxInventarioDevolucionDto model)
    {
        try { return Ok(await _inventario.RegistrarDevolucionCompraAsync(userId, model)); }
        catch (InvalidOperationException ex) { return BadRequest(ex.Message); }
    }

    [HttpPost("tomas-fisicas")]
    public async Task<ActionResult<EContaxInventarioOperacionResultadoDto>> TomaFisica(
        [FromQuery] int userId, [FromBody] EContaxInventarioTomaFisicaDto model)
    {
        try { return Ok(await _inventario.AplicarTomaFisicaAsync(userId, model)); }
        catch (InvalidOperationException ex) { return BadRequest(ex.Message); }
    }

    [HttpPost("reservas")]
    public async Task<IActionResult> Reservar(
        [FromQuery] int userId, [FromBody] EContaxInventarioReservaDto model)
    {
        try { await _inventario.ReservarAsync(userId, model); return NoContent(); }
        catch (InvalidOperationException ex) { return BadRequest(ex.Message); }
    }

    [HttpPost("reservas/liberar")]
    public async Task<IActionResult> LiberarReserva(
        [FromQuery] int userId, [FromBody] EContaxInventarioReservaDto model)
    {
        try { await _inventario.ReservarAsync(userId, model, true); return NoContent(); }
        catch (InvalidOperationException ex) { return BadRequest(ex.Message); }
    }

    [HttpPost("revalorizaciones")]
    public async Task<IActionResult> Revalorizar(
        [FromQuery] int userId, [FromBody] EContaxInventarioRevalorizacionDto model)
    {
        try { await _inventario.RevalorizarAsync(userId, model); return NoContent(); }
        catch (InvalidOperationException ex) { return BadRequest(ex.Message); }
    }

    [HttpPost("reportes")]
    public async Task<ActionResult<EContaxInventarioReporteDto>> Reporte(
        [FromQuery] int userId, [FromBody] EContaxInventarioReporteFiltroDto model)
    {
        try { return Ok(await _inventario.GetReporteAsync(userId, model ?? new())); }
        catch (InvalidOperationException ex) { return BadRequest(ex.Message); }
    }

    [HttpPost("reportes/pdf")]
    public async Task<IActionResult> ReportePdf(
        [FromQuery] int userId, [FromBody] EContaxInventarioReporteFiltroDto model)
    {
        try
        {
            var reporte = await _inventario.GetReporteAsync(userId, model ?? new());
            return File(_reportePdf.Generar(reporte), "application/pdf", $"reporte_inventario_{DateTime.Now:yyyyMMdd_HHmm}.pdf");
        }
        catch (InvalidOperationException ex) { return BadRequest(ex.Message); }
    }
}
