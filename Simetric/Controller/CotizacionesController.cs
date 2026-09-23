using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Simetric.Data;
using Simetric.DTOs;
using Simetric.Models;
using Simetric.Services;

namespace Simetric.Controllers;

[ApiController]
[Authorize]
[Route("api/cotizaciones")]
public sealed class CotizacionesController : ControllerBase
{
    private const decimal PrecioMaximo = 99999999.99m;
    private readonly AppDbContext _db;
    private readonly CotizacionSchemaService _schema;
    private readonly FacturacionService _facturacion;
    private readonly IFacturaPdfService _pdf;
    private readonly ILogger<CotizacionesController> _logger;

    public CotizacionesController(
        AppDbContext db,
        CotizacionSchemaService schema,
        FacturacionService facturacion,
        IFacturaPdfService pdf,
        ILogger<CotizacionesController> logger)
    {
        _db = db;
        _schema = schema;
        _facturacion = facturacion;
        _pdf = pdf;
        _logger = logger;
    }

    public sealed class CotizacionInputDto
    {
        public string? Titulo { get; set; }
        public int IdCliente { get; set; }
        public string? FormaPago { get; set; }
        public string? Detalle { get; set; }
        public DateTime? FechaVigencia { get; set; }
        public List<CotizacionDetalleInputDto> Detalles { get; set; } = new();
    }

    public sealed class CotizacionDetalleInputDto
    {
        public int CodigoProducto { get; set; }
        public decimal PrecioUnitario { get; set; }
        public int Cantidad { get; set; }
        public decimal Descuento { get; set; }
        public int? TarifaIva { get; set; }
        public string? Detalle { get; set; }
    }

    public sealed class EmitirCotizacionDto
    {
        public int? IdUsuario { get; set; }
        public int CodEmisor { get; set; }
        public string? Serie { get; set; }
    }

    public sealed class CotizacionDto
    {
        public int Id { get; set; }
        public string Titulo { get; set; } = "Proforma";
        public int? IdCliente { get; set; }
        public string NombreCliente { get; set; } = "Sin cliente";
        public string? IdentificacionCliente { get; set; }
        public DateTime FechaCreacion { get; set; }
        public DateTime? FechaAprobacion { get; set; }
        public DateTime? FechaVigencia { get; set; }
        public string Estado { get; set; } = "Pendiente";
        public string? FormaPago { get; set; }
        public string? Detalle { get; set; }
        public int? CodFactura { get; set; }
        public decimal TotalEstimado { get; set; }
        public List<CotizacionDetalleDto> Detalles { get; set; } = new();
    }

    public sealed class CotizacionDetalleDto
    {
        public int CodigoProducto { get; set; }
        public string NombreProducto { get; set; } = string.Empty;
        public decimal PrecioUnitario { get; set; }
        public int Cantidad { get; set; }
        public decimal Descuento { get; set; }
        public int TarifaIva { get; set; }
        public string? Detalle { get; set; }
        public decimal TotalLinea { get; set; }
    }

    [HttpGet]
    public async Task<ActionResult<List<CotizacionDto>>> Listar([FromQuery] int userId)
    {
        var ownerId = await GetOwnerIdAsync(userId);
        if (ownerId is null) return Unauthorized();

        await _schema.EnsureSchemaAsync();
        var cotizaciones = await _db.Cotizaciones
            .AsNoTracking()
            .Include(x => x.Detalles)
            .Where(x => x.IdUsuario == ownerId.Value)
            .OrderByDescending(x => x.FechaCreacion)
            .ToListAsync();

        var clientes = await _db.Clientes.AsNoTracking()
            .Where(x => x.Usuario == ownerId.Value && cotizaciones.Select(c => c.IdCliente).Contains(x.Codcliente))
            .ToDictionaryAsync(x => x.Codcliente);

        return cotizaciones.Select(x => Mapear(x, clientes.GetValueOrDefault(x.IdCliente ?? 0))).ToList();
    }

