using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Simetric.Data;
using Simetric.Models;
using Simetric.Services;
using System.Globalization;

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

    [HttpGet("siguiente-secuencial")]
    public async Task<IActionResult> GetSiguienteSecuencial(
        [FromQuery] int? idUsuario,
        [FromQuery] string? documento,
        [FromQuery] string? serie,
        [FromQuery] int? codEmisor = null)
    {
        if (idUsuario is null or <= 0)
            return BadRequest("Id de usuario requerido.");

        var documentKey = NormalizarDocumento(documento);
        if (string.IsNullOrWhiteSpace(documentKey))
            return BadRequest("Tipo de documento requerido.");

        var serieRaw = ToSerieRaw(FormatearSerie(serie));
        var emisor = codEmisor is > 0
            ? codEmisor
            : (await _facturacionService.GetEmisoresActivosAsync(idUsuario.Value)).FirstOrDefault()?.Codigo;
        var state = await GetSequenceStateAsync(idUsuario.Value, documentKey, serieRaw, emisor);
        var proximo = await ResolveNextDocumentSequenceAsync(
            idUsuario.Value,
            documentKey,
            serieRaw,
            emisor,
            state);

        return Ok(new
        {
            documento = documentKey,
            serie = FormatearSerie(serieRaw),
            inicializada = state.Initialized || !string.IsNullOrWhiteSpace(proximo),
            secuenciaAnterior = state.PreviousSequence,
            proximo
        });
    }

    [HttpPost("secuencia-inicial")]
    public async Task<IActionResult> GuardarSecuenciaInicial([FromQuery] int? idUsuario, [FromBody] CajaSecuenciaInicialDto model)
    {
        if (idUsuario is null or <= 0)
            return BadRequest("Id de usuario requerido.");

        var documentKey = NormalizarDocumento(model.Documento);
        if (string.IsNullOrWhiteSpace(documentKey))
            return BadRequest("Tipo de documento requerido.");

        var serieRaw = ToSerieRaw(FormatearSerie(model.Serie));
        var previousSequence = string.Empty;
        if (model.HabiaGenerado)
        {
            if (!_initialSequencePromptService.TryNormalizeSequence(model.SecuenciaAnterior, out previousSequence))
                return BadRequest("Ingresa el secuencial anterior con hasta 9 digitos.");
        }

        var emisor = model.CodEmisor is > 0
            ? model.CodEmisor
            : (await _facturacionService.GetEmisoresActivosAsync(idUsuario.Value)).FirstOrDefault()?.Codigo;
        var state = new InitialSequencePromptState
        {
            Initialized = true,
            HadPreviousDocuments = model.HabiaGenerado,
            PreviousSequence = previousSequence,
            ConfiguredAt = DateTimeOffset.UtcNow
        };

        await _initialSequencePromptService.SaveStateAsync(idUsuario.Value, documentKey, serieRaw, state, emisor);
        var proximo = _initialSequencePromptService.ResolveNextSequence(null, state);

        return Ok(new
        {
            documento = documentKey,
            serie = FormatearSerie(serieRaw),
            inicializada = true,
            secuenciaAnterior = previousSequence,
            proximo
        });
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
        var secFactura = await ResolveNextDocumentSequenceAsync(idUsuario, "factura", ToSerieRaw(serieFactura), codEmisor, factura);
        var secGuia = await ResolveNextDocumentSequenceAsync(idUsuario, "guia-remision", ToSerieRaw(serieGuia), codEmisor, guia);
        var secNotaCredito = await ResolveNextDocumentSequenceAsync(idUsuario, "nota-credito", ToSerieRaw(serieNotasCred), codEmisor, notaCredito);
        var secNotaDebito = await ResolveNextDocumentSequenceAsync(idUsuario, "nota-debito", ToSerieRaw(serieNotasDeb), codEmisor, notaDebito);
        var secLiquidacion = await ResolveNextDocumentSequenceAsync(idUsuario, "liquidacion-compra", ToSerieRaw(serieLiquidacion), codEmisor, liquidacion);
        var secRetencion = _initialSequencePromptService.ResolveNextSequence(null, retencion) ?? string.Empty;

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
            secFactura,
            secGuia,
            secNotaCredito,
            secNotaDebito,
            secLiquidacion,
            secRetencion,
            secuenciaFacturaInicializada = factura.Initialized || !string.IsNullOrWhiteSpace(secFactura),
            secuenciaGuiaInicializada = guia.Initialized || !string.IsNullOrWhiteSpace(secGuia),
            secuenciaNotaCreditoInicializada = notaCredito.Initialized || !string.IsNullOrWhiteSpace(secNotaCredito),
            secuenciaNotaDebitoInicializada = notaDebito.Initialized || !string.IsNullOrWhiteSpace(secNotaDebito),
            secuenciaLiquidacionInicializada = liquidacion.Initialized || !string.IsNullOrWhiteSpace(secLiquidacion),
            secuenciaRetencionInicializada = retencion.Initialized || !string.IsNullOrWhiteSpace(secRetencion),
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

    private async Task<string> ResolveNextDocumentSequenceAsync(
        int idUsuario,
        string documentKey,
        string serieRaw,
        int? codEmisor,
        InitialSequencePromptState state)
    {
        var automatico = await GetNextExistingDocumentSequenceAsync(idUsuario, documentKey, serieRaw, codEmisor);
        var siguiente = _initialSequencePromptService.ResolveNextSequence(automatico, state);
        return string.IsNullOrWhiteSpace(siguiente) ? string.Empty : siguiente;
    }

    private async Task<string?> GetNextExistingDocumentSequenceAsync(int idUsuario, string documentKey, string serieRaw, int? codEmisor)
    {
        var usuariosCuenta = await GetUsuariosCuentaIdsAsync(idUsuario);
        var serieVisual = FormatearSerie(serieRaw);
        List<string?> secuenciales;

        switch (documentKey)
        {
            case "factura":
                secuenciales = await _context.Facturas
                    .AsNoTracking()
                    .Where(x =>
                        x.Idusuario.HasValue &&
                        usuariosCuenta.Contains(x.Idusuario.Value) &&
                        x.Serie != null &&
                        x.Serie.Replace("-", "") == serieRaw &&
                        (!codEmisor.HasValue || x.Codemisor == codEmisor.Value))
                    .Select(x => x.Numfactura)
                    .ToListAsync();
                break;

            case "nota-credito":
                secuenciales = await _context.NotaCreditos
                    .AsNoTracking()
                    .Where(x =>
                        x.Usuario.HasValue &&
                        usuariosCuenta.Contains(x.Usuario.Value) &&
                        x.Serie != null &&
                        x.Serie.Replace("-", "") == serieRaw &&
                        (!codEmisor.HasValue || x.CodEmisor == codEmisor.Value))
                    .Select(x => x.NumNotaCredito)
                    .ToListAsync();
                break;

            case "nota-debito":
                secuenciales = await _context.NotaDebitos
                    .AsNoTracking()
                    .Where(x =>
                        x.Usuario.HasValue &&
                        usuariosCuenta.Contains(x.Usuario.Value) &&
                        x.Serie != null &&
                        x.Serie.Replace("-", "") == serieRaw &&
                        (!codEmisor.HasValue || x.CodEmisor == codEmisor.Value))
                    .Select(x => x.NumNotaDebito)
                    .ToListAsync();
                break;

            case "guia-remision":
                secuenciales = await _context.GuiasRemision
                    .AsNoTracking()
                    .Where(x =>
                        x.IdUsuario.HasValue &&
                        usuariosCuenta.Contains(x.IdUsuario.Value) &&
                        x.Serie != null &&
                        x.Serie.Replace("-", "") == serieRaw)
                    .Select(x => x.NumGuiaRemision)
                    .ToListAsync();
                break;

            case "liquidacion-compra":
                secuenciales = await _context.ComprasFacturas
                    .AsNoTracking()
                    .Where(x =>
                        x.Estado == true &&
                        x.CodDocumento == "03" &&
                        x.Usuario.HasValue &&
                        usuariosCuenta.Contains(x.Usuario.Value) &&
                        x.Serie == serieVisual &&
                        (!codEmisor.HasValue || x.CodEmisor == codEmisor.Value))
                    .Select(x => x.NumFactura)
                    .ToListAsync();
                break;

            default:
                return null;
        }

        var maximo = 0;
        foreach (var valor in secuenciales)
        {
            var digitos = LimpiarDigitos(valor);
            if (int.TryParse(digitos, out var numero) && numero > maximo)
                maximo = numero;
        }

        return maximo > 0
            ? (maximo + 1).ToString("D9", CultureInfo.InvariantCulture)
            : null;
    }

    private async Task<List<int>> GetUsuariosCuentaIdsAsync(int idUsuario)
    {
        var usuario = await _context.Usuarios
            .AsNoTracking()
            .Where(u => u.IdUsuario == idUsuario)
            .Select(u => new { u.IdUsuario, u.idJefe, u.estadoAsociado })
            .FirstOrDefaultAsync();

        var titularId = usuario?.estadoAsociado == true && usuario.idJefe is > 0
            ? usuario.idJefe.Value
            : idUsuario;

        var usuarios = await _context.Usuarios
            .AsNoTracking()
            .Where(u => u.IdUsuario == titularId || (u.idJefe == titularId && u.estadoAsociado == true))
            .Select(u => u.IdUsuario)
            .ToListAsync();

        if (!usuarios.Contains(idUsuario))
            usuarios.Add(idUsuario);

        return usuarios;
    }

    private static string FormatearSerie(string? serie) => $"{ExtraerEstablecimiento(serie)}-{ExtraerPuntoEmision(serie)}";
    private static string ToSerieRaw(string serie) => serie.Replace("-", string.Empty);
    private static string NormalizarDocumento(string? documento)
    {
        var key = (documento ?? string.Empty).Trim().ToLowerInvariant();
        return key switch
        {
            "factura" or "fac" => "factura",
            "guia" or "guia-remision" or "guiaremision" or "gui" => "guia-remision",
            "nota-credito" or "notacredito" or "notaCredito" or "nc" => "nota-credito",
            "nota-debito" or "notadebito" or "notaDebito" or "nd" => "nota-debito",
            "liquidacion" or "liquidacion-compra" or "liquidacioncompra" or "liq" => "liquidacion-compra",
            "retencion" or "ret" => "retencion",
            _ => string.Empty
        };
    }
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

    public sealed class CajaSecuenciaInicialDto
    {
        public string? Documento { get; set; }
        public string? Serie { get; set; }
        public int? CodEmisor { get; set; }
        public bool HabiaGenerado { get; set; }
        public string? SecuenciaAnterior { get; set; }
    }
}
