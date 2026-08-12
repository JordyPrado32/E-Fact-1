using Microsoft.AspNetCore.Mvc;
using Simetric.DTOs;
using Simetric.Services;

namespace Simetric.Controllers;

[ApiController]
[Route("api/liquidaciones-compra")]
public class LiquidacionesCompraController : UsuarioApiControllerBase
{
    private readonly LiquidacionCompraService _service;

    public LiquidacionesCompraController(LiquidacionCompraService service) => _service = service;

    [HttpPost]
    public async Task<IActionResult> Crear([FromQuery] int? idUsuario, [FromBody] LiquidacionCompraPreviewDto preview)
    {
        var usuario = ResolverIdUsuario(idUsuario ?? preview.Usuario);
        if (usuario <= 0) return Unauthorized();

        preview.Usuario = usuario;
        return Ok(await _service.GuardarLiquidacionConArchivosAsync(preview));
    }

    [HttpGet]
    public async Task<IActionResult> Listar([FromQuery] int idUsuario)
    {
        idUsuario = ResolverIdUsuario(idUsuario);
        return idUsuario <= 0 ? Unauthorized() : Ok(await _service.ListarLiquidacionesUsuarioAsync(idUsuario));
    }

    [HttpGet("preparacion")]
    public async Task<IActionResult> GetPreparacion([FromQuery] int idUsuario)
    {
        idUsuario = ResolverIdUsuario(idUsuario);
        if (idUsuario <= 0) return Unauthorized();

        var previewTask = _service.CrearPreviewManualAsync(idUsuario);
        var identificacionesTask = _service.ObtenerTiposIdentificacionProveedorAsync();
        var formasPagoTask = _service.ObtenerFormasPagoCompraAsync();
        await Task.WhenAll(previewTask, identificacionesTask, formasPagoTask);

        return Ok(new
        {
            preview = await previewTask,
            tiposIdentificacion = await identificacionesTask,
            formasPago = await formasPagoTask
        });
    }

    [HttpGet("proveedores")]
    public async Task<IActionResult> BuscarProveedores([FromQuery] int idUsuario, [FromQuery] string? filtro = null)
    {
        idUsuario = ResolverIdUsuario(idUsuario);
        return idUsuario <= 0
            ? Unauthorized()
            : Ok(await _service.BuscarProveedoresAsync(filtro, idUsuario));
    }

    [HttpGet("{codFactura:int}")]
    public async Task<IActionResult> Ver(int codFactura, [FromQuery] int idUsuario)
    {
        idUsuario = ResolverIdUsuario(idUsuario);
        if (idUsuario <= 0) return Unauthorized();
        var liquidacion = await _service.GetLiquidacionDetalleUsuarioAsync(codFactura, idUsuario);
        return liquidacion is null ? NotFound() : Ok(liquidacion);
    }

    [HttpGet("{codFactura:int}/xml")]
    public async Task<IActionResult> GetXml(int codFactura, [FromQuery] int idUsuario)
    {
        idUsuario = ResolverIdUsuario(idUsuario);
        if (idUsuario <= 0) return Unauthorized();
        var url = await _service.AsegurarXmlLiquidacionUsuarioAsync(codFactura, idUsuario);
        return url is null ? NotFound() : Ok(new { url });
    }

    [HttpGet("{codFactura:int}/pdf")]
    public async Task<IActionResult> GetPdf(int codFactura, [FromQuery] int idUsuario, [FromQuery] FormatoImpresionDocumento formato = FormatoImpresionDocumento.A4)
    {
        idUsuario = ResolverIdUsuario(idUsuario);
        if (idUsuario <= 0) return Unauthorized();
        var url = await _service.AsegurarPdfLiquidacionUsuarioAsync(codFactura, idUsuario, formato);
        return url is null ? NotFound() : Ok(new { url });
    }

    [HttpPost("{codFactura:int}/emitir")]
    public async Task<IActionResult> Emitir(int codFactura, [FromQuery] int idUsuario)
    {
        idUsuario = ResolverIdUsuario(idUsuario);
        return idUsuario <= 0 ? Unauthorized() : Ok(await _service.EmitirLiquidacionSriAsync(codFactura, idUsuario));
    }

    [HttpPost("{codFactura:int}/enviar-correo")]
    public async Task<IActionResult> EnviarCorreo(int codFactura, [FromQuery] int idUsuario)
    {
        idUsuario = ResolverIdUsuario(idUsuario);
        if (idUsuario <= 0) return Unauthorized();
        if (await _service.GetLiquidacionDetalleUsuarioAsync(codFactura, idUsuario) is null) return NotFound();
        var resultado = await _service.IntentarEnviarLiquidacionPorCorreoAsync(codFactura);
        return resultado.Error ? BadRequest(resultado) : Ok(resultado);
    }
}
