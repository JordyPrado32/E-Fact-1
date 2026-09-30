using Microsoft.AspNetCore.Mvc;
using Simetric.DTOs;
using Simetric.Services;

namespace Simetric.Controllers;

[ApiController]
[Route("api/reportes/documentos")]
public class ReportesDocumentosController : UsuarioApiControllerBase
{
    private readonly ReporteComprobantesService _reporteService;
    private readonly EmisorSistemaService _emisorSistemaService;

    public ReportesDocumentosController(ReporteComprobantesService reporteService, EmisorSistemaService emisorSistemaService)
    {
        _reporteService = reporteService;
        _emisorSistemaService = emisorSistemaService;
    }

    private async Task<(int IdUsuario, int? CodEmisor)> ResolverContextoAsync(int idUsuario, bool backOffice)
    {
        var usuario = ResolverIdUsuario(idUsuario);
        if (usuario <= 0)
            return (0, null);
        if (!backOffice)
            return (usuario, null);
        if (!await _emisorSistemaService.TieneAccesoBackOfficeAsync(usuario))
            return (0, null);
        var emisor = await _emisorSistemaService.GetEmisorSistemaAsync();
        return emisor?.IdUsuario is > 0
            ? (emisor.IdUsuario.Value, EmisorSistemaService.CodigoEmisorBackOffice)
            : (0, null);
    }

    [HttpGet]
    public async Task<IActionResult> GetDocumentos([FromQuery] int idUsuario, [FromQuery] string? search = null, [FromQuery] bool backOffice = false)
    {
        var contexto = await ResolverContextoAsync(idUsuario, backOffice);
        if (contexto.IdUsuario <= 0) return Unauthorized();

        var reporte = await _reporteService.ObtenerReporteUsuarioAsync(contexto.IdUsuario, contexto.CodEmisor);
        reporte.Items = Filtrar(reporte.Items, search).ToList();

        return Ok(reporte);
    }

    [HttpGet("emitidos")]
    public async Task<IActionResult> GetEmitidos([FromQuery] int idUsuario, [FromQuery] string? search = null, [FromQuery] bool backOffice = false)
    {
        var contexto = await ResolverContextoAsync(idUsuario, backOffice);
        if (contexto.IdUsuario <= 0) return Unauthorized();

        var reporte = await _reporteService.ObtenerReporteUsuarioAsync(contexto.IdUsuario, contexto.CodEmisor);
        return Ok(Filtrar(reporte.Items, search));
    }

    [HttpGet("recibidos")]
    public async Task<IActionResult> GetRecibidos([FromQuery] int idUsuario, [FromQuery] string? search = null, [FromQuery] bool backOffice = false)
    {
        var contexto = await ResolverContextoAsync(idUsuario, backOffice);
        if (contexto.IdUsuario <= 0) return Unauthorized();

        var reporte = await _reporteService.ObtenerReporteUsuarioAsync(contexto.IdUsuario, contexto.CodEmisor);
        return Ok(Filtrar(reporte.Items.Where(x =>
            string.Equals(x.TerceroRol, "Proveedor", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(x.TipoDocumentoCodigo, ReporteComprobantesTipos.LiquidacionCompra, StringComparison.OrdinalIgnoreCase)), search));
    }

    private static IEnumerable<ReporteComprobanteItemDto> Filtrar(IEnumerable<ReporteComprobanteItemDto> items, string? search)
    {
        if (string.IsNullOrWhiteSpace(search)) return items;

        var term = search.Trim();
        return items.Where(x =>
            Contains(x.TipoDocumento, term) ||
            Contains(x.NumeroDocumento, term) ||
            Contains(x.TerceroNombre, term) ||
            Contains(x.TerceroIdentificacion, term) ||
            Contains(x.EstadoDocumento, term) ||
            Contains(x.ClaveAcceso, term) ||
            Contains(x.NumeroAutorizacion, term));
    }

    private static bool Contains(string? source, string term) =>
        !string.IsNullOrWhiteSpace(source) &&
        source.Contains(term, StringComparison.OrdinalIgnoreCase);
}
