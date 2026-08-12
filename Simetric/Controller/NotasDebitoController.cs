using Microsoft.AspNetCore.Mvc;
using Simetric.DTOs;
using Simetric.Models;
using Simetric.Services;

namespace Simetric.Controllers;

[ApiController]
[Route("api/notas-debito")]
public class NotasDebitoController : UsuarioApiControllerBase
{
    private readonly NotaDebitoService _service;
    private readonly FacturacionService _facturacionService;

    public NotasDebitoController(NotaDebitoService service, FacturacionService facturacionService)
    {
        _service = service;
        _facturacionService = facturacionService;
    }

    public sealed class CrearNotaDebitoDto
    {
        public int? IdUsuario { get; set; }
        public NotaDebito NotaDebito { get; set; } = null!;
        public List<NotaDebitoService.DetalleNdDto> Detalles { get; set; } = new();
        public List<FacturaCorreoDestinoDto> Correos { get; set; } = new();
    }

    public sealed class EnviarCorreoDto
    {
        public int? IdUsuario { get; set; }
        public bool ForzarReenvio { get; set; }
        public List<string?> CorreosExtra { get; set; } = new();
    }

    [HttpPost]
    public async Task<IActionResult> Crear([FromBody] CrearNotaDebitoDto dto)
    {
        var idUsuario = ResolverIdUsuario(dto.IdUsuario);
        if (idUsuario <= 0) return Unauthorized();
        if (dto.NotaDebito is null || dto.Detalles.Count == 0)
            return BadRequest(new { mensaje = "La nota de débito debe contener cabecera y detalles." });

        dto.NotaDebito.Usuario = idUsuario;
        var sec = await _service.CrearAsync(dto.NotaDebito, dto.Detalles, dto.Correos);
        return Ok(new { sec });
    }

    [HttpGet]
    public async Task<IActionResult> Listar([FromQuery] int idUsuario)
    {
        idUsuario = ResolverIdUsuario(idUsuario);
        return idUsuario <= 0 ? Unauthorized() : Ok(await _service.ListarNotasDebitoUsuarioAsync(idUsuario));
    }

    [HttpGet("buscar-facturas")]
    public async Task<IActionResult> BuscarFacturas([FromQuery] int idUsuario, [FromQuery] string texto)
    {
        idUsuario = ResolverIdUsuario(idUsuario);
        return idUsuario <= 0 ? Unauthorized() : Ok(await _service.BuscarFacturasAutocompleteAsync(texto, idUsuario));
    }

    [HttpGet("facturas/{codFactura:int}/detalles")]
    public async Task<IActionResult> GetDetallesFactura(int codFactura, [FromQuery] int idUsuario)
    {
        idUsuario = ResolverIdUsuario(idUsuario);
        if (idUsuario <= 0) return Unauthorized();
        if (await _facturacionService.GetFacturaCompletaUsuarioAsync(codFactura, idUsuario) is null) return NotFound();
        return Ok(await _service.ObtenerDetallesFacturaAsync(codFactura));
    }

    [HttpGet("{sec:int}")]
    public async Task<IActionResult> Ver(int sec, [FromQuery] int idUsuario)
    {
        idUsuario = ResolverIdUsuario(idUsuario);
        if (idUsuario <= 0) return Unauthorized();
        var nota = await _service.GetNotaDebitoDetalleUsuarioAsync(sec, idUsuario);
        return nota is null ? NotFound() : Ok(nota);
    }

    [HttpGet("{sec:int}/xml")]
    public async Task<IActionResult> GetXml(int sec, [FromQuery] int idUsuario)
    {
        idUsuario = ResolverIdUsuario(idUsuario);
        if (idUsuario <= 0) return Unauthorized();
        var url = await _service.AsegurarXmlNotaDebitoUsuarioAsync(sec, idUsuario);
        return url is null ? NotFound() : Ok(new { url });
    }

    [HttpGet("{sec:int}/pdf")]
    public async Task<IActionResult> GetPdf(int sec, [FromQuery] int idUsuario, [FromQuery] FormatoImpresionDocumento formato = FormatoImpresionDocumento.A4)
    {
        idUsuario = ResolverIdUsuario(idUsuario);
        if (idUsuario <= 0) return Unauthorized();
        var url = await _service.AsegurarPdfNotaDebitoUsuarioAsync(sec, idUsuario, formato);
        return url is null ? NotFound() : Ok(new { url });
    }

    [HttpPost("{sec:int}/emitir")]
    public async Task<IActionResult> Emitir(int sec, [FromQuery] int idUsuario)
    {
        idUsuario = ResolverIdUsuario(idUsuario);
        return idUsuario <= 0 ? Unauthorized() : Ok(await _service.EmitirNotaDebitoSriAsync(sec, idUsuario));
    }

    [HttpPost("{sec:int}/enviar-correo")]
    public async Task<IActionResult> EnviarCorreo(int sec, [FromBody] EnviarCorreoDto dto)
    {
        var idUsuario = ResolverIdUsuario(dto.IdUsuario);
        if (idUsuario <= 0) return Unauthorized();
        if (await _service.GetNotaDebitoDetalleUsuarioAsync(sec, idUsuario) is null) return NotFound();
        var resultado = await _service.IntentarEnviarNotaDebitoPorCorreoAsync(
            sec,
            correosExtra: dto.CorreosExtra,
            forzarReenvio: dto.ForzarReenvio);
        return resultado.Error ? BadRequest(resultado) : Ok(resultado);
    }

    [HttpDelete("{sec:int}")]
    public async Task<IActionResult> Anular(int sec, [FromQuery] int idUsuario)
    {
        idUsuario = ResolverIdUsuario(idUsuario);
        if (idUsuario <= 0) return Unauthorized();
        return await _service.AnularNotaDebitoDirectoAsync(sec, idUsuario) ? NoContent() : BadRequest();
    }
}
