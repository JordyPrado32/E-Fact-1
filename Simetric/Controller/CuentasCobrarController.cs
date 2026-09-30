using Microsoft.AspNetCore.Mvc;
using Simetric.Services;

namespace Simetric.Controllers;

[ApiController]
[Route("api/cuentas-cobrar")]
public class CuentasCobrarController : UsuarioApiControllerBase
{
    private readonly AbonoService _abonoService;
    private readonly IEstadoCuentaPdfService _estadoCuentaPdfService;
    private readonly IEstadoCuentaExcelService _estadoCuentaExcelService;
    private readonly IEmailService _emailService;
    private readonly EmisorSistemaService _emisorSistemaService;

    public CuentasCobrarController(
        AbonoService abonoService,
        IEstadoCuentaPdfService estadoCuentaPdfService,
        IEstadoCuentaExcelService estadoCuentaExcelService,
        IEmailService emailService,
        EmisorSistemaService emisorSistemaService)
    {
        _abonoService = abonoService;
        _estadoCuentaPdfService = estadoCuentaPdfService;
        _estadoCuentaExcelService = estadoCuentaExcelService;
        _emailService = emailService;
        _emisorSistemaService = emisorSistemaService;
    }

    private async Task<(int IdUsuario, int? CodEmisor)> ResolverContextoAsync(int idUsuario, bool backOffice)
    {
        var usuarioAutenticado = ResolverIdUsuario(idUsuario);
        if (usuarioAutenticado <= 0)
            return (0, null);

        if (!backOffice)
            return (usuarioAutenticado, null);

        if (!await _emisorSistemaService.TieneAccesoBackOfficeAsync(usuarioAutenticado))
            return (0, null);

        var emisor = await _emisorSistemaService.GetEmisorSistemaAsync();
        return emisor?.IdUsuario is > 0
            ? (emisor.IdUsuario.Value, EmisorSistemaService.CodigoEmisorBackOffice)
            : (0, null);
    }

    [HttpGet]
    public async Task<IActionResult> GetPendientes([FromQuery] int idUsuario, [FromQuery] string? search = null, [FromQuery] bool backOffice = false)
    {
        var contexto = await ResolverContextoAsync(idUsuario, backOffice);
        if (contexto.IdUsuario <= 0) return Unauthorized();

        var data = string.IsNullOrWhiteSpace(search)
            ? await _abonoService.GetFacturasCreditoPendientes(contexto.IdUsuario, codEmisor: contexto.CodEmisor)
            : await _abonoService.GetFacturasCreditoPendientes(contexto.IdUsuario, search, contexto.CodEmisor);

        return Ok(data);
    }

    [HttpGet("estado-cuenta")]
    public async Task<IActionResult> GetEstadoCuenta([FromQuery] int idUsuario, [FromQuery] bool backOffice = false)
    {
        var contexto = await ResolverContextoAsync(idUsuario, backOffice);
        return contexto.IdUsuario <= 0
            ? Unauthorized()
            : Ok(await _abonoService.GetEstadoCuentaClientesAsync(contexto.IdUsuario, contexto.CodEmisor));
    }

    [HttpGet("estado-cuenta/{idCliente:int}")]
    public async Task<IActionResult> GetEstadoCuentaDetalle(int idCliente, [FromQuery] int idUsuario, [FromQuery] bool backOffice = false)
    {
        var contexto = await ResolverContextoAsync(idUsuario, backOffice);
        if (contexto.IdUsuario <= 0) return Unauthorized();

        var detalle = await _abonoService.GetEstadoCuentaDetalleAsync(contexto.IdUsuario, idCliente, contexto.CodEmisor);
        return detalle is null ? NotFound() : Ok(detalle);
    }