    [HttpPost]
    public async Task<ActionResult<CotizacionDto>> Crear([FromQuery] int userId, [FromBody] CotizacionInputDto input)
    {
        var ownerId = await GetOwnerIdAsync(userId);
        if (ownerId is null) return Unauthorized();

        var resultado = await GuardarAsync(ownerId.Value, input, null);
        return resultado.Error is not null ? BadRequest(new { mensaje = resultado.Error }) : Ok(Mapear(resultado.Cotizacion!, resultado.Cliente));
    }

    [HttpPut("{id:int}")]
    public async Task<ActionResult<CotizacionDto>> Editar(int id, [FromQuery] int userId, [FromBody] CotizacionInputDto input)
    {
        var ownerId = await GetOwnerIdAsync(userId);
        if (ownerId is null) return Unauthorized();

        await _schema.EnsureSchemaAsync();
        var cotizacion = await _db.Cotizaciones.Include(x => x.Detalles)
            .FirstOrDefaultAsync(x => x.Id == id && x.IdUsuario == ownerId.Value);
        if (cotizacion is null) return NotFound();
        if (!EsPendiente(cotizacion)) return BadRequest(new { mensaje = "La proforma ya fue aprobada y no se puede editar." });

        var resultado = await GuardarAsync(ownerId.Value, input, cotizacion);
        return resultado.Error is not null ? BadRequest(new { mensaje = resultado.Error }) : Ok(Mapear(resultado.Cotizacion!, resultado.Cliente));
    }

    [HttpPost("{id:int}/aprobar")]
    public async Task<IActionResult> Aprobar(int id, [FromQuery] int userId)
    {
        var ownerId = await GetOwnerIdAsync(userId);
        if (ownerId is null) return Unauthorized();

        await _schema.EnsureSchemaAsync();
        var cotizacion = await _db.Cotizaciones.FirstOrDefaultAsync(x => x.Id == id && x.IdUsuario == ownerId.Value);
        if (cotizacion is null) return NotFound();
        if (!EsPendiente(cotizacion)) return BadRequest(new { mensaje = "La proforma ya no está pendiente." });

        cotizacion.Estado = "Aprobada";
        cotizacion.FechaAprobacion = DateTime.Now;
        await _db.SaveChangesAsync();
        return Ok(new { mensaje = "Proforma aprobada correctamente." });
    }

    [HttpPost("{id:int}/dar-baja")]
    public async Task<IActionResult> DarDeBaja(int id, [FromQuery] int userId)
    {
        var ownerId = await GetOwnerIdAsync(userId);
        if (ownerId is null) return Unauthorized();

        await _schema.EnsureSchemaAsync();
        var cotizacion = await _db.Cotizaciones.FirstOrDefaultAsync(x => x.Id == id && x.IdUsuario == ownerId.Value);
        if (cotizacion is null) return NotFound();
        if (string.Equals(cotizacion.Estado, "Facturada", StringComparison.OrdinalIgnoreCase))
            return BadRequest(new { mensaje = "Una proforma facturada no puede darse de baja." });
        if (string.Equals(cotizacion.Estado, "Baja", StringComparison.OrdinalIgnoreCase))
            return BadRequest(new { mensaje = "La proforma ya está dada de baja." });

        cotizacion.Estado = "Baja";
        await _db.SaveChangesAsync();
        return Ok(new { mensaje = "Proforma dada de baja correctamente." });
    }

    [HttpGet("{id:int}/pdf")]
    public async Task<IActionResult> Pdf(int id, [FromQuery] int userId)
    {
        var ownerId = await GetOwnerIdAsync(userId);
        if (ownerId is null) return Unauthorized();

        var view = await ConstruirVistaProformaAsync(id, ownerId.Value);
        if (view is null) return NotFound();

        var path = await _pdf.GenerarPdfProformaAsync(view);
        return PhysicalFile(path, "application/pdf", $"proforma_{id}.pdf", enableRangeProcessing: true);
    }

