using Microsoft.AspNetCore.Mvc;
using Simetric.DTOs;
using Simetric.Services;

namespace Simetric.Controllers;

[ApiController]
[Route("api/compras")]
public class ComprasController : UsuarioApiControllerBase
{
    private readonly ReporteComprobantesService _reporteService;
    private readonly LiquidacionCompraService _liquidacionService;

    public ComprasController(
        ReporteComprobantesService reporteService,
        LiquidacionCompraService liquidacionService)
    {
        _reporteService = reporteService;
        _liquidacionService = liquidacionService;
    }

    [HttpGet]
    [HttpGet("documentos")]
    public async Task<IActionResult> GetDocumentos([FromQuery] int idUsuario, [FromQuery] string? search = null)
    {
        idUsuario = ResolverIdUsuario(idUsuario);
        if (idUsuario <= 0) return Unauthorized();

        var reporte = await _reporteService.ObtenerReporteUsuarioAsync(idUsuario);
        var compras = reporte.Items.Where(EsDocumentoCompra);

        return Ok(Filtrar(compras, search));
    }

    [HttpGet("liquidaciones")]
    public async Task<IActionResult> GetLiquidaciones([FromQuery] int idUsuario)
    {
        idUsuario = ResolverIdUsuario(idUsuario);
        return idUsuario <= 0
            ? Unauthorized()
            : Ok(await _liquidacionService.ListarLiquidacionesUsuarioAsync(idUsuario));
    }

    [HttpGet("xml")]
    public async Task<IActionResult> GetXml([FromQuery] int idUsuario, [FromQuery] string? search = null)
    {
        idUsuario = ResolverIdUsuario(idUsuario);
        if (idUsuario <= 0) return Unauthorized();

        var reporte = await _reporteService.ObtenerReporteUsuarioAsync(idUsuario);
        var comprasConXml = reporte.Items.Where(x => EsDocumentoCompra(x) && !string.IsNullOrWhiteSpace(x.XmlUrl));

        return Ok(Filtrar(comprasConXml, search));
    }

    private static bool EsDocumentoCompra(ReporteComprobanteItemDto item) =>
        string.Equals(item.TerceroRol, "Proveedor", StringComparison.OrdinalIgnoreCase) ||
        string.Equals(item.TipoDocumentoCodigo, ReporteComprobantesTipos.LiquidacionCompra, StringComparison.OrdinalIgnoreCase);

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
