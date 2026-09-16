using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Simetric.Data;
using Simetric.Services;
using Simetric.Services.ESign;

namespace Simetric.Controllers;

/// <summary>Catálogos y consultas al proveedor de E-Rúbrica para clientes móviles.</summary>
[Authorize]
[ApiController]
[Route("api/mobile/e-rubrica")]
public sealed class ESignMobileCatalogController : ControllerBase
{
    private readonly UanatacaApiService _uanatacaApiService;
    private readonly IDbContextFactory<AppDbContext> _dbFactory;

    public ESignMobileCatalogController(
        UanatacaApiService uanatacaApiService,
        IDbContextFactory<AppDbContext> dbFactory)
    {
        _uanatacaApiService = uanatacaApiService;
        _dbFactory = dbFactory;
    }

    [HttpGet("catalogos/productos")]
    public async Task<IActionResult> ObtenerProductos(CancellationToken cancellationToken) =>
        Ok(await _uanatacaApiService.ObtenerProductosAsync(cancellationToken));

    [HttpGet("catalogos/stakeholder-productos")]
    public async Task<IActionResult> ObtenerProductosStakeholder([FromQuery] string? stakeholderUuid, CancellationToken cancellationToken) =>
        Ok(await _uanatacaApiService.ObtenerProductosStakeholderAsync(stakeholderUuid, cancellationToken));

    [HttpGet("catalogos/saldo")]
    public async Task<IActionResult> ObtenerSaldo(CancellationToken cancellationToken)
    {
        if (!await BackOfficePermissionHelper.PuedeGestionarUanacreditosAsync(User, _dbFactory))
            return Forbid();

        return Ok(new { balance = await _uanatacaApiService.ObtenerSaldoAsync(cancellationToken) });
    }

    [HttpGet("proveedor/solicitudes")]
    public async Task<IActionResult> BuscarSolicitudesProveedor(
        [FromQuery] string? q,
        [FromQuery] string? status,
        [FromQuery] string? uuid,
        CancellationToken cancellationToken) =>
        Ok(await _uanatacaApiService.BuscarSolicitudesAsync(q, status, uuid, cancellationToken));
}
