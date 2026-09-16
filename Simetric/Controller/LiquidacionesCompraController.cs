using Microsoft.AspNetCore.Mvc;
using Simetric.DTOs;
using Simetric.Services;

namespace Simetric.Controllers;

[ApiController]
[Route("api/liquidaciones-compra")]
public class LiquidacionesCompraController : UsuarioApiControllerBase
{
    private readonly LiquidacionCompraService _service;
    private readonly ComprasXmlService _comprasXmlService;
    private readonly RetencionCorreoService _retencionCorreoService;

    public LiquidacionesCompraController(
        LiquidacionCompraService service,
        ComprasXmlService comprasXmlService,
        RetencionCorreoService retencionCorreoService)
    {
        _service = service;
        _comprasXmlService = comprasXmlService;
        _retencionCorreoService = retencionCorreoService;
    }

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

    [HttpPost("{codFactura:int}/retencion")]
    public async Task<IActionResult> CrearRetencion(
        int codFactura,
        [FromQuery] int idUsuario,
        [FromBody] LiquidacionRetencionRequestDto request)
    {
        idUsuario = ResolverIdUsuario(idUsuario);
        if (idUsuario <= 0) return Unauthorized();
        if (request.Retenciones.Count == 0) return BadRequest("Agrega al menos una retencion.");

        var detalle = await _service.GetLiquidacionDetalleUsuarioAsync(codFactura, idUsuario);
        if (detalle is null) return NotFound();
        if (!detalle.Preview.EstaAutorizada)
            return BadRequest("La liquidacion debe estar autorizada por el SRI antes de generar la retencion.");

        var liquidacion = detalle.Preview;
        var preview = new CompraXmlPreviewDto
        {
            Usuario = idUsuario,
            YaImportado = true,
            CodFacturaExistente = codFactura,
            ClaveAcceso = liquidacion.ClaveAcceso,
            NumeroAutorizacion = liquidacion.NumeroAutorizacion,
            Estab = liquidacion.Estab,
            PtoEmi = liquidacion.PtoEmi,
            Secuencial = liquidacion.Secuencial,
            RucProveedor = liquidacion.IdentificacionProveedor,
            RazonSocialProveedor = liquidacion.RazonSocialProveedor,
            Ambiente = liquidacion.Ambiente,
            RucEmisor = liquidacion.RucEmisor,
            DireccionMatriz = liquidacion.DireccionMatriz,
            DireccionEstablecimiento = liquidacion.DireccionEstablecimiento,
            DireccionProveedor = liquidacion.DireccionProveedor,
            TelefonoFijoProveedor = liquidacion.TelefonoFijoProveedor,
            TelefonoProveedor = liquidacion.TelefonoProveedor,
            EmailProveedor = liquidacion.EmailProveedor,
            IdentificacionComprador = liquidacion.RucEmisor,
            TipoIdentificacionComprador = liquidacion.TipoIdentificacionProveedor,
            TipoIdentificacionCompradorNombre = liquidacion.TipoIdentificacionProveedorNombre,
            ObligadoContabilidad = liquidacion.ObligadoContabilidad,
            FechaEmision = liquidacion.FechaEmision,
            FechaEmisionDocumentoSustento = liquidacion.FechaEmision,
            TotalSinImpuestos = liquidacion.TotalSinImpuestos,
            TotalDescuento = liquidacion.TotalDescuento,
            ImporteTotal = liquidacion.ImporteTotal,
            Moneda = liquidacion.Moneda,
            FormaPago = liquidacion.FormaPago,
            FormaPagoNombre = liquidacion.FormaPagoNombre,
            Subtotal12 = liquidacion.Subtotal15,
            Subtotal0 = liquidacion.Subtotal0,
            Subtotal5 = liquidacion.Subtotal5,
            Subtotal8 = liquidacion.Subtotal8,
            NoImp = liquidacion.NoImp,
            ExIva = liquidacion.ExIva,
            Iva = liquidacion.IvaTotal,
            Iva5 = liquidacion.Iva5,
            Iva8 = liquidacion.Iva8,
            CodEmisor = liquidacion.CodEmisor,
            CodProveedor = liquidacion.CodProveedor,
            Detalles = liquidacion.Detalles.Select(item => new CompraXmlDetalleDto
            {
                CodPrincipal = item.CodPrincipal,
                CodAuxiliar = item.CodAuxiliar,
                Descripcion = item.Descripcion,
                Cantidad = item.Cantidad,
                PrecioUnitario = item.PrecioUnitario,
                Descuento = item.Descuento,
                PrecioTotalSinImpuesto = item.PrecioTotalSinImpuesto,
                CodImp = item.CodigoPorcentaje,
                PorImp = item.CodigoPorcentaje,
                Tarifa = item.Tarifa,
                ValorIVA = item.ValorIva,
                ValorTotal = item.ValorTotal
            }).ToList(),
            Retenciones = request.Retenciones
        };

        await _comprasXmlService.GuardarCompraDesdePreviewAsync(preview);
        var codRetencion = await _retencionCorreoService.RegistrarDestinatariosRetencionPorCompraAsync(
            codFactura,
            request.CorreoPrincipal ?? liquidacion.EmailProveedor,
            request.Correos);

        return Ok(new
        {
            codLiquidacion = codFactura,
            codRetencion,
            numeroRetencion = preview.NumeroRetencionGenerado
        });
    }
}
