using Microsoft.AspNetCore.Mvc;
using Simetric.Services;

namespace Simetric.Controllers;

[ApiController]
[Route("api/cuentas-cobrar")]
public class CuentasCobrarController : UsuarioApiControllerBase
{
    private readonly AbonoService _abonoService;

    public CuentasCobrarController(AbonoService abonoService)
    {
        _abonoService = abonoService;
    }

    [HttpGet]
    public async Task<IActionResult> GetPendientes([FromQuery] int idUsuario, [FromQuery] string? search = null)
    {
        idUsuario = ResolverIdUsuario(idUsuario);
        if (idUsuario <= 0) return Unauthorized();

        var data = string.IsNullOrWhiteSpace(search)
            ? await _abonoService.GetFacturasCreditoPendientes(idUsuario)
            : await _abonoService.GetFacturasCreditoPendientes(idUsuario, search);

        return Ok(data);
    }

    [HttpGet("estado-cuenta")]
    public async Task<IActionResult> GetEstadoCuenta([FromQuery] int idUsuario)
    {
        idUsuario = ResolverIdUsuario(idUsuario);
        return idUsuario <= 0
            ? Unauthorized()
            : Ok(await _abonoService.GetEstadoCuentaClientesAsync(idUsuario));
    }

    [HttpGet("estado-cuenta/{idCliente:int}")]
    public async Task<IActionResult> GetEstadoCuentaDetalle(int idCliente, [FromQuery] int idUsuario)
    {
        idUsuario = ResolverIdUsuario(idUsuario);
        if (idUsuario <= 0) return Unauthorized();

        var detalle = await _abonoService.GetEstadoCuentaDetalleAsync(idUsuario, idCliente);
        return detalle is null ? NotFound() : Ok(detalle);
    }

    [HttpGet("abonos")]
    public async Task<IActionResult> GetAbonos([FromQuery] int idUsuario)
    {
        idUsuario = ResolverIdUsuario(idUsuario);
        return idUsuario <= 0
            ? Unauthorized()
            : Ok(await _abonoService.GetEstadoCuentaClientesAsync(idUsuario));
    }

    [HttpPost("abonos")]
    public async Task<IActionResult> RegistrarAbono([FromQuery] int idUsuario, [FromBody] RegistrarAbonoMobileDto model)
    {
        idUsuario = ResolverIdUsuario(idUsuario);
        if (idUsuario <= 0) return Unauthorized();
        if (model.IdCliente <= 0) return BadRequest("Debe seleccionar un cliente.");
        if (model.MontoRecibido <= 0) return BadRequest("El monto recibido debe ser mayor a cero.");

        var distribucion = model.Distribucion?.Where(x => x.Monto > 0).ToDictionary(x => x.IdFactura, x => x.Monto)
            ?? new Dictionary<int, decimal>();

        if (distribucion.Count == 0 && model.IdFactura.GetValueOrDefault() > 0)
        {
            distribucion[model.IdFactura!.Value] = model.MontoRecibido;
        }

        if (distribucion.Count == 0)
        {
            return BadRequest("Debe indicar al menos una factura para aplicar el abono.");
        }

        var ok = await _abonoService.RegistrarPagoManual(
            idUsuario,
            model.IdCliente,
            model.MontoRecibido,
            distribucion,
            model.Observacion ?? "Abono registrado desde movil",
            model.UsarSaldoAFavor);

        return ok ? Ok(new { message = "Abono registrado correctamente." }) : BadRequest("No se pudo registrar el abono.");
    }
}

public sealed class RegistrarAbonoMobileDto
{
    public int IdCliente { get; set; }
    public int? IdFactura { get; set; }
    public decimal MontoRecibido { get; set; }
    public string? Observacion { get; set; }
    public bool UsarSaldoAFavor { get; set; }
    public List<RegistrarAbonoDistribucionDto>? Distribucion { get; set; }
}

public sealed class RegistrarAbonoDistribucionDto
{
    public int IdFactura { get; set; }
    public decimal Monto { get; set; }
}
