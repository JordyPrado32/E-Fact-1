using Microsoft.AspNetCore.Mvc;
using Simetric.Services;

namespace Simetric.Controllers;

[ApiController]
[Route("api/retenciones")]
public class RetencionesGeneradasController : UsuarioApiControllerBase
{
    private readonly RetencionGeneradaService _service;

    public RetencionesGeneradasController(RetencionGeneradaService service)
    {
        _service = service;
    }

    [HttpGet]
    public async Task<IActionResult> Listar([FromQuery] int idUsuario)
    {
        idUsuario = ResolverIdUsuario(idUsuario);
        return idUsuario <= 0 ? Unauthorized() : Ok(await _service.ListarRetencionesUsuarioAsync(idUsuario));
    }

    [HttpGet("{sec:int}/xml")]
    public async Task<IActionResult> GetXml(int sec, [FromQuery] int idUsuario)
    {
        idUsuario = ResolverIdUsuario(idUsuario);
        if (idUsuario <= 0) return Unauthorized();
        var url = await _service.AsegurarXmlRetencionUsuarioAsync(sec, idUsuario);
        return url is null ? NotFound() : Ok(new { url });
    }

    [HttpGet("{sec:int}/pdf")]
    public async Task<IActionResult> GetPdf(int sec, [FromQuery] int idUsuario, [FromQuery] FormatoImpresionDocumento formato = FormatoImpresionDocumento.A4)
    {
        idUsuario = ResolverIdUsuario(idUsuario);
        if (idUsuario <= 0) return Unauthorized();
        var url = await _service.AsegurarPdfRetencionUsuarioAsync(sec, idUsuario, formato);
        return url is null ? NotFound() : Ok(new { url });
    }

    [HttpPost("{sec:int}/emitir")]
    public async Task<IActionResult> Emitir(int sec, [FromQuery] int idUsuario)
    {
        idUsuario = ResolverIdUsuario(idUsuario);
        return idUsuario <= 0 ? Unauthorized() : Ok(await _service.EmitirRetencionSriAsync(sec, idUsuario));
    }

    [HttpPost("{sec:int}/enviar-correo")]
    public async Task<IActionResult> EnviarCorreo(int sec, [FromQuery] int idUsuario)
    {
        idUsuario = ResolverIdUsuario(idUsuario);
        if (idUsuario <= 0) return Unauthorized();
        if (await _service.GetRetencionDetalleUsuarioAsync(sec, idUsuario) is null) return NotFound();
        var resultado = await _service.IntentarEnviarRetencionPorCorreoAsync(sec);
        return resultado.Error ? BadRequest(resultado) : Ok(resultado);
    }
}
