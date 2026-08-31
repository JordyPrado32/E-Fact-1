using System.Text.Json;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Simetric.Data;
using Simetric.DTOs;
using Simetric.Models;
using Simetric.Services;
using System.Globalization;

namespace Simetric.Controllers;

[ApiController]
[Route("api/documentos")]
public class CompraDocumentosController : UsuarioApiControllerBase
{
    private static readonly JsonSerializerOptions HistorialJsonOptions = new(JsonSerializerDefaults.Web);
    private readonly IDbContextFactory<AppDbContext> _dbFactory;
    private readonly PagoService _pagoService;
    private readonly IConfiguration _configuration;

    public CompraDocumentosController(
        IDbContextFactory<AppDbContext> dbFactory,
        PagoService pagoService,
        IConfiguration configuration)
    {
        _dbFactory = dbFactory;
        _pagoService = pagoService;
        _configuration = configuration;
    }

    [HttpGet("compra")]
    public async Task<IActionResult> GetCompra([FromQuery] int idUsuario)
    {
        idUsuario = ResolverIdUsuario(idUsuario);
        if (idUsuario <= 0) return Unauthorized();

        await using var context = await _dbFactory.CreateDbContextAsync();
        var usuario = await context.Usuarios
            .AsNoTracking()
            .Where(x => x.IdUsuario == idUsuario)
            .Select(x => new
            {
                x.IdUsuario,
                x.SaldoDocumentos,
                x.FechaUltimaRecargaDocumentos,
                x.HistorialComprasDocumentosJson
            })
            .FirstOrDefaultAsync();

        return usuario is null
            ? NotFound()
            : Ok(new
            {
                usuario.IdUsuario,
                saldoDocumentos = usuario.SaldoDocumentos,
                usuario.FechaUltimaRecargaDocumentos,
                historial = LeerHistorial(usuario.HistorialComprasDocumentosJson)
            });
    }

    [HttpPost("compra")]
    public async Task<IActionResult> CrearCompra([FromQuery] int idUsuario, [FromBody] CompraDocumentosMobileDto model)
    {
        idUsuario = ResolverIdUsuario(idUsuario);
        if (idUsuario <= 0) return Unauthorized();
        if (!model.EsIlimitado && model.Documentos <= 0) return BadRequest("Debe indicar la cantidad de documentos.");
        if (model.MontoTotal <= 0) return BadRequest("Debe indicar el monto total.");

        await using var context = await _dbFactory.CreateDbContextAsync();
        var usuario = await context.Usuarios.FirstOrDefaultAsync(x => x.IdUsuario == idUsuario);
        if (usuario is null) return NotFound();

        var historial = LeerHistorial(usuario.HistorialComprasDocumentosJson);
        var compra = new CompraDocumentosHistorialItem
        {
            Documentos = model.EsIlimitado ? 0 : model.Documentos,
            MontoTotal = model.MontoTotal,
            Estado = "Pendiente",
            Descripcion = string.IsNullOrWhiteSpace(model.Descripcion)
                ? model.EsIlimitado ? "Plan ilimitado E-FACT" : $"Recarga de {model.Documentos} documentos E-FACT"
                : model.Descripcion.Trim(),
            EmailDestino = string.IsNullOrWhiteSpace(model.EmailDestino) ? usuario.Email : model.EmailDestino.Trim(),
            EsIlimitado = model.EsIlimitado,
            EsPermanente = model.EsPermanente
        };

        historial.Insert(0, compra);
        usuario.HistorialComprasDocumentosJson = JsonSerializer.Serialize(historial, HistorialJsonOptions);
        await context.SaveChangesAsync();

        return Ok(compra);
    }

