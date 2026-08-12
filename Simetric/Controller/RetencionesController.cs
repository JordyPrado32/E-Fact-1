using Microsoft.AspNetCore.Mvc;
using Simetric.Services;

namespace Simetric.Controllers;

[ApiController]
[Route("api/retenciones")]
public class RetencionesController : UsuarioApiControllerBase
{
    private readonly RetencionGeneradaService _generadasService;
    private readonly RetencionesService _catalogoService;

    public RetencionesController(
        RetencionGeneradaService generadasService,
        RetencionesService catalogoService)
    {
        _generadasService = generadasService;
        _catalogoService = catalogoService;
    }

    [HttpGet]
    public async Task<IActionResult> Listar([FromQuery] int idUsuario)
    {
        idUsuario = ResolverIdUsuario(idUsuario);
        return idUsuario <= 0 ? Unauthorized() : Ok(await _generadasService.ListarRetencionesUsuarioAsync(idUsuario));
    }

    [HttpGet("catalogo")]
    public async Task<IActionResult> GetCatalogo([FromQuery] string tipo)
    {
        if (string.IsNullOrWhiteSpace(tipo)) return BadRequest(new { mensaje = "Debe indicar el tipo de retención." });
        return Ok(await _catalogoService.ListarPorTipoAsync(tipo));
    }

    [HttpGet("catalogo/buscar")]
    public async Task<IActionResult> BuscarCatalogo([FromQuery] string tipo, [FromQuery] int codigo)
    {
        var retencion = await _catalogoService.BuscarRetencionAsync(tipo, codigo);
        return retencion is null ? NotFound() : Ok(retencion);
    }

    [HttpGet("{sec:int}")]
    public async Task<IActionResult> Ver(int sec, [FromQuery] int idUsuario)
    {
        idUsuario = ResolverIdUsuario(idUsuario);
        if (idUsuario <= 0) return Unauthorized();
        var retencion = await _generadasService.GetRetencionDetalleUsuarioAsync(sec, idUsuario);
        return retencion is null ? NotFound() : Ok(retencion);
    }

    [HttpGet("{sec:int}/xml")]
    public async Task<IActionResult> GetXml(int sec, [FromQuery] int idUsuario)
    {
        idUsuario = ResolverIdUsuario(idUsuario);
        if (idUsuario <= 0) return Unauthorized();
        var url = await _generadasService.AsegurarXmlRetencionUsuarioAsync(sec, idUsuario);
        return url is null ? NotFound() : Ok(new { url });
    }

    [HttpGet("{sec:int}/pdf")]
    public async Task<IActionResult> GetPdf(int sec, [FromQuery] int idUsuario, [FromQuery] FormatoImpresionDocumento formato = FormatoImpresionDocumento.A4)
    {
        idUsuario = ResolverIdUsuario(idUsuario);
        if (idUsuario <= 0) return Unauthorized();
        var url = await _generadasService.AsegurarPdfRetencionUsuarioAsync(sec, idUsuario, formato);
        return url is null ? NotFound() : Ok(new { url });
    }

    [HttpPost("{sec:int}/emitir")]
    public async Task<IActionResult> Emitir(int sec, [FromQuery] int idUsuario)
    {
        idUsuario = ResolverIdUsuario(idUsuario);
        return idUsuario <= 0 ? Unauthorized() : Ok(await _generadasService.EmitirRetencionSriAsync(sec, idUsuario));
    }

    [HttpPost("{sec:int}/enviar-correo")]
    public async Task<IActionResult> EnviarCorreo(int sec, [FromQuery] int idUsuario)
    {
        idUsuario = ResolverIdUsuario(idUsuario);
        if (idUsuario <= 0) return Unauthorized();
        if (await _generadasService.GetRetencionDetalleUsuarioAsync(sec, idUsuario) is null) return NotFound();
        var resultado = await _generadasService.IntentarEnviarRetencionPorCorreoAsync(sec);
        return resultado.Error ? BadRequest(resultado) : Ok(resultado);
    }
}
