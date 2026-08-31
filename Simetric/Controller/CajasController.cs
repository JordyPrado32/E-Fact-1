using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Simetric.Data;
using Simetric.Models;
using Simetric.Services;

namespace Simetric.Controllers;

[ApiController]
[Route("api/cajas")]
public class CajasController : ControllerBase
{
    private readonly AppDbContext _context;
    private readonly ConfiguracionService _configuracionService;
    private readonly FacturacionService _facturacionService;
    private readonly InitialSequencePromptService _initialSequencePromptService;

    public CajasController(
        AppDbContext context,
        ConfiguracionService configuracionService,
        FacturacionService facturacionService,
        InitialSequencePromptService initialSequencePromptService)
    {
        _context = context;
        _configuracionService = configuracionService;
        _facturacionService = facturacionService;
        _initialSequencePromptService = initialSequencePromptService;
    }

    [HttpGet]
    public async Task<IActionResult> GetAll([FromQuery] int? idUsuario)
    {
        if (idUsuario is null or <= 0)
            return BadRequest("Id de usuario requerido.");

        var cajas = await _configuracionService.GetCajasCuentaActivasAsync(idUsuario.Value);
        var emisor = (await _facturacionService.GetEmisoresActivosAsync(idUsuario.Value)).FirstOrDefault();

        return Ok(new
        {
            emisor = emisor is null ? null : new
            {
                codigo = emisor.Codigo,
                razonSocial = emisor.RazonSocial,
                ruc = emisor.Ruc,
                nomComercial = emisor.NomComercial,
                codEstablecimiento = emisor.CodEstablecimiento,
                dirEstablecimiento = emisor.DirEstablecimiento,
                direccionMatriz = emisor.DireccionMatriz,
                idEmpresa = emisor.IdEmpresa,
                idSucursal = emisor.IdSucursal
            },
            cajas = await Task.WhenAll(cajas.Select(caja => ToDtoAsync(idUsuario.Value, caja, emisor?.Codigo)))
        });
    }

    [HttpPost]
    public async Task<IActionResult> Create([FromQuery] int? idUsuario, [FromBody] CajaUpsertDto model)
    {
        if (idUsuario is null or <= 0)
            return BadRequest("Id de usuario requerido.");

        var cajas = await _configuracionService.GetCajasCuentaActivasAsync(idUsuario.Value);
        var emisor = (await _facturacionService.GetEmisoresActivosAsync(idUsuario.Value)).FirstOrDefault();
        var establecimiento = NormalizarCodigo(emisor?.CodEstablecimiento) ?? NormalizarCodigo(model.Establecimiento);
        var punto = NormalizarCodigo(model.PuntoEmision);
        if (establecimiento is null)
            return BadRequest("Primero configura el establecimiento del emisor.");
        if (punto is null)
            return BadRequest("El punto de emisión debe contener tres dígitos y no puede ser 000.");

        var serie = $"{establecimiento}-{punto}";
        if (cajas.Any(c => FormatearSerie(c.SerieFactura) == serie))
            return BadRequest($"La serie {serie} ya está configurada.");

        var principal = cajas.OrderBy(c => c.NumCaja).ThenBy(c => c.Sec).FirstOrDefault();
        var siguienteNumero = cajas.Select(c => c.NumCaja ?? 0).DefaultIfEmpty(0).Max() + 1;
        var caja = new Caja
        {
            IdUsuario = idUsuario.Value,
            NumCaja = siguienteNumero,
            idJefe = idUsuario.Value,
            IdEmpresa = principal?.IdEmpresa is > 0 ? principal.IdEmpresa : emisor?.IdEmpresa is > 0 ? emisor.IdEmpresa : 1,
            IdSucursal = principal?.IdSucursal is > 0 ? principal.IdSucursal : emisor?.IdSucursal is > 0 ? emisor.IdSucursal : 1,
            SerieFactura = serie,
            SerieNotasCred = serie,
            SerieGuia = serie,
            SerieCompras = serie,
            SerieDebitos = serie,
            Estado = true
        };

        if (!await _configuracionService.SaveCajaAsync(caja, idUsuario.Value))
            return BadRequest("No se pudo guardar el punto de emisión.");

        return Ok(await ToDtoAsync(idUsuario.Value, caja, emisor?.Codigo));
    }

    [HttpPut("{sec:int}")]
    public async Task<IActionResult> Update(int sec, [FromQuery] int? idUsuario, [FromBody] CajaUpsertDto model)
    {
        if (idUsuario is null or <= 0)
            return BadRequest("Id de usuario requerido.");

        var cajas = await _configuracionService.GetCajasCuentaActivasAsync(idUsuario.Value);
        var caja = cajas.FirstOrDefault(c => c.Sec == sec);
        if (caja is null)
            return NotFound();

        var establecimiento = ExtraerEstablecimiento(caja.SerieFactura);
        var punto = NormalizarCodigo(model.PuntoEmision);
        if (punto is null)
            return BadRequest("El punto de emisión debe contener tres dígitos y no puede ser 000.");

        var serie = $"{establecimiento}-{punto}";
        if (cajas.Any(c => c.Sec != sec && FormatearSerie(c.SerieFactura) == serie))
            return BadRequest($"La serie {serie} ya está configurada.");

        caja.SerieFactura = serie;
        caja.SerieNotasCred = serie;
        caja.SerieGuia = serie;
        caja.SerieCompras = serie;
        caja.SerieDebitos = serie;
        caja.Estado = true;

        if (!await _configuracionService.SaveCajaAsync(caja, idUsuario.Value))
            return BadRequest("No se pudo actualizar el punto de emisión.");

        var emisor = (await _facturacionService.GetEmisoresActivosAsync(idUsuario.Value)).FirstOrDefault();
        return Ok(await ToDtoAsync(idUsuario.Value, caja, emisor?.Codigo));
    }

