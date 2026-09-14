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
    private static readonly HashSet<string> BancosTransferencia = new(StringComparer.OrdinalIgnoreCase)
    {
        "Banco Pichincha", "Banco Guayaquil", "Banco Internacional", "Banco Pacifico",
        "Banco Produbanco", "Banco Bolivariano", "Cooperativa JEP", "Otra institucion"
    };
    private readonly IDbContextFactory<AppDbContext> _dbFactory;
    private readonly PagoService _pagoService;
    private readonly IConfiguration _configuration;
    private readonly IEmailService _emailService;

    public CompraDocumentosController(
        IDbContextFactory<AppDbContext> dbFactory,
        PagoService pagoService,
        IConfiguration configuration,
        IEmailService emailService)
    {
        _dbFactory = dbFactory;
        _pagoService = pagoService;
        _configuration = configuration;
        _emailService = emailService;
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
            FormaPago = "DeUna",
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

    [HttpPost("compra/transferencia")]
    public async Task<IActionResult> RegistrarTransferenciaCompra([FromQuery] int idUsuario, [FromBody] CompraDocumentosTransferenciaDto model)
    {
        idUsuario = ResolverIdUsuario(idUsuario);
        if (idUsuario <= 0) return Unauthorized();
        if (!model.EsIlimitado && model.Documentos < EFactDocumentPricing.DocumentosMinimosPersonalizados)
            return BadRequest($"La recarga personalizada requiere al menos {EFactDocumentPricing.DocumentosMinimosPersonalizados} documentos.");
        if (model.MontoTotal < EFactDocumentPricing.MontoMinimoPersonalizado || model.MontoTotal > EFactDocumentPricing.MontoMaximoPersonalizado)
            return BadRequest("El monto de la recarga no es valido.");
        if (string.IsNullOrWhiteSpace(model.Banco) || string.IsNullOrWhiteSpace(model.Titular) || string.IsNullOrWhiteSpace(model.CuentaOrigen) || string.IsNullOrWhiteSpace(model.NumeroComprobante))
            return BadRequest("Completa los datos de la transferencia.");
        if (!BancosTransferencia.Contains(model.Banco.Trim())) return BadRequest("Selecciona un banco valido.");
        if (!model.CuentaOrigen.All(char.IsDigit)) return BadRequest("El numero de cuenta origen solo debe contener numeros.");
        if (model.NumeroComprobante.Length > 50 || !model.NumeroComprobante.All(char.IsLetterOrDigit))
            return BadRequest("El numero de comprobante debe tener maximo 50 caracteres alfanumericos.");

        var partesTitular = model.Titular.Trim().Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (partesTitular.Length < 2 || partesTitular.Count(parte => parte.Length >= 3 && parte.All(char.IsLetter)) < 2)
            return BadRequest("El titular de la cuenta debe incluir al menos un nombre y un apellido.");

        byte[] comprobante;
        try
        {
            var base64 = (model.ComprobanteBase64 ?? string.Empty).Trim();
            var separator = base64.IndexOf(',');
            if (separator >= 0) base64 = base64[(separator + 1)..];
            comprobante = Convert.FromBase64String(base64);
        }
        catch (FormatException)
        {
            return BadRequest("El comprobante debe ser una imagen JPG o PNG valida.");
        }

        if (comprobante.Length == 0 || comprobante.Length > 5 * 1024 * 1024 || !EsImagenComprobante(comprobante))
            return BadRequest("El comprobante debe ser una imagen de maximo 5MB.");

        await using var context = await _dbFactory.CreateDbContextAsync();
        var usuario = await context.Usuarios.FirstOrDefaultAsync(x => x.IdUsuario == idUsuario);
        if (usuario is null) return NotFound();
        if (string.IsNullOrWhiteSpace(usuario.Email)) return BadRequest("Tu perfil no tiene correo configurado.");

        var total = decimal.Round(model.MontoTotal, 2, MidpointRounding.AwayFromZero);
        var compraId = Guid.NewGuid().ToString("N");
        var compra = new CompraDocumentosHistorialItem
        {
            Id = compraId,
            Fecha = DateTime.Now,
            Documentos = model.EsIlimitado ? 0 : model.Documentos,
            MontoTotal = total,
            Estado = "Pendiente",
            Descripcion = model.EsIlimitado ? "Plan de documentos ilimitados por 1 año" : $"Recarga de {model.Documentos} documentos E-FACT",
            CustomValue = $"recarga-documentos|purchase:{compraId}|user:{idUsuario}|docs:{(model.EsIlimitado ? 0 : model.Documentos)}|total:{total.ToString("0.00", CultureInfo.InvariantCulture)}",
            FormaPago = "Transferencia",
            EmailDestino = usuario.Email.Trim().ToLowerInvariant(),
            EsIlimitado = model.EsIlimitado,
            EsPermanente = model.EsPermanente
        };
        var historial = LeerHistorial(usuario.HistorialComprasDocumentosJson);
        historial.Insert(0, compra);
        usuario.HistorialComprasDocumentosJson = JsonSerializer.Serialize(historial, HistorialJsonOptions);

        var nombre = string.IsNullOrWhiteSpace(usuario.NombreEmpresa)
            ? $"{usuario.Nombres} {usuario.Apellidos}".Trim()
            : usuario.NombreEmpresa.Trim();
        var solicitud = new ReporteVentaBackOffice
        {
            Cliente = Truncar($"{nombre} | {usuario.Email}", 150),
            Producto = "e-fact",
            PlanPaquete = Truncar(compra.Descripcion, 100),
            Valor = total,
            Fecha = DateTime.Now,
            Canal = "Transferencia Movil",
            Vendedor = "Solicitud movil",
            Estado = "pendiente",
            FormaPago = "Transferencia Bancaria",
            Observacion = Truncar($"[CompraDocs:{compraId}] Banco: {model.Banco.Trim()}. Cuenta origen: {model.CuentaOrigen.Trim()}. N. comprobante: {model.NumeroComprobante.Trim().ToUpperInvariant()}. Titular: {model.Titular.Trim()}. Solicitud pendiente de aprobacion por compra de documentos.", 500),
            ComprobanteArchivo = comprobante
        };
        context.ReporteVentasBackOffice.Add(solicitud);
        await context.SaveChangesAsync();

        try
        {
            await _emailService.EnviarAvisoCobroPendienteAsync(usuario.Email.Trim(), nombre, solicitud.Producto, solicitud.PlanPaquete, solicitud.Valor, solicitud.FormaPago, model.NumeroComprobante.Trim().ToUpperInvariant());
        }
        catch
        {
            // La solicitud ya fue registrada y el BackOffice puede validarla aunque falle el aviso.
        }

        return Ok(new { purchaseId = compraId, status = "Pendiente", message = "Tu transferencia fue registrada. Sera validada en un plazo maximo de 24 horas." });
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
            return (JsonSerializer.Deserialize<List<CompraDocumentosHistorialItem>>(historialJson, HistorialJsonOptions)
                ?? new List<CompraDocumentosHistorialItem>())
                .Where(item => !EsCompraDeUnaNoPagada(item))
                .ToList();
        }
        catch (JsonException)
        {
            return new List<CompraDocumentosHistorialItem>();
        }
    }

    private static bool EsCompraDeUnaNoPagada(CompraDocumentosHistorialItem item) =>
        !item.SaldoAplicado &&
        !EsCompraAprobada(item) &&
        !string.IsNullOrWhiteSpace(item.CustomValue) &&
        string.Equals(item.FormaPago, "DeUna", StringComparison.OrdinalIgnoreCase) &&
        item.CustomValue.StartsWith("recarga-documentos|", StringComparison.OrdinalIgnoreCase);

    private static bool EsCompraAprobada(CompraDocumentosHistorialItem item) =>
        (item.Estado ?? string.Empty).Contains("aprob", StringComparison.OrdinalIgnoreCase) ||
        (item.Estado ?? string.Empty).Contains("autoriz", StringComparison.OrdinalIgnoreCase) ||
        (item.Estado ?? string.Empty).Contains("pag", StringComparison.OrdinalIgnoreCase);

    private static string Truncar(string value, int maximo) => value.Length <= maximo ? value : value[..maximo];

    private static bool EsImagenComprobante(byte[] bytes) =>
        bytes.Length >= 8 &&
        ((bytes[0] == 0xFF && bytes[1] == 0xD8 && bytes[2] == 0xFF) ||
         (bytes[0] == 0x89 && bytes[1] == 0x50 && bytes[2] == 0x4E && bytes[3] == 0x47 && bytes[4] == 0x0D && bytes[5] == 0x0A && bytes[6] == 0x1A && bytes[7] == 0x0A));
}

public class CompraDocumentosMobileDto
{
    public int Documentos { get; set; }
    public decimal MontoTotal { get; set; }
    public string? Descripcion { get; set; }
    public string? EmailDestino { get; set; }
    public bool EsIlimitado { get; set; }
    public bool EsPermanente { get; set; }
}

public sealed class CompraDocumentosTransferenciaDto : CompraDocumentosMobileDto
{
    public string Banco { get; set; } = string.Empty;
    public string Titular { get; set; } = string.Empty;
    public string CuentaOrigen { get; set; } = string.Empty;
    public string NumeroComprobante { get; set; } = string.Empty;
    public string? ComprobanteBase64 { get; set; }
}