    [HttpPost("{id:int}/emitir")]
    public async Task<IActionResult> Emitir(int id, [FromBody] EmitirCotizacionDto input)
    {
        if (input is null) return BadRequest(new { mensaje = "Confirma el emisor, la caja y la serie antes de emitir." });

        var authenticatedUserId = ResolverUserId(input.IdUsuario);
        if (authenticatedUserId is null) return Unauthorized();

        var userId = authenticatedUserId.Value;
        var ownerId = await GetOwnerIdAsync(userId);
        if (ownerId is null) return Unauthorized();

        await _schema.EnsureSchemaAsync();
        var cotizacion = await _db.Cotizaciones.Include(x => x.Detalles)
            .FirstOrDefaultAsync(x => x.Id == id && x.IdUsuario == ownerId.Value);
        if (cotizacion is null) return NotFound();
        if (!string.Equals(cotizacion.Estado, "Aprobada", StringComparison.OrdinalIgnoreCase))
            return BadRequest(new { mensaje = "Solo se puede emitir una proforma aprobada." });
        if (cotizacion.CodFactura is > 0)
            return Ok(new { mensaje = "La factura ya fue emitida.", codfactura = cotizacion.CodFactura });

        var serie = input.Serie?.Trim();
        if (input.CodEmisor <= 0 || string.IsNullOrWhiteSpace(serie))
            return BadRequest(new { mensaje = "Confirma el emisor, la caja y la serie antes de emitir." });

        var emisor = (await _facturacion.GetEmisoresActivosAsync(userId))
            .FirstOrDefault(x => x.Codigo == input.CodEmisor);
        if (emisor is null) return BadRequest(new { mensaje = "El emisor seleccionado no está disponible." });

        var cliente = await _db.Clientes.AsNoTracking()
            .FirstOrDefaultAsync(x => x.Codcliente == cotizacion.IdCliente && x.Usuario == ownerId.Value);
        if (cliente is null) return BadRequest(new { mensaje = "El cliente de la proforma ya no está disponible." });

        var numero = await _facturacion.GetNextFacturaNumeroAsync(userId, emisor.Codigo, serie);
        if (string.IsNullOrWhiteSpace(numero))
            return Conflict(new { requiereSecuencia = true, mensaje = "Confirma la secuencia inicial de la caja antes de emitir." });

        var detalles = cotizacion.Detalles.Select(d =>
        {
            var baseImponible = Math.Max(0m, d.PrecioUnitario * d.Cantidad - d.Descuento);
            var iva = Math.Round(baseImponible * d.TarifaIva / 100m, 2, MidpointRounding.AwayFromZero);
            return new Detallefactura
            {
                Codproducto = d.CodigoProducto,
                Cantproducto = d.Cantidad,
                Precioproducto = d.PrecioUnitario,
                Descuento = d.Descuento,
                Descripproducto = string.IsNullOrWhiteSpace(d.Detalle) ? d.NombreProducto : $"{d.NombreProducto} - {d.Detalle.Trim()}",
                Tarifa = d.TarifaIva,
                Valortproducto = baseImponible,
                Valoriva = iva,
                Valortotal = baseImponible + iva
            };
        }).ToList();

        var factura = new Factura
        {
            Codclientes = cliente.Codcliente,
            Codemisor = emisor.Codigo,
            Coddocumento = 1,
            Tipopago = cotizacion.FormaPago,
            Serie = serie,
            Numfactura = numero,
            Idusuario = userId,
            Fechaentrega = DateTime.Now,
            Ambiente = 2,
            Estado = true,
            Autorizado = false,
            Notas = cotizacion.Detalle,
            Subtotal = detalles.Sum(x => x.Valortproducto),
            Subtotal12 = detalles.Where(x => x.Tarifa > 0).Sum(x => x.Valortproducto),
            Subtotal0 = detalles.Where(x => x.Tarifa == 0).Sum(x => x.Valortproducto),
            Descuentos = detalles.Sum(x => x.Descuento ?? 0m),
            Iva = detalles.Sum(x => x.Valoriva),
            Valortotal = detalles.Sum(x => x.Valortotal),
            Valorapagar = detalles.Sum(x => x.Valortotal)
        };

        try
        {
            if (!await _facturacion.GuardarFacturaCompletaAsync(userId, factura, cliente, detalles))
                return BadRequest(new { mensaje = _facturacion.UltimoErrorGuardarFactura ?? "No se pudo emitir la factura." });

            object? sri = null;
            try { sri = await _facturacion.ReintentarEnvioSriFacturaAsync(factura.Codfactura); }
            catch (Exception ex) { _logger.LogWarning(ex, "La factura de la proforma {CotizacionId} se guardó sin reintento SRI.", id); }

            cotizacion.Estado = "Facturada";
            cotizacion.CodFactura = factura.Codfactura;
            await _db.SaveChangesAsync();

            return Ok(new { mensaje = "Factura emitida correctamente.", codfactura = factura.Codfactura, sri });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "No se pudo emitir la factura de la proforma {CotizacionId}.", id);
            return StatusCode(StatusCodes.Status500InternalServerError, new { mensaje = "No se pudo emitir la factura." });
        }
    }

    private async Task<(Cotizacion? Cotizacion, Cliente? Cliente, string? Error)> GuardarAsync(
        int ownerId,
        CotizacionInputDto input,
        Cotizacion? cotizacion)
    {
        if (input is null) return (null, null, "La información de la proforma es obligatoria.");
        if (string.IsNullOrWhiteSpace(input.Titulo)) return (null, null, "Cada proforma debe tener un título.");
        if (input.Titulo.Trim().Length > 200) return (null, null, "El título no puede superar los 200 caracteres.");
        if (input.IdCliente <= 0) return (null, null, "Selecciona un cliente.");
        if (string.IsNullOrWhiteSpace(input.FormaPago)) return (null, null, "Selecciona una forma de pago.");
        if (input.FormaPago.Trim().Length > 10) return (null, null, "La forma de pago no puede superar los 10 caracteres.");
        if (input.Detalle?.Length > 1000) return (null, null, "Las observaciones no pueden superar los 1000 caracteres.");
        if (input.FechaVigencia?.Date < DateTime.Today) return (null, null, "La fecha de vigencia no puede estar en el pasado.");
        if (input.Detalles is null || input.Detalles.Count == 0) return (null, null, "Agrega al menos un producto.");
        if (input.Detalles.Any(x => x.Detalle?.Length > 500)) return (null, null, "El detalle de una línea no puede superar los 500 caracteres.");
        if (input.Detalles.Any(x => x.CodigoProducto <= 0 || x.Cantidad < 1 || x.PrecioUnitario < 0 || x.PrecioUnitario > PrecioMaximo || x.Descuento < 0 || x.Descuento > x.PrecioUnitario * x.Cantidad || x.TarifaIva is < 0 or > 100))
            return (null, null, "Revisa cantidades, precios y descuentos.");

        await _schema.EnsureSchemaAsync();
        var cliente = await _db.Clientes.AsNoTracking().FirstOrDefaultAsync(x => x.Codcliente == input.IdCliente && x.Usuario == ownerId && (x.Estado == null || x.Estado == true));
        if (cliente is null) return (null, null, "El cliente seleccionado no está disponible.");

        var codigos = input.Detalles.Select(x => x.CodigoProducto).Distinct().ToList();
        var productos = await _db.Productos.AsNoTracking()
            .Where(x => x.Idusuario == ownerId && (x.Estado == null || x.Estado == true) && codigos.Contains(x.Codigo))
            .Select(x => new { x.Codigo, x.Nombre, x.Porcentajeimpuesto })
            .ToDictionaryAsync(x => x.Codigo);
        if (productos.Count != codigos.Count) return (null, null, "Uno o más productos ya no están disponibles.");

        cotizacion ??= new Cotizacion { IdUsuario = ownerId, FechaCreacion = DateTime.Now };
        cotizacion.Titulo = input.Titulo.Trim();
        cotizacion.IdCliente = cliente.Codcliente;
        cotizacion.FormaPago = input.FormaPago?.Trim();
        cotizacion.Detalle = string.IsNullOrWhiteSpace(input.Detalle) ? null : input.Detalle.Trim();
        cotizacion.FechaVigencia = input.FechaVigencia?.Date;
        cotizacion.Estado = "Pendiente";
        cotizacion.FechaAprobacion = null;
        cotizacion.CodFactura = null;
        cotizacion.Detalles.Clear();

        foreach (var item in input.Detalles)
        {
            var producto = productos[item.CodigoProducto];
            var tarifa = item.TarifaIva.HasValue
                ? Math.Clamp(item.TarifaIva.Value, 0, 100)
                : ObtenerTarifaIva(producto.Porcentajeimpuesto);
            var precio = decimal.Round(item.PrecioUnitario, 2, MidpointRounding.AwayFromZero);
            var descuento = decimal.Round(item.Descuento, 2, MidpointRounding.AwayFromZero);
            var baseImponible = Math.Max(0m, precio * item.Cantidad - descuento);
            var iva = decimal.Round(baseImponible * tarifa / 100m, 2, MidpointRounding.AwayFromZero);
            cotizacion.Detalles.Add(new CotizacionDetalle
            {
                CodigoProducto = producto.Codigo,
                NombreProducto = producto.Nombre ?? "Producto / servicio",
                PrecioUnitario = precio,
                Cantidad = item.Cantidad,
                Descuento = descuento,
                TarifaIva = tarifa,
                Detalle = string.IsNullOrWhiteSpace(item.Detalle) ? null : item.Detalle.Trim(),
                TotalLinea = baseImponible + iva
            });
        }

        cotizacion.TotalEstimado = cotizacion.Detalles.Sum(x => x.TotalLinea);
        if (cotizacion.Id == 0) _db.Cotizaciones.Add(cotizacion);
        await _db.SaveChangesAsync();
        return (cotizacion, cliente, null);
    }

    private async Task<FacturaViewDto?> ConstruirVistaProformaAsync(int id, int ownerId)
    {
        var cotizacion = await _db.Cotizaciones.AsNoTracking().Include(x => x.Detalles)
            .FirstOrDefaultAsync(x => x.Id == id && x.IdUsuario == ownerId);
        if (cotizacion is null) return null;

        var cliente = cotizacion.IdCliente is > 0
            ? await _db.Clientes.AsNoTracking().FirstOrDefaultAsync(x => x.Codcliente == cotizacion.IdCliente && x.Usuario == ownerId)
            : null;
        var emisor = (await _facturacion.GetEmisoresActivosAsync(ownerId)).FirstOrDefault();
        var formaPago = await _db.FormasPago.AsNoTracking().FirstOrDefaultAsync(x => x.Codigo == cotizacion.FormaPago);
        var detalles = cotizacion.Detalles.Select(d =>
        {
            var baseImponible = Math.Max(0m, d.PrecioUnitario * d.Cantidad - d.Descuento);
            var iva = Math.Round(baseImponible * d.TarifaIva / 100m, 2, MidpointRounding.AwayFromZero);
            return new Detallefactura
            {
                Codproducto = d.CodigoProducto,
                Cantproducto = d.Cantidad,
                Precioproducto = d.PrecioUnitario,
                Descuento = d.Descuento,
                Descripproducto = string.IsNullOrWhiteSpace(d.Detalle) ? d.NombreProducto : $"{d.NombreProducto} - {d.Detalle.Trim()}",
                Tarifa = d.TarifaIva,
                Valortproducto = baseImponible,
                Valoriva = iva,
                Valortotal = baseImponible + iva
            };
        }).ToList();

        return new FacturaViewDto
        {
            TituloDocumento = cotizacion.Titulo,
            Cliente = cliente,
            Emisor = emisor,
            FormaPagoNombre = formaPago?.Descripcion ?? cotizacion.FormaPago ?? string.Empty,
            Factura = new Factura
            {
                Codfactura = cotizacion.Id,
                Fechaentrega = cotizacion.FechaCreacion,
                Fechavence = cotizacion.FechaVigencia,
                Tipopago = cotizacion.FormaPago,
                Subtotal = detalles.Sum(x => x.Valortproducto),
                Subtotal12 = detalles.Where(x => x.Tarifa > 0).Sum(x => x.Valortproducto),
                Subtotal0 = detalles.Where(x => x.Tarifa == 0).Sum(x => x.Valortproducto),
                Descuentos = detalles.Sum(x => x.Descuento ?? 0m),
                Iva = detalles.Sum(x => x.Valoriva),
                Valortotal = detalles.Sum(x => x.Valortotal),
                Notas = cotizacion.Detalle,
                Numfactura = cotizacion.Id.ToString("D6")
            },
            Detalles = detalles
        };
    }

    private async Task<int?> GetOwnerIdAsync(int? requestedUserId)
    {
        var userId = ResolverUserId(requestedUserId);
        if (userId is null) return null;

        var user = await _db.Usuarios.AsNoTracking().Where(x => x.IdUsuario == userId.Value).Select(x => new { x.IdUsuario, x.idJefe }).FirstOrDefaultAsync();
        return user is null ? null : user.idJefe ?? user.IdUsuario;
    }

    private int? ResolverUserId(int? requested)
    {
        if (User.Identity?.IsAuthenticated != true) return null;

        var claim = User.FindFirst("IdUsuario")?.Value ?? User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value;
        if (!int.TryParse(claim, out var claimId) || claimId <= 0) return null;
        return requested is > 0 && requested.Value != claimId ? null : claimId;
    }

    private static bool EsPendiente(Cotizacion cotizacion) => string.IsNullOrWhiteSpace(cotizacion.Estado) || string.Equals(cotizacion.Estado, "Pendiente", StringComparison.OrdinalIgnoreCase);

    private static int ObtenerTarifaIva(string? valor)
    {
        var porcentaje = TaxRateHelper.ParsePercentOrZero(valor);
        return (int)Math.Round(porcentaje, 0, MidpointRounding.AwayFromZero) switch
        {
            2 => 12,
            3 => 14,
            4 => 15,
            5 => 5,
            10 => 13,
            13 => 13,
            14 => 14,
            15 => 15,
            _ => (int)Math.Round(porcentaje, 0, MidpointRounding.AwayFromZero)
        };
    }

    private static CotizacionDto Mapear(Cotizacion x, Cliente? cliente) => new()
    {
        Id = x.Id,
        Titulo = string.IsNullOrWhiteSpace(x.Titulo) ? "Proforma" : x.Titulo,
        IdCliente = x.IdCliente,
        NombreCliente = ObtenerNombreCliente(cliente),
        IdentificacionCliente = cliente?.Numeroidentificacion,
        FechaCreacion = x.FechaCreacion,
        FechaAprobacion = x.FechaAprobacion,
        FechaVigencia = x.FechaVigencia,
        Estado = string.IsNullOrWhiteSpace(x.Estado) ? "Pendiente" : x.Estado,
        FormaPago = x.FormaPago,
        Detalle = x.Detalle,
        CodFactura = x.CodFactura,
        TotalEstimado = x.TotalEstimado,
        Detalles = x.Detalles.Select(d => new CotizacionDetalleDto
        {
            CodigoProducto = d.CodigoProducto,
            NombreProducto = d.NombreProducto,
            PrecioUnitario = d.PrecioUnitario,
            Cantidad = d.Cantidad,
            Descuento = d.Descuento,
            TarifaIva = d.TarifaIva,
            Detalle = d.Detalle,
            TotalLinea = d.TotalLinea
        }).ToList()
    };

    private static string ObtenerNombreCliente(Cliente? cliente)
    {
        if (cliente is null) return "Sin cliente";
        return string.Join(" ", new[] { cliente.Nombrerazonsocial ?? cliente.Nombrecomercial, cliente.Nombres, cliente.Apellidos }.Where(x => !string.IsNullOrWhiteSpace(x))).Trim();
    }
}