    [HttpPut("{sec:int}/principal")]
    public async Task<IActionResult> MarcarPrincipal(int sec, [FromQuery] int? idUsuario)
    {
        if (idUsuario is null or <= 0)
            return BadRequest("Id de usuario requerido.");

        if (!await _configuracionService.MarcarCajaPrincipalAsync(sec, idUsuario.Value))
            return BadRequest("No se pudo marcar el punto como principal.");

        return Ok();
    }

    [HttpDelete("{sec:int}")]
    public async Task<IActionResult> Delete(int sec, [FromQuery] int? idUsuario)
    {
        if (idUsuario is null or <= 0)
            return BadRequest("Id de usuario requerido.");

        var cajas = await _configuracionService.GetCajasCuentaActivasAsync(idUsuario.Value);
        var caja = cajas.FirstOrDefault(c => c.Sec == sec);
        if (caja is null)
            return NotFound();
        if (caja.NumCaja == 1 || cajas.Count <= 1)
            return BadRequest("No se puede eliminar el punto principal.");

        if (!await _configuracionService.DeleteCajaAsync(sec, idUsuario.Value))
            return BadRequest("No se pudo eliminar el punto de emisión.");

        return Ok();
    }

    private async Task<object> ToDtoAsync(int idUsuario, Caja caja, int? codEmisor)
    {
        var serieFactura = FormatearSerie(caja.SerieFactura);
        var serieGuia = FormatearSerie(caja.SerieGuia);
        var serieNotasCred = FormatearSerie(caja.SerieNotasCred);
        var serieNotasDeb = FormatearSerie(caja.SerieDebitos);
        var serieLiquidacion = FormatearSerie(caja.SerieCompras);
        var factura = await GetSequenceStateAsync(idUsuario, "factura", ToSerieRaw(serieFactura), codEmisor);
        var guia = await GetSequenceStateAsync(idUsuario, "guia-remision", ToSerieRaw(serieGuia), codEmisor);
        var notaCredito = await GetSequenceStateAsync(idUsuario, "nota-credito", ToSerieRaw(serieNotasCred), codEmisor);
        var notaDebito = await GetSequenceStateAsync(idUsuario, "nota-debito", ToSerieRaw(serieNotasDeb), codEmisor);
        var liquidacion = await GetSequenceStateAsync(idUsuario, "liquidacion-compra", ToSerieRaw(serieLiquidacion), codEmisor);
        var retencion = await GetSequenceStateAsync(idUsuario, "retencion", ToSerieRaw(serieLiquidacion), codEmisor);

        return new
        {
            sec = caja.Sec,
            numCaja = caja.NumCaja,
            idUsuario = caja.IdUsuario,
            idEmpresa = caja.IdEmpresa,
            idSucursal = caja.IdSucursal,
            serieFactura,
            serieGuia,
            serieNotasCred,
            serieNotasDeb,
            serieLiquidacion,
            serieLiquidacionCompra = serieLiquidacion,
            serieRetencion = serieLiquidacion,
            secFactura = factura.PreviousSequence,
            secGuia = guia.PreviousSequence,
            secNotaCredito = notaCredito.PreviousSequence,
            secNotaDebito = notaDebito.PreviousSequence,
            secLiquidacion = liquidacion.PreviousSequence,
            secRetencion = retencion.PreviousSequence,
            secuenciaFacturaInicializada = factura.Initialized,
            secuenciaGuiaInicializada = guia.Initialized,
            secuenciaNotaCreditoInicializada = notaCredito.Initialized,
            secuenciaNotaDebitoInicializada = notaDebito.Initialized,
            secuenciaLiquidacionInicializada = liquidacion.Initialized,
            secuenciaRetencionInicializada = retencion.Initialized,
            estado = caja.Estado,
            esPrincipal = caja.NumCaja == 1,
            establecimiento = ExtraerEstablecimiento(caja.SerieFactura),
            puntoEmision = ExtraerPuntoEmision(caja.SerieFactura)
        };
    }

    private async Task<InitialSequencePromptState> GetSequenceStateAsync(int idUsuario, string documentKey, string serieRaw, int? codEmisor)
    {
        try
        {
            return await _initialSequencePromptService.GetStateAsync(idUsuario, documentKey, serieRaw, codEmisor);
        }
        catch
        {
            return new InitialSequencePromptState();
        }
    }

    private static string FormatearSerie(string? serie) => $"{ExtraerEstablecimiento(serie)}-{ExtraerPuntoEmision(serie)}";
    private static string ToSerieRaw(string serie) => serie.Replace("-", string.Empty);
    private static string ExtraerEstablecimiento(string? serie)
    {
        var digitos = LimpiarDigitos(serie);
        return digitos.Length >= 3 ? digitos[..3] : "001";
    }
    private static string ExtraerPuntoEmision(string? serie)
    {
        var digitos = LimpiarDigitos(serie);
        return digitos.Length >= 6 ? digitos.Substring(3, 3) : "001";
    }
    private static string? NormalizarCodigo(string? value)
    {
        var digitos = LimpiarDigitos(value);
        if (digitos.Length is < 1 or > 3) return null;
        var codigo = digitos.PadLeft(3, '0');
        return codigo == "000" ? null : codigo;
    }
    private static string LimpiarDigitos(string? value) => new((value ?? string.Empty).Where(char.IsDigit).ToArray());

    public sealed class CajaUpsertDto
    {
        public string? Establecimiento { get; set; }
        public string? PuntoEmision { get; set; }
    }
}
