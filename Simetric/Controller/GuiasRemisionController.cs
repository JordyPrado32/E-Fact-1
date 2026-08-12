using Microsoft.AspNetCore.Mvc;
using Simetric.Models;
using Simetric.Services;

namespace Simetric.Controllers;

[ApiController]
[Route("api/guias-remision")]
public class GuiasRemisionController : UsuarioApiControllerBase
{
    private readonly GuiaRemisionService _service;

    public GuiasRemisionController(GuiaRemisionService service) => _service = service;

    public sealed class CrearGuiaRemisionDto
    {
        public int? IdUsuario { get; set; }
        public int? CodFactura { get; set; }
        public int? CodEmisor { get; set; }
        public Transportista Transportista { get; set; } = null!;
        public GuiaRemision Guia { get; set; } = null!;
        public GuiaDestinatario Destinatario { get; set; } = null!;
        public List<DetalleGuiaRemision> Detalles { get; set; } = new();
    }

    public sealed class EnviarCorreoDto
    {
        public int? IdUsuario { get; set; }
        public bool ForzarReenvio { get; set; }
    }

    [HttpPost]
    public async Task<IActionResult> Crear([FromBody] CrearGuiaRemisionDto dto)
    {
        var idUsuario = ResolverIdUsuario(dto.IdUsuario);
        if (idUsuario <= 0) return Unauthorized();
        if (dto.Transportista is null || dto.Guia is null || dto.Destinatario is null || dto.Detalles.Count == 0)
            return BadRequest(new { mensaje = "La guía debe contener transportista, cabecera, destinatario y detalles." });

        var resultado = await _service.GuardarGuiaRemisionCompletaAsync(
            idUsuario,
            dto.CodFactura,
            dto.CodEmisor,
            dto.Transportista,
            dto.Guia,
            dto.Destinatario,
            dto.Detalles);
        return Ok(resultado);
    }

    [HttpGet]
    public async Task<IActionResult> Listar([FromQuery] int idUsuario)
    {
        idUsuario = ResolverIdUsuario(idUsuario);
        return idUsuario <= 0 ? Unauthorized() : Ok(await _service.ListarGuiasRemisionUsuarioAsync(idUsuario));
    }

    [HttpGet("preparacion")]
    public async Task<IActionResult> GetPreparacion([FromQuery] int idUsuario, [FromQuery] string? serie = null)
    {
        idUsuario = ResolverIdUsuario(idUsuario);
        if (idUsuario <= 0) return Unauthorized();

        var serieVisual = await _service.GetSerieGuiaVisualAsync(idUsuario);
        var siguiente = await _service.GetNextGuiaRemisionNumeroAsync(idUsuario, serie);
        return Ok(new { serie = serieVisual, proximo = siguiente });
    }

    [HttpGet("transportistas")]
    public async Task<IActionResult> BuscarTransportistas([FromQuery] int idUsuario, [FromQuery] string? filtro = null)
    {
        idUsuario = ResolverIdUsuario(idUsuario);
        return idUsuario <= 0
            ? Unauthorized()
            : Ok(await _service.BuscarTransportistasAsync(idUsuario, filtro ?? string.Empty));
    }

    [HttpGet("transportistas/por-identificacion")]
    public async Task<IActionResult> GetTransportista([FromQuery] int idUsuario, [FromQuery] string identificacion)
    {
        idUsuario = ResolverIdUsuario(idUsuario);
        if (idUsuario <= 0) return Unauthorized();
        var transportista = await _service.GetTransportistaPorIdentificacionAsync(idUsuario, identificacion);
        return transportista is null ? NotFound() : Ok(transportista);
    }

    [HttpGet("{sec:int}")]
    public async Task<IActionResult> Ver(int sec, [FromQuery] int idUsuario)
    {
        idUsuario = ResolverIdUsuario(idUsuario);
        if (idUsuario <= 0) return Unauthorized();
        var guia = await _service.GetGuiaRemisionDetalleUsuarioAsync(sec, idUsuario);
        return guia is null ? NotFound() : Ok(guia);
    }

    [HttpGet("{sec:int}/xml")]
    public async Task<IActionResult> GetXml(int sec, [FromQuery] int idUsuario)
    {
        idUsuario = ResolverIdUsuario(idUsuario);
        if (idUsuario <= 0) return Unauthorized();
        var url = await _service.AsegurarXmlGuiaRemisionUsuarioAsync(sec, idUsuario);
        return url is null ? NotFound() : Ok(new { url });
    }

    [HttpGet("{sec:int}/pdf")]
    public async Task<IActionResult> GetPdf(int sec, [FromQuery] int idUsuario, [FromQuery] FormatoImpresionDocumento formato = FormatoImpresionDocumento.A4)
    {
        idUsuario = ResolverIdUsuario(idUsuario);
        if (idUsuario <= 0) return Unauthorized();
        var url = await _service.AsegurarPdfGuiaRemisionUsuarioAsync(sec, idUsuario, formato);
        return url is null ? NotFound() : Ok(new { url });
    }

    [HttpPost("{sec:int}/emitir")]
    public async Task<IActionResult> Emitir(int sec, [FromQuery] int idUsuario)
    {
        idUsuario = ResolverIdUsuario(idUsuario);
        return idUsuario <= 0 ? Unauthorized() : Ok(await _service.EmitirGuiaRemisionSriAsync(sec, idUsuario));
    }

    [HttpPost("{sec:int}/enviar-correo")]
    public async Task<IActionResult> EnviarCorreo(int sec, [FromBody] EnviarCorreoDto dto)
    {
        var idUsuario = ResolverIdUsuario(dto.IdUsuario);
        if (idUsuario <= 0) return Unauthorized();
        if (await _service.GetGuiaRemisionDetalleUsuarioAsync(sec, idUsuario) is null) return NotFound();
        var resultado = await _service.IntentarEnviarGuiaRemisionPorCorreoAsync(sec, forzarReenvio: dto.ForzarReenvio);
        return resultado.Error ? BadRequest(resultado) : Ok(resultado);
    }

    [HttpDelete("{sec:int}")]
    public async Task<IActionResult> Anular(int sec, [FromQuery] int idUsuario)
    {
        idUsuario = ResolverIdUsuario(idUsuario);
        if (idUsuario <= 0) return Unauthorized();
        return await _service.AnularGuiaRemisionDirectoAsync(sec, idUsuario) ? NoContent() : BadRequest();
    }
}