    [HttpPost("compra/pago")]
    public async Task<IActionResult> CrearPagoCompra([FromQuery] int idUsuario, [FromBody] CompraDocumentosMobileDto model)
    {
        idUsuario = ResolverIdUsuario(idUsuario);
        if (idUsuario <= 0) return Unauthorized();
        if (!model.EsIlimitado && model.Documentos < EFactDocumentPricing.DocumentosMinimosPersonalizados)
            return BadRequest($"La recarga personalizada requiere al menos {EFactDocumentPricing.DocumentosMinimosPersonalizados} documentos.");
        if (model.MontoTotal < EFactDocumentPricing.MontoMinimoPersonalizado)
            return BadRequest($"El monto minimo de recarga es ${EFactDocumentPricing.MontoMinimoPersonalizado:0.00}.");
        if (model.MontoTotal > EFactDocumentPricing.MontoMaximoPersonalizado)
            return BadRequest($"El monto maximo de recarga es ${EFactDocumentPricing.MontoMaximoPersonalizado:0.00}.");

        await using var context = await _dbFactory.CreateDbContextAsync();
        var usuario = await context.Usuarios.FirstOrDefaultAsync(x => x.IdUsuario == idUsuario);
        if (usuario is null) return NotFound();
        if (string.IsNullOrWhiteSpace(usuario.Identificacion))
            return BadRequest("Tu perfil no tiene identificacion configurada para enviar a Pagomedios.");
        if (string.IsNullOrWhiteSpace(usuario.Email))
            return BadRequest("Tu perfil no tiene correo configurado para enviar a Pagomedios.");

        var compraId = Guid.NewGuid().ToString("N");
        var total = decimal.Round(model.MontoTotal, 2, MidpointRounding.AwayFromZero);
        var customValue = $"recarga-documentos|purchase:{compraId}|user:{idUsuario}|docs:{(model.EsIlimitado ? 0 : model.Documentos)}|total:{total.ToString("0.00", CultureInfo.InvariantCulture)}{(model.EsIlimitado ? "|plan:ilimitado-anual" : string.Empty)}";
        var historial = LeerHistorial(usuario.HistorialComprasDocumentosJson);
        historial.Insert(0, new CompraDocumentosHistorialItem
        {
            Id = compraId,
            Fecha = DateTime.Now,
            Documentos = model.EsIlimitado ? 0 : model.Documentos,
            MontoTotal = total,
            Estado = "Pendiente",
            Descripcion = model.EsIlimitado ? "Plan de documentos ilimitados por 1 año" : $"Recarga de {model.Documentos} documentos E-FACT",
            EmailDestino = usuario.Email.Trim().ToLowerInvariant(),
            CustomValue = customValue,
            EsIlimitado = model.EsIlimitado,
            EsPermanente = model.EsPermanente
        });
        usuario.HistorialComprasDocumentosJson = JsonSerializer.Serialize(historial, HistorialJsonOptions);
        await context.SaveChangesAsync();

        var baseUrl = (_configuration["AppBaseUrl"] ?? $"{Request.Scheme}://{Request.Host}").TrimEnd('/');
        var notifyUrl = $"{baseUrl}/api/pagomedios/notificacion-compra-documentos?uid={idUsuario}&purchase={Uri.EscapeDataString(compraId)}";
        var subtotal = EFactDocumentPricing.CalcularSubtotalDesdeTotal(total);
        var request = new PagomediosRequest
        {
            Integration = true,
            Third = new ThirdParty
            {
                Document = new string(usuario.Identificacion.Where(char.IsDigit).ToArray()),
                DocumentType = usuario.Identificacion.Count(char.IsDigit) == 13 ? "04" : usuario.Identificacion.Count(char.IsDigit) == 10 ? "05" : "06",
                Name = string.IsNullOrWhiteSpace(usuario.NombreEmpresa) ? usuario.NombreCompleto : usuario.NombreEmpresa.Trim(),
                Email = usuario.Email.Trim().ToLowerInvariant(),
                Phones = string.IsNullOrWhiteSpace(usuario.Celular) ? "0999999999" : new string(usuario.Celular.Where(char.IsDigit).ToArray()),
                Address = string.IsNullOrWhiteSpace(usuario.DireccionEmpresa) ? "Quito" : usuario.DireccionEmpresa.Trim(),
                Type = string.IsNullOrWhiteSpace(usuario.NombreEmpresa) ? "Individual" : "Company"
            },
            GenerateInvoice = 0,
            Description = model.EsIlimitado ? "Plan E-FACT de documentos ilimitados por 1 año" : $"Recarga de {model.Documentos} documentos E-FACT",
            Amount = total,
            AmountWithTax = subtotal,
            AmountWithoutTax = 0m,
            TaxValue = EFactDocumentPricing.CalcularIvaDesdeTotal(total),
            NotifyUrl = notifyUrl,
            CustomValue = customValue,
            HasCards = 0,
            HasDeUna = 1,
            HasPaypal = 0,
            HasSafetypay = false
        };

        var payment = await _pagoService.SendJsonAsync(HttpMethod.Post, "/payment-requests", request);
        if (!payment.IsSuccess || string.IsNullOrWhiteSpace(payment.PaymentUrl))
        {
            historial.RemoveAll(item => string.Equals(item.Id, compraId, StringComparison.OrdinalIgnoreCase) && !item.SaldoAplicado);
            usuario.HistorialComprasDocumentosJson = JsonSerializer.Serialize(historial, HistorialJsonOptions);
            await context.SaveChangesAsync();
            return BadRequest(new { message = payment.ErrorMessage ?? "Pagomedios no pudo generar el checkout.", purchaseId = compraId });
        }

        return Ok(new { paymentUrl = payment.PaymentUrl, purchaseId = compraId, status = "Pendiente" });
    }

    [HttpGet("recargas")]
    public async Task<IActionResult> GetRecargas([FromQuery] int idUsuario)
    {
        idUsuario = ResolverIdUsuario(idUsuario);
        if (idUsuario <= 0) return Unauthorized();

        await using var context = await _dbFactory.CreateDbContextAsync();
        var historialJson = await context.Usuarios
            .AsNoTracking()
            .Where(x => x.IdUsuario == idUsuario)
            .Select(x => x.HistorialComprasDocumentosJson)
            .FirstOrDefaultAsync();

        return Ok(LeerHistorial(historialJson));
    }

    [HttpGet("paquetes")]
    public IActionResult GetPaquetes() => Ok(new[]
    {
        new { id = "docs-10", descripcion = "10 documentos", documentos = 10, esIlimitado = false },
        new { id = "docs-50", descripcion = "50 documentos", documentos = 50, esIlimitado = false },
        new { id = "docs-100", descripcion = "100 documentos", documentos = 100, esIlimitado = false },
        new { id = "ilimitado", descripcion = "Ilimitados durante 1 ano", documentos = 0, esIlimitado = true }
    });

    private static List<CompraDocumentosHistorialItem> LeerHistorial(string? historialJson)
    {
        if (string.IsNullOrWhiteSpace(historialJson)) return new List<CompraDocumentosHistorialItem>();

        try
        {
            return JsonSerializer.Deserialize<List<CompraDocumentosHistorialItem>>(historialJson, HistorialJsonOptions)
                ?? new List<CompraDocumentosHistorialItem>();
        }
        catch (JsonException)
        {
            return new List<CompraDocumentosHistorialItem>();
        }
    }
}

public sealed class CompraDocumentosMobileDto
{
    public int Documentos { get; set; }
    public decimal MontoTotal { get; set; }
    public string? Descripcion { get; set; }
    public string? EmailDestino { get; set; }
    public bool EsIlimitado { get; set; }
    public bool EsPermanente { get; set; }
}
