using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Simetric.Data;
using Simetric.DTOs;
using Simetric.Models;
using Simetric.Services;

namespace Simetric.Controllers;

[ApiController]
[Route("api/ordenes-transferencia")]
public sealed class OrdenesTransferenciaController : UsuarioApiControllerBase
{
    private static readonly JsonSerializerOptions HistorialJsonOptions = new(JsonSerializerDefaults.Web);
    private static readonly HashSet<string> BancosTransferencia = new(StringComparer.OrdinalIgnoreCase)
    {
        "Banco Pichincha", "Banco Guayaquil", "Banco Internacional", "Banco Pacifico",
        "Banco Produbanco", "Banco Bolivariano", "Cooperativa JEP", "Otra institucion"
    };

    private readonly IDbContextFactory<AppDbContext> _dbFactory;
    private readonly IEmailService _emailService;

    public OrdenesTransferenciaController(
        IDbContextFactory<AppDbContext> dbFactory,
        IEmailService emailService)
    {
        _dbFactory = dbFactory;
        _emailService = emailService;
    }

    [HttpPost]
    [RequestSizeLimit(8 * 1024 * 1024)]
    public async Task<IActionResult> Crear(
        [FromBody] OrdenTransferenciaRequest request,
        [FromHeader(Name = "Idempotency-Key")] string? idempotencyKey,
        CancellationToken cancellationToken)
    {
        var validacion = ValidarDatosTransferencia(request);
        if (validacion is not null)
            return BadRequest(new { mensaje = validacion });

        var claveIdempotencia = (idempotencyKey ?? request.IdempotencyKey)?.Trim();
        if (!string.IsNullOrWhiteSpace(claveIdempotencia) &&
            (claveIdempotencia.Length > 100 || !Regex.IsMatch(claveIdempotencia, "^[A-Za-z0-9._:-]+$")))
            return BadRequest(new { mensaje = "La clave de idempotencia solo puede contener letras, numeros, punto, guion, dos puntos y guion bajo." });

        var comprobante = DecodificarComprobante(request.ComprobanteBase64);
        if (comprobante is null || !EsImagenComprobante(comprobante))
            return BadRequest(new { mensaje = "El comprobante debe ser una imagen JPG o PNG valida de hasta 5 MB." });

        await using var context = await _dbFactory.CreateDbContextAsync(cancellationToken);

        if (!string.IsNullOrWhiteSpace(claveIdempotencia))
        {
            var marcadorIdempotencia = $"[Idempotencia:{claveIdempotencia}]";
            var ordenExistente = await context.ReporteVentasBackOffice.AsNoTracking()
                .Where(v => v.Observacion != null && v.Observacion.Contains(marcadorIdempotencia))
                .OrderBy(v => v.IdReporte)
                .Select(v => new { v.Observacion, v.Estado, v.Valor })
                .ToListAsync(cancellationToken);

            if (ordenExistente.Count > 0)
            {
                return Ok(new
                {
                    orderId = ExtraerMarcador(ordenExistente[0].Observacion, "[Orden:"),
                    estado = ordenExistente[0].Estado,
                    total = ordenExistente.Sum(v => v.Valor),
                    duplicada = true
                });
            }
        }

        var cliente = await BuscarClienteAsync(context, request.Cliente, cancellationToken);
        if (cliente is null)
            return NotFound(new { mensaje = "No se encontro un usuario normal de E-Fact activo con el nombre indicado." });

        var lineas = ConstruirLineas(request.Productos);
        if (lineas.Error is not null)
            return BadRequest(new { mensaje = lineas.Error });

        var orderId = Guid.NewGuid().ToString("N");
        var ahora = DateTime.Now;
        var marcadorIdempotenciaNuevo = string.IsNullOrWhiteSpace(claveIdempotencia)
            ? string.Empty
            : $" [Idempotencia:{claveIdempotencia}]";
        await using var transaction = await context.Database.BeginTransactionAsync(cancellationToken);

        try
        {
            if (lineas.Documentos > 0)
            {
                var historial = LeerHistorial(cliente.HistorialComprasDocumentosJson);
                historial.Insert(0, new CompraDocumentosHistorialItem
                {
                    Id = orderId,
                    Fecha = ahora,
                    Documentos = lineas.Documentos,
                    MontoTotal = lineas.TotalDocumentos,
                    Estado = "Pendiente",
                    Descripcion = $"Orden de transferencia: {lineas.Documentos} documentos E-FACT",
                    CustomValue = $"orden-transferencia|order:{orderId}|user:{cliente.IdUsuario}|docs:{lineas.Documentos}",
                    FormaPago = "Transferencia",
                    EmailDestino = cliente.Email.Trim()
                });
                cliente.HistorialComprasDocumentosJson = JsonSerializer.Serialize(historial, HistorialJsonOptions);
            }

            var nombreCliente = $"{cliente.Nombres} {cliente.Apellidos}".Trim();
            var productos = lineas.Items.Select(item =>
                $"{item.Producto}: {item.PlanPaquete} (${item.Valor.ToString("0.00", CultureInfo.InvariantCulture)})");

            foreach (var linea in lineas.Items)
            {
                var marcador = linea.Producto.Equals("e-fact", StringComparison.OrdinalIgnoreCase)
                    ? $"[CompraDocs:{orderId}]"
                    : $"[OrdenTransferencia:{orderId}]";

                context.ReporteVentasBackOffice.Add(new ReporteVentaBackOffice
                {
                    Cliente = Truncar($"{nombreCliente} | {cliente.Email}", 150),
                    Producto = linea.Producto,
                    PlanPaquete = Truncar(linea.PlanPaquete, 100),
                    Valor = linea.Valor,
                    Fecha = ahora,
                    Canal = "Transferencia Web - API",
                    Vendedor = "API ordenes transferencia",
                    Estado = "pendiente",
                    FormaPago = "Transferencia Bancaria",
                    Observacion = Truncar(
                        $"{marcador} [Orden:{orderId}]{marcadorIdempotenciaNuevo} Banco: {request.Banco.Trim()}. Cuenta origen: {request.CuentaOrigen.Trim()}. " +
                        $"N. comprobante: {request.NumeroComprobante.Trim().ToUpperInvariant()}. Titular: {request.TitularCuenta.Trim()}. " +
                        $"Productos de la orden: {string.Join("; ", productos)}. Solicitud pendiente de aprobacion por transferencia API.",
                        500),
                    ComprobanteArchivo = comprobante
                });
            }

            await context.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);

            try
            {
                await _emailService.EnviarAvisoCobroPendienteAsync(
                    cliente.Email.Trim(), nombreCliente, "orden-transferencia",
                    Truncar(string.Join("; ", productos), 100), lineas.Total,
                    "Transferencia Bancaria", request.NumeroComprobante.Trim().ToUpperInvariant());
            }
            catch
            {
                // La orden ya queda disponible en Cobros aunque falle el aviso.
            }

            return Ok(new
            {
                orderId,
                clienteId = cliente.IdUsuario,
                estado = "pendiente",
                total = lineas.Total,
                productos = lineas.Items.Select(item => new
                {
                    producto = item.Producto,
                    item.PlanPaquete,
                    valor = item.Valor
                })
            });
        }
        catch
        {
            await transaction.RollbackAsync(cancellationToken);
            throw;
        }
    }

    private static string? ValidarDatosTransferencia(OrdenTransferenciaRequest request)
    {
        if (request is null)
            return "El cuerpo de la solicitud es obligatorio.";
        if (!BancosTransferencia.Contains(request.Banco.Trim()))
            return "Selecciona un banco valido.";
        if (string.IsNullOrWhiteSpace(request.TitularCuenta) || string.IsNullOrWhiteSpace(request.CuentaOrigen) ||
            string.IsNullOrWhiteSpace(request.NumeroComprobante))
            return "Completa los datos de la transferencia.";
        if (!request.CuentaOrigen.Trim().All(char.IsDigit))
            return "La cuenta de origen solo debe contener numeros.";
        if (request.NumeroComprobante.Trim().Length > 50 || !request.NumeroComprobante.Trim().All(char.IsLetterOrDigit))
            return "El numero de comprobante debe tener maximo 50 caracteres alfanumericos.";

        var titular = request.TitularCuenta.Trim().Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (titular.Length < 2 || titular.Count(item => item.Length >= 2 && item.All(char.IsLetter)) < 2)
            return "El titular debe incluir al menos un nombre y un apellido.";
        if (request.Cliente is null || string.IsNullOrWhiteSpace(request.Cliente.Nombres) ||
            string.IsNullOrWhiteSpace(request.Cliente.Apellidos))
            return "El cliente debe incluir al menos un nombre y un apellido.";
        if (request.Productos is null || request.Productos.Count == 0 || request.Productos.Count > 2)
            return "Debe indicar uno o dos productos en la orden.";

        return null;
    }

    private static async Task<Usuario?> BuscarClienteAsync(
        AppDbContext context,
        ClienteOrdenTransferenciaDto datos,
        CancellationToken cancellationToken)
    {
        var nombres = NormalizarNombre(datos.Nombres);
        var apellidos = NormalizarNombre(datos.Apellidos);
        var query = context.Usuarios.AsNoTracking()
            .Where(u => u.IdTipoUsuario == 1 && u.Estado == true &&
                        u.Identificacion != "9999999999999" &&
                        u.Email != "consumidorfinal@numerica");

        if (datos.IdUsuario is > 0)
            query = query.Where(u => u.IdUsuario == datos.IdUsuario.Value);
        else if (!string.IsNullOrWhiteSpace(datos.Email))
            query = query.Where(u => u.Email == datos.Email.Trim().ToLowerInvariant());

        var candidatos = await query
            .Select(u => new Usuario
            {
                IdUsuario = u.IdUsuario,
                Nombres = u.Nombres,
                Apellidos = u.Apellidos,
                Email = u.Email,
                HistorialComprasDocumentosJson = u.HistorialComprasDocumentosJson
            })
            .ToListAsync(cancellationToken);

        var coincidencias = candidatos.Where(u =>
            NormalizarNombre(u.Nombres) == nombres && NormalizarNombre(u.Apellidos) == apellidos).ToList();

        return coincidencias.Count == 1 ? coincidencias[0] : null;
    }

    private static (List<LineaOrden> Items, int Documentos, decimal TotalDocumentos, decimal Total, string? Error)
        ConstruirLineas(IEnumerable<ProductoOrdenTransferenciaDto> productos)
    {
        var lineas = new List<LineaOrden>();
        var tipos = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var producto in productos)
        {
            var tipo = NormalizarTipoProducto(producto.Tipo);
            if (tipo is null || !tipos.Add(tipo))
                return (lineas, 0, 0m, 0m, "Los productos deben ser e-fact y/o e-rubrica, sin repetirlos.");

            if (tipo == "e-fact")
            {
                if (producto.CantidadDocumentos < EFactDocumentPricing.DocumentosMinimosPersonalizados)
                    return (lineas, 0, 0m, 0m, $"La compra de documentos requiere al menos {EFactDocumentPricing.DocumentosMinimosPersonalizados} documentos.");

                var valor = EFactDocumentPricing.CalcularTotalPorCantidad(producto.CantidadDocumentos);
                lineas.Add(new LineaOrden("e-fact", $"{producto.CantidadDocumentos} documentos E-FACT", valor));
            }
            else
            {
                var vigencia = producto.VigenciaFirma?.Trim().ToUpperInvariant();
                var valor = ESignPricing.ObtenerPrecioFinal(vigencia);
                if (valor <= 0m)
                    return (lineas, 0, 0m, 0m, "La vigencia de e-Rubrica no es valida.");

                lineas.Add(new LineaOrden("e-sign", $"Firma electronica {vigencia}", valor));
            }
        }

        var totalDocumentos = lineas.Where(x => x.Producto == "e-fact").Sum(x => x.Valor);
        var documentos = productos.Where(x => NormalizarTipoProducto(x.Tipo) == "e-fact").Sum(x => x.CantidadDocumentos);
        return (lineas, documentos, totalDocumentos, decimal.Round(lineas.Sum(x => x.Valor), 2, MidpointRounding.AwayFromZero), null);
    }

    private static string? NormalizarTipoProducto(string? tipo) => tipo?.Trim().ToUpperInvariant() switch
    {
        "EFACT" or "E-FACT" or "DOCUMENTOS" or "DOCUMENTOS E-FACT" => "e-fact",
        "ERUBRICA" or "E-RUBRICA" or "E-SIGN" or "FIRMA" or "FIRMA ELECTRONICA" => "e-sign",
        _ => null
    };

    private static byte[]? DecodificarComprobante(string? base64)
    {
        if (string.IsNullOrWhiteSpace(base64))
            return null;

        try
        {
            var contenido = base64.Trim();
            var separador = contenido.IndexOf(',');
            if (separador >= 0) contenido = contenido[(separador + 1)..];
            var bytes = Convert.FromBase64String(contenido);
            return bytes.Length is > 0 and <= 5 * 1024 * 1024 ? bytes : null;
        }
        catch (FormatException)
        {
            return null;
        }
    }

    private static bool EsImagenComprobante(byte[] bytes) =>
        bytes.Length >= 8 &&
        ((bytes[0] == 0xFF && bytes[1] == 0xD8 && bytes[2] == 0xFF) ||
         (bytes[0] == 0x89 && bytes[1] == 0x50 && bytes[2] == 0x4E && bytes[3] == 0x47 &&
          bytes[4] == 0x0D && bytes[5] == 0x0A && bytes[6] == 0x1A && bytes[7] == 0x0A));

    private static List<CompraDocumentosHistorialItem> LeerHistorial(string? historialJson)
    {
        if (string.IsNullOrWhiteSpace(historialJson))
            return new List<CompraDocumentosHistorialItem>();

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

    private static string NormalizarNombre(string value) =>
        Regex.Replace(value.Trim().Normalize(NormalizationForm.FormD), "\\p{Mn}", string.Empty)
            .Normalize(NormalizationForm.FormC)
            .ToUpperInvariant() switch
        {
            var normalizado => Regex.Replace(normalizado, "\\s+", " ")
        };

    private static string Truncar(string value, int maximo) => value.Length <= maximo ? value : value[..maximo];

    private static string ExtraerMarcador(string? texto, string inicio)
    {
        if (string.IsNullOrWhiteSpace(texto))
            return string.Empty;

        var indice = texto.IndexOf(inicio, StringComparison.OrdinalIgnoreCase);
        if (indice < 0)
            return string.Empty;

        var inicioValor = indice + inicio.Length;
        var fin = texto.IndexOf(']', inicioValor);
        return fin > inicioValor ? texto[inicioValor..fin] : string.Empty;
    }

    private sealed record LineaOrden(string Producto, string PlanPaquete, decimal Valor);
}
