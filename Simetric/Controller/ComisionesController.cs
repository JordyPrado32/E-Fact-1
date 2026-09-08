using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Simetric.Models.EDeclara;
using Simetric.Services;
using Simetric.Services.EDeclara;

namespace Simetric.Controllers;

[ApiController]
[Authorize]
[Route("api/e-declara/comisiones")]
public sealed class ComisionesController : UsuarioApiControllerBase
{
    private readonly ComisionesService _service;
    private readonly AppAccessService _access;
    public ComisionesController(ComisionesService service, AppAccessService access) { _service = service; _access = access; }

    private async Task<bool> TieneAccesoAsync()
    {
        if (!Request.Path.StartsWithSegments("/api/e-declara/comisiones") ||
            !int.TryParse(User.FindFirst("IdUsuario")?.Value, out var userId)) return false;
        return (await _access.CanAccessAsync(userId, EDeclaraRoutes.ServiceKey,
            User.FindFirst("IdTipoUsuario")?.Value == AppAccessService.AdminRoleId)).HasAccess;
    }

    private bool EsTenantDelUsuario(int idEmpresa) =>
        int.TryParse(User.FindFirst("IdUsuario")?.Value, out var userId) && idEmpresa == userId;

    [HttpGet("resumen")]
    public async Task<IActionResult> Resumen([FromQuery] int idEmpresa, [FromQuery] DateTime? desde, [FromQuery] DateTime? hasta)
        => await TieneAccesoAsync() && EsTenantDelUsuario(idEmpresa) ? Ok(await _service.ObtenerResumenAsync(idEmpresa, desde, hasta)) : Forbid();

    [HttpGet("involucrados")]
    public async Task<IActionResult> Involucrados([FromQuery] int idEmpresa, [FromQuery] string? filtro)
        => await TieneAccesoAsync() && EsTenantDelUsuario(idEmpresa) ? Ok(await _service.ListarInvolucradosAsync(idEmpresa, filtro)) : Forbid();

    [HttpPost("involucrados")]
    public async Task<IActionResult> Guardar([FromBody] ComisionInvolucrado involucrado)
    {
        if (!await TieneAccesoAsync() || !EsTenantDelUsuario(involucrado.IdEmpresa)) return Forbid();
        if (!int.TryParse(User.FindFirst("IdUsuario")?.Value, out var userId)) return Unauthorized();
        try { return Ok(new { id = await _service.GuardarInvolucradoAsync(involucrado, userId) }); }
        catch (ArgumentException ex) { return BadRequest(ex.Message); }
        catch (InvalidOperationException ex) { return Conflict(ex.Message); }
    }

    [HttpGet("movimientos")]
    public async Task<IActionResult> Movimientos([FromQuery] int idEmpresa, [FromQuery] ComisionEstado? estado,
        [FromQuery] DateTime? desde, [FromQuery] DateTime? hasta, [FromQuery] string? filtro)
        => await TieneAccesoAsync() && EsTenantDelUsuario(idEmpresa)
            ? Ok(await _service.ListarMovimientosAsync(idEmpresa, estado, desde, hasta, filtro)) : Forbid();

    [HttpPut("involucrados/{id:int}/estado")]
    public async Task<IActionResult> CambiarEstado(int id, [FromBody] ComisionEstadoRequest request, [FromQuery] bool activo)
    {
        if (!await TieneAccesoAsync() || !EsTenantDelUsuario(request.IdEmpresa)) return Forbid();
        if (!int.TryParse(User.FindFirst("IdUsuario")?.Value, out var userId)) return Unauthorized();
        await _service.CambiarEstadoInvolucradoAsync(request.IdEmpresa, id, activo, userId);
        return NoContent();
    }

    [HttpGet("configuracion")]
    public async Task<IActionResult> Configuracion([FromQuery] int idEmpresa)
        => await TieneAccesoAsync() && EsTenantDelUsuario(idEmpresa) ? Ok(await _service.ObtenerConfiguracionAsync(idEmpresa)) : Forbid();

    [HttpGet("historial")]
    public async Task<IActionResult> Historial([FromQuery] int idEmpresa, [FromQuery] int limite = 12)
        => await TieneAccesoAsync() && EsTenantDelUsuario(idEmpresa)
            ? Ok(await _service.ListarHistorialAsync(idEmpresa, limite)) : Forbid();

    [HttpPut("configuracion")]
    public async Task<IActionResult> GuardarConfiguracion([FromBody] ComisionConfiguracion configuracion)
    {
        if (!await TieneAccesoAsync() || !EsTenantDelUsuario(configuracion.IdEmpresa)) return Forbid();
        if (!int.TryParse(User.FindFirst("IdUsuario")?.Value, out var userId)) return Unauthorized();
        await _service.GuardarConfiguracionAsync(configuracion, userId);
        return NoContent();
    }

    [HttpPut("movimientos/pendientes")]
    public async Task<IActionResult> MarcarPendientes([FromBody] ComisionEstadoRequest request)
    {
        if (!await TieneAccesoAsync() || !EsTenantDelUsuario(request.IdEmpresa)) return Forbid();
        if (!int.TryParse(User.FindFirst("IdUsuario")?.Value, out var userId)) return Unauthorized();
        await _service.MarcarPendientesAsync(request.IdEmpresa, request.Ids, userId);
        return NoContent();
    }

    [HttpPut("movimientos/pagar")]
    public async Task<IActionResult> RegistrarPago([FromBody] ComisionPagoRequest request)
    {
        if (!await TieneAccesoAsync() || !EsTenantDelUsuario(request.IdEmpresa)) return Forbid();
        if (!int.TryParse(User.FindFirst("IdUsuario")?.Value, out var userId)) return Unauthorized();
        await _service.RegistrarPagoAsync(request.IdEmpresa, request.Ids, request.MetodoPago, request.Referencia, userId);
        return NoContent();
    }
}
