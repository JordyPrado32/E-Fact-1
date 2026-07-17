using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Simetric.Services;
using Simetric.Services.ESign;

namespace Simetric.Controllers;

[Authorize]
[ApiController]
[Route("api/e-sign/bes")]
public sealed class ESignBesController : ControllerBase
{
    private readonly BesPrecompraService _besPrecompraService;
    private readonly SolicitudService _solicitudService;

    public ESignBesController(BesPrecompraService besPrecompraService, SolicitudService solicitudService)
    {
        _besPrecompraService = besPrecompraService;
        _solicitudService = solicitudService;
    }

    [HttpGet("productos")]
    public async Task<IActionResult> ObtenerProductos(CancellationToken cancellationToken)
        => Ok(await _besPrecompraService.ObtenerProductosAsync(cancellationToken));

    [HttpGet("stakeholder-productos")]
    public async Task<IActionResult> ObtenerProductosStakeholder([FromQuery] string? stakeholderUuid, CancellationToken cancellationToken)
        => Ok(await _besPrecompraService.ObtenerProductosStakeholderAsync(stakeholderUuid, cancellationToken));

    [HttpGet("saldo")]
    public async Task<IActionResult> ObtenerSaldo(CancellationToken cancellationToken)
        => Ok(new { balance = await _besPrecompraService.ObtenerSaldoAsync(cancellationToken) });

    [HttpGet("solicitudes")]
    public async Task<IActionResult> BuscarSolicitudes(
        [FromQuery] string? q,
        [FromQuery] string? status,
        [FromQuery] string? uuid,
        CancellationToken cancellationToken)
        => Ok(await _besPrecompraService.BuscarSolicitudesAsync(q, status, uuid, cancellationToken));

    [HttpPost("solicitudes/{solId:int}/sincronizar")]
    public async Task<IActionResult> SincronizarSolicitud(int solId, CancellationToken cancellationToken)
    {
        var result = await _solicitudService.SincronizarSolicitudBesAsync(solId, cancellationToken: cancellationToken);
        return result.Success ? Ok(result) : BadRequest(result);
    }
}
