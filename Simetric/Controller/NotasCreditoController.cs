using Microsoft.AspNetCore.Mvc;
using Simetric.DTOs;
using Simetric.Models;
using Simetric.Services;

namespace Simetric.Controllers;

[ApiController]
[Route("api/notas-credito")]
public class NotasCreditoController : UsuarioApiControllerBase
{
    private readonly NotaCreditoService _service;
    private readonly FacturacionService _facturacionService;

    public NotasCreditoController(NotaCreditoService service, FacturacionService facturacionService)
    {
        _service = service;
        _facturacionService = facturacionService;
    }

    public sealed class CrearNotaCreditoDto
    {
        public int? IdUsuario { get; set; }
        public NotaCredito NotaCredito { get; set; } = null!;
        public List<NotaCreditoService.DetalleNcDto> Detalles { get; set; } = new();
        public List<FacturaCorreoDestinoDto> Correos { get; set; } = new();
    }

    [HttpPost]
    public async Task<IActionResult> Crear([FromBody] CrearNotaCreditoDto dto)
    {
        var idUsuario = ResolverIdUsuario(dto.IdUsuario);
        if (idUsuario <= 0) return Unauthorized();
        if (dto.NotaCredito is null || dto.Detalles.Count == 0)
            return BadRequest(new { mensaje = "La nota de crédito debe contener cabecera y detalles." });

        dto.NotaCredito.Usuario = idUsuario;
        var sec = await _service.CrearAsync(dto.NotaCredito, dto.Detalles, dto.Correos);
        return Ok(new { sec });
    }

    [HttpGet]
    public async Task<IActionResult> Listar([FromQuery] int idUsuario)
    {
        idUsuario = ResolverIdUsuario(idUsuario);
        return idUsuario <= 0 ? Unauthorized() : Ok(await _service.ListarNotasCreditoUsuarioAsync(idUsuario));
    }

    [HttpGet("buscar-facturas")]
    public async Task<IActionResult> BuscarFacturas([FromQuery] int idUsuario, [FromQuery] string texto)
    {
        idUsuario = ResolverIdUsuario(idUsuario);
        return idUsuario <= 0 ? Unauthorized() : Ok(await _service.BuscarFacturasAutocompleteAsync(texto, idUsuario));
    }

    [HttpGet("facturas/{codFactura:int}/detalles-disponibles")]
    public async Task<IActionResult> GetDetallesDisponibles(int codFactura, [FromQuery] int idUsuario)
    {
        idUsuario = ResolverIdUsuario(idUsuario);
        if (idUsuario <= 0) return Unauthorized();

        if (await _facturacionService.GetFacturaCompletaUsuarioAsync(codFactura, idUsuario) is null) return NotFound();
        return Ok(await _service.ObtenerDetallesDisponiblesAsync(codFactura));
    }

    [HttpGet("{sec:int}")]
    public async Task<IActionResult> Ver(int sec, [FromQuery] int idUsuario)
    {
        idUsuario = ResolverIdUsuario(idUsuario);
        if (idUsuario <= 0) return Unauthorized();
        var nota = await _service.GetNotaCreditoDetalleUsuarioAsync(sec, idUsuario);
        return nota is null ? NotFound() : Ok(nota);
    }

    [HttpGet("{sec:int}/xml")]
    public async Task<IActionResult> GetXml(int sec, [FromQuery] int idUsuario)
    {
        idUsuario = ResolverIdUsuario(idUsuario);
        if (idUsuario <= 0) return Unauthorized();
        var url = await _service.AsegurarXmlNotaCreditoUsuarioAsync(sec, idUsuario);
        return url is null ? NotFound() : Ok(new { url });
    }

    [HttpGet("{sec:int}/pdf")]
    public async Task<IActionResult> GetPdf(int sec, [FromQuery] int idUsuario, [FromQuery] FormatoImpresionDocumento formato = FormatoImpresionDocumento.A4)
    {
        idUsuario = ResolverIdUsuario(idUsuario);
        if (idUsuario <= 0) return Unauthorized();
        var url = await _service.AsegurarPdfNotaCreditoUsuarioAsync(sec, idUsuario, formato);
        return url is null ? NotFound() : Ok(new { url });
    }

    [HttpPost("{sec:int}/emitir")]
    public async Task<IActionResult> Emitir(int sec, [FromQuery] int idUsuario)
    {
        idUsuario = ResolverIdUsuario(idUsuario);
        return idUsuario <= 0 ? Unauthorized() : Ok(await _service.EmitirNotaCreditoSriAsync(sec, idUsuario));
    }

    [HttpPost("{sec:int}/enviar-correo")]
    public async Task<IActionResult> EnviarCorreo(int sec, [FromQuery] int idUsuario)
    {
        idUsuario = ResolverIdUsuario(idUsuario);
        if (idUsuario <= 0) return Unauthorized();
        if (await _service.GetNotaCreditoDetalleUsuarioAsync(sec, idUsuario) is null) return NotFound();
        var resultado = await _service.IntentarEnviarNotaCreditoPorCorreoAsync(sec);
        return resultado.Error ? BadRequest(resultado) : Ok(resultado);
    }

    [HttpDelete("{sec:int}")]
    public async Task<IActionResult> Anular(int sec, [FromQuery] int idUsuario)
    {
        idUsuario = ResolverIdUsuario(idUsuario);
        if (idUsuario <= 0) return Unauthorized();
        if (await _service.GetNotaCreditoDetalleUsuarioAsync(sec, idUsuario) is null) return NotFound();
        return await _service.AnularNotaCreditoDirectoAsync(sec) ? NoContent() : BadRequest();
    }
}
