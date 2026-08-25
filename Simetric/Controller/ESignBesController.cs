using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Simetric.Services;
using Simetric.Services.ESign;

namespace Simetric.Controllers;

[Authorize]
[ApiController]
[Route("api/e-rubrica/uanataca")]
public sealed class ESignUanatacaController : ControllerBase
{
    private readonly UanatacaApiService _uanatacaApiService;
    private readonly SolicitudService _solicitudService;

    public ESignUanatacaController(UanatacaApiService uanatacaApiService, SolicitudService solicitudService)
    {
        _uanatacaApiService = uanatacaApiService;
        _solicitudService = solicitudService;
    }

    [HttpGet("productos")]
    public async Task<IActionResult> ObtenerProductos(CancellationToken cancellationToken)
        => Ok(await _uanatacaApiService.ObtenerProductosAsync(cancellationToken));

    [HttpGet("stakeholder-productos")]
    public async Task<IActionResult> ObtenerProductosStakeholder([FromQuery] string? stakeholderUuid, CancellationToken cancellationToken)
        => Ok(await _uanatacaApiService.ObtenerProductosStakeholderAsync(stakeholderUuid, cancellationToken));

    [HttpGet("saldo")]
    public async Task<IActionResult> ObtenerSaldo(CancellationToken cancellationToken)
        => Ok(new { balance = await _uanatacaApiService.ObtenerSaldoAsync(cancellationToken) });

    [HttpGet("solicitudes")]
    public async Task<IActionResult> BuscarSolicitudes(
        [FromQuery] string? q,
        [FromQuery] string? status,
        [FromQuery] string? uuid,
        CancellationToken cancellationToken)
        => Ok(await _uanatacaApiService.BuscarSolicitudesAsync(q, status, uuid, cancellationToken));

    [HttpPost("solicitudes/{solId:int}/sincronizar")]
    public async Task<IActionResult> SincronizarSolicitud(int solId, CancellationToken cancellationToken)
    {
        var result = await _solicitudService.SincronizarSolicitudUanatacaAsync(solId, cancellationToken: cancellationToken);
        return result.Success ? Ok(result) : BadRequest(result);
    }
}