    [HttpGet("estado-cuenta/excel")]
    public async Task<IActionResult> GetEstadoCuentaListadoExcel([FromQuery] int idUsuario, [FromQuery] bool backOffice = false)
    {
        var contexto = await ResolverContextoAsync(idUsuario, backOffice);
        if (contexto.IdUsuario <= 0) return Unauthorized();

        var items = await _abonoService.GetEstadoCuentaClientesAsync(contexto.IdUsuario, contexto.CodEmisor);
        var archivo = await _estadoCuentaExcelService.GenerarListadoAsync(items);
        return File(archivo.Content, archivo.ContentType, archivo.FileName);
    }

    [HttpGet("estado-cuenta/{idCliente:int}/pdf")]
    public async Task<IActionResult> GetEstadoCuentaPdf(int idCliente, [FromQuery] int idUsuario, [FromQuery] bool backOffice = false)
    {
        var contexto = await ResolverContextoAsync(idUsuario, backOffice);
        if (contexto.IdUsuario <= 0) return Unauthorized();

        var detalle = await _abonoService.GetEstadoCuentaDetalleAsync(contexto.IdUsuario, idCliente, contexto.CodEmisor);
        if (detalle is null) return NotFound();

        var archivo = await _estadoCuentaPdfService.GenerarDetalleAsync(detalle);
        return File(archivo.Content, archivo.ContentType, archivo.FileName);
    }

    [HttpGet("estado-cuenta/{idCliente:int}/excel")]
    public async Task<IActionResult> GetEstadoCuentaExcel(int idCliente, [FromQuery] int idUsuario, [FromQuery] bool backOffice = false)
    {
        var contexto = await ResolverContextoAsync(idUsuario, backOffice);
        if (contexto.IdUsuario <= 0) return Unauthorized();

        var detalle = await _abonoService.GetEstadoCuentaDetalleAsync(contexto.IdUsuario, idCliente, contexto.CodEmisor);
        if (detalle is null) return NotFound();

        var archivo = await _estadoCuentaExcelService.GenerarDetalleAsync(detalle);
        return File(archivo.Content, archivo.ContentType, archivo.FileName);
    }

    [HttpPost("estado-cuenta/{idCliente:int}/enviar")]
    public async Task<IActionResult> EnviarEstadoCuenta(int idCliente, [FromQuery] int idUsuario, [FromQuery] bool backOffice = false)
    {
        var contexto = await ResolverContextoAsync(idUsuario, backOffice);
        if (contexto.IdUsuario <= 0) return Unauthorized();

        var detalle = await _abonoService.GetEstadoCuentaDetalleAsync(contexto.IdUsuario, idCliente, contexto.CodEmisor);
        if (detalle is null) return NotFound();
        if (string.IsNullOrWhiteSpace(detalle.Correo))
            return BadRequest("El cliente no tiene un correo registrado para enviar el estado de cuenta.");

        var archivo = await _estadoCuentaPdfService.GenerarDetalleAsync(detalle);
        await _emailService.EnviarEstadoCuentaAsync(
            new[] { detalle.Correo },
            detalle,
            archivo.Content,
            archivo.FileName);

        return Ok(new { message = "Estado de cuenta enviado correctamente.", correo = detalle.Correo });
    }

    [HttpGet("abonos")]
    public async Task<IActionResult> GetAbonos([FromQuery] int idUsuario, [FromQuery] bool backOffice = false)
    {
        var contexto = await ResolverContextoAsync(idUsuario, backOffice);
        return contexto.IdUsuario <= 0
            ? Unauthorized()
            : Ok(await _abonoService.GetEstadoCuentaClientesAsync(contexto.IdUsuario, contexto.CodEmisor));
    }

    [HttpPost("abonos")]
    public async Task<IActionResult> RegistrarAbono([FromQuery] int idUsuario, [FromBody] RegistrarAbonoMobileDto model, [FromQuery] bool backOffice = false)
    {
        var contexto = await ResolverContextoAsync(idUsuario, backOffice);
        if (contexto.IdUsuario <= 0) return Unauthorized();
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
            contexto.IdUsuario,
            model.IdCliente,
            model.MontoRecibido,
            distribucion,
            model.Observacion ?? "Abono registrado desde movil",
            model.UsarSaldoAFavor,
            contexto.CodEmisor);

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
