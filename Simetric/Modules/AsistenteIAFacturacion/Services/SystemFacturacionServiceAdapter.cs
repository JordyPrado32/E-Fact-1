using Microsoft.EntityFrameworkCore;
using Simetric.Data;
using Simetric.Models;
using Simetric.Modules.AsistenteIAFacturacion.DTOs;
using Simetric.Services;
using System.Collections.Concurrent;

namespace Simetric.Modules.AsistenteIAFacturacion.Services;

public sealed class SystemFacturacionServiceAdapter : IFacturacionService
{
    private static readonly TimeSpan OwnerCacheLifetime = TimeSpan.FromMinutes(10);
    private static readonly TimeSpan FormasPagoCacheLifetime = TimeSpan.FromMinutes(10);
    private static readonly ConcurrentDictionary<int, CachedValue<int>> OwnerCache = new();
    private static CachedValue<IReadOnlyList<string>>? FormasPagoCache;

    private readonly IDbContextFactory<AppDbContext> _dbFactory;
    private readonly Simetric.Services.FacturacionService _facturacionService;
    private readonly NotaCreditoService _notaCreditoService;

    public SystemFacturacionServiceAdapter(
        IDbContextFactory<AppDbContext> dbFactory,
        Simetric.Services.FacturacionService facturacionService,
        NotaCreditoService notaCreditoService)
    {
        _dbFactory = dbFactory;
        _facturacionService = facturacionService;
        _notaCreditoService = notaCreditoService;
    }

    public async Task<IReadOnlyList<string>> ObtenerFormasPagoAsync(CancellationToken cancellationToken = default)
    {
        if (FormasPagoCache is { } cache && cache.ExpiresAt > DateTimeOffset.UtcNow)
            return cache.Value;

        var formas = await _facturacionService.ObtenerFormasPagoAsync();
        var result = formas
            .Where(x => x.Estado == true && x.TipoVenta == true)
            .Select(x => x.Descripcion?.Trim())
            .Where(x => !string.IsNullOrWhiteSpace(x))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Cast<string>()
            .ToList();

        FormasPagoCache = new CachedValue<IReadOnlyList<string>>(result, DateTimeOffset.UtcNow.Add(FormasPagoCacheLifetime));
        return result;
    }

    public async Task<IReadOnlyList<FacturaReferenciaDto>> BuscarFacturasParaNotaCreditoAsync(int userId, string query, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(query))
            return Array.Empty<FacturaReferenciaDto>();

        await using var context = await _dbFactory.CreateDbContextAsync(cancellationToken);
        var ownerId = await ResolveOwnerIdAsync(context, userId, cancellationToken);
        if (ownerId <= 0)
            return Array.Empty<FacturaReferenciaDto>();

        var normalizedQuery = query.Trim();
        return await context.Facturas
            .AsNoTracking()
            .Where(f => f.Idusuario == ownerId &&
                        f.Numfactura != null &&
                        f.Numfactura.Contains(normalizedQuery))
            .OrderByDescending(f => f.Codfactura)
            .Select(f => new FacturaReferenciaDto
            {
                Id = f.Codfactura,
                NumeroFactura = f.Numfactura ?? string.Empty,
                Serie = f.Serie,
                ClienteNombre = f.Nombread ?? string.Empty
            })
            .Take(5)
            .ToListAsync(cancellationToken);
    }

    public async Task<FacturaEmissionResult> EmitirAsync(int userId, FacturaDraftDto draft, CancellationToken cancellationToken = default)
    {
        await using var context = await _dbFactory.CreateDbContextAsync(cancellationToken);
        var ownerId = await ResolveOwnerIdAsync(context, userId, cancellationToken);
        if (ownerId <= 0)
            return Fail("No fue posible resolver el usuario propietario de la factura.");

        if (draft.Cliente?.Id <= 0)
            return Fail("La factura no tiene un cliente válido.");

        var cliente = await context.Clientes
            .AsNoTracking()
            .FirstOrDefaultAsync(c => c.Codcliente == draft.Cliente.Id && c.Usuario == ownerId, cancellationToken);
        if (cliente is null)
            return Fail("El cliente seleccionado ya no existe en el sistema.");

        var emisor = (await _facturacionService.GetEmisoresActivosAsync(userId)).FirstOrDefault();
        if (emisor is null)
            return Fail("No hay un emisor activo configurado para esta cuenta.");

        var formasPago = await _facturacionService.ObtenerFormasPagoAsync();
        var formaPago = ResolveFormaPago(formasPago, draft.FormaPago);
        if (formaPago is null)
            return Fail("No se encontró una forma de pago válida para la emisión.");

        var numeroFactura = await _facturacionService.GetNextFacturaNumeroAsync(userId);
        var fechaEmision = DateTime.Now;
        var esCredito = IsCreditPayment(formaPago, draft.FormaPago);
        var diasCredito = esCredito
            ? Math.Max(1, draft.DiasCredito.GetValueOrDefault() > 0 ? draft.DiasCredito!.Value : 30)
            : 0;
        var fechaVencimiento = esCredito
            ? (draft.FechaVencimiento?.Date ?? fechaEmision.Date.AddDays(diasCredito))
            : fechaEmision;

        var serviciosManualesPendientes = new Dictionary<FacturaItemDraftDto, Producto>();
        foreach (var item in draft.Items)
        {
            if (item.Cantidad <= 0)
                return Fail($"La cantidad de '{item.Descripcion}' debe ser mayor a cero.");

            if (!item.ProductoId.HasValue && item.EsServicioManual)
            {
                var servicio = new Producto
                {
                    Nombre = item.Descripcion,
                    CodigoPrincipal = $"SRV-IA-{DateTime.UtcNow:yyyyMMddHHmmss}",
                    ValorUnitario = item.PrecioUnitario,
                    Idusuario = ownerId,
                    Estado = true,
                    Tipocompravena = "SERVICIO",
                    Codigoimpuesto = TaxRateHelper.ResolveSriTaxCode(item.TarifaPorcentaje),
                    Porcentajeimpuesto = TaxRateHelper.NormalizePercentInt(item.TarifaPorcentaje).ToString("0")
                };

                context.Productos.Add(servicio);
                serviciosManualesPendientes[item] = servicio;
            }
            else if (!item.ProductoId.HasValue || item.ProductoId.Value <= 0)
            {
                return Fail($"El ítem '{item.Descripcion}' no tiene un producto válido para emitir.");
            }
        }

        if (serviciosManualesPendientes.Count > 0)
            await context.SaveChangesAsync(cancellationToken);

        var detalles = new List<Detallefactura>(draft.Items.Count);
        foreach (var item in draft.Items)
        {
            var productoId = item.ProductoId;
            if ((!productoId.HasValue || productoId.Value <= 0) && serviciosManualesPendientes.TryGetValue(item, out var servicioManual))
                productoId = servicioManual.Codigo;

            if (!productoId.HasValue || productoId.Value <= 0)
                return Fail($"El ítem '{item.Descripcion}' no tiene un producto válido para emitir.");

            detalles.Add(new Detallefactura
            {
                Codproducto = productoId.Value,
                Codprincipal = item.CodigoPrincipal,
                Cantproducto = item.Cantidad,
                Descripproducto = item.Descripcion,
                Precioproducto = decimal.Round(item.PrecioUnitario, 2),
                Descuento = decimal.Round(item.DescuentoAplicado, 2),
                Valortproducto = decimal.Round(item.Subtotal, 2),
                Valoriva = decimal.Round(item.Impuesto, 2),
                Valortotal = decimal.Round(item.Total, 2),
                Tarifa = TaxRateHelper.NormalizePercentInt(item.TarifaPorcentaje)
            });
        }

        var factura = new Factura
        {
            Codclientes = cliente.Codcliente,
            Codemisor = emisor.Codigo,
            Coddocumento = 1,
            Tipodocumento = 1,
            Numfactura = numeroFactura,
            Fechaentrega = fechaEmision,
            Fechavence = fechaVencimiento,
            Subtotal = decimal.Round(draft.Subtotal, 2),
            Descuentos = decimal.Round(draft.Descuento, 2),
            Iva = decimal.Round(draft.Impuesto, 2),
            Valortotal = decimal.Round(draft.Total, 2),
            Tipopago = formaPago.Codigo,
            Estado = true,
            Autorizado = false,
            Idusuario = userId,
            Nombread = draft.Cliente.Nombre,
            Correoad = draft.Cliente.Correo,
            Direccionad = draft.Cliente.Direccion,
            DescuentoGlobalPct = draft.DescuentoGlobalPorcentaje,
            DescuentoGlobalValor = draft.DescuentoGlobalValor,
            Tiempocredito = esCredito ? diasCredito : null,
            Valorapagar = decimal.Round(draft.Total, 2),
            Estadopago = esCredito ? "PENDIENTE" : "PAGADO",
            Subtotal0 = decimal.Round(draft.Items.Where(x => x.TarifaPorcentaje <= 0).Sum(x => x.Subtotal), 2),
            Subtotal12 = decimal.Round(draft.Items.Where(x => x.TarifaPorcentaje > 0).Sum(x => x.Subtotal), 2)
        };

        var ok = await _facturacionService.GuardarFacturaCompletaAsync(userId, factura, cliente, detalles);
        if (!ok)
            return Fail(_facturacionService.UltimoErrorGuardarFactura ?? "No se pudo emitir la factura con el servicio actual.");

        var resultadoSri = await _facturacionService.ReintentarEnvioSriFacturaAsync(factura.Codfactura);
        if (string.Equals(resultadoSri.estado, DocumentoAutorizacionHelper.EstadoAutorizado, StringComparison.OrdinalIgnoreCase))
        {
            var resultadoCorreo = await _facturacionService.IntentarEnviarFacturaPorCorreoAsync(factura.Codfactura, m: resultadoSri);
            var mensajeCorreo = resultadoCorreo.Enviado || resultadoCorreo.YaEnviado
                ? string.Empty
                : $" {resultadoCorreo.Mensaje}".TrimEnd();

            return new FacturaEmissionResult
            {
                Success = true,
                Message = $"Factura emitida y autorizada correctamente con numero {numeroFactura}.{mensajeCorreo}",
                NumeroFactura = numeroFactura
            };
        }

        return new FacturaEmissionResult
        {
            Success = true,
            Message = $"Factura emitida con numero {numeroFactura}, pero quedo pendiente/no autorizada en SRI. {BuildSriMessage(resultadoSri)}",
            NumeroFactura = numeroFactura
        };

    }

    public async Task<NotaCreditoEmissionResult> EmitirNotaCreditoAsync(int userId, int facturaId, string? motivo = null, CancellationToken cancellationToken = default)
    {
        await using var context = await _dbFactory.CreateDbContextAsync(cancellationToken);
        var ownerId = await ResolveOwnerIdAsync(context, userId, cancellationToken);
        if (ownerId <= 0)
        {
            return new NotaCreditoEmissionResult
            {
                Success = false,
                Message = "No fue posible resolver el usuario propietario de la factura."
            };
        }

        var resultado = await _notaCreditoService.EmitirNotaCreditoAutomaticaDesdeFacturaAsync(ownerId, facturaId, motivo);
        return new NotaCreditoEmissionResult
        {
            Success = resultado.Success,
            Autorizada = resultado.Autorizada,
            Message = resultado.Message,
            NumeroNotaCredito = string.IsNullOrWhiteSpace(resultado.NumeroNotaCredito) ? null : resultado.NumeroNotaCredito
        };
    }

    private static FacturaEmissionResult Fail(string message) => new()
    {
        Success = false,
        Message = message
    };

    private static string BuildSriMessage(Simetric.Models.Glogales.mensajeSRI? resultadoSri)
    {
        if (resultadoSri is null)
            return "No se recibio respuesta del SRI.";

        if (!string.IsNullOrWhiteSpace(resultadoSri.mensaje))
            return resultadoSri.mensaje.Trim();

        if (!string.IsNullOrWhiteSpace(resultadoSri.xml))
            return resultadoSri.xml.Trim();

        if (!string.IsNullOrWhiteSpace(resultadoSri.estado))
            return $"Estado SRI: {resultadoSri.estado.Trim()}.";

        return "No se recibio respuesta del SRI.";
    }

    private static FormasPago? ResolveFormaPago(IEnumerable<FormasPago> formasPago, string? requested)
    {
        if (!string.IsNullOrWhiteSpace(requested))
        {
            var normalized = NormalizeText(requested);
            if (normalized.Contains("credit", StringComparison.OrdinalIgnoreCase))
            {
                var credito = formasPago.FirstOrDefault(x => x.Codigo == "19")
                    ?? formasPago.FirstOrDefault(x => NormalizeText(x.Descripcion).Contains("credit", StringComparison.OrdinalIgnoreCase))
                    ?? formasPago.FirstOrDefault(x => NormalizeText(x.DescripcionSri).Contains("credit", StringComparison.OrdinalIgnoreCase));
                if (credito is not null)
                    return credito;
            }

            if (normalized.Contains("efectivo", StringComparison.OrdinalIgnoreCase) || normalized.Contains("contado", StringComparison.OrdinalIgnoreCase))
            {
                var efectivo = formasPago.FirstOrDefault(x => x.Codigo == "01")
                    ?? formasPago.FirstOrDefault(x => NormalizeText(x.Descripcion).Contains("efectivo", StringComparison.OrdinalIgnoreCase))
                    ?? formasPago.FirstOrDefault(x => NormalizeText(x.DescripcionSri).Contains("efectivo", StringComparison.OrdinalIgnoreCase));
                if (efectivo is not null)
                    return efectivo;
            }

            var match = formasPago.FirstOrDefault(x =>
                string.Equals((x.Descripcion ?? string.Empty).Trim(), requested.Trim(), StringComparison.OrdinalIgnoreCase) ||
                string.Equals((x.DescripcionSri ?? string.Empty).Trim(), requested.Trim(), StringComparison.OrdinalIgnoreCase) ||
                NormalizeText(x.Descripcion).Contains(normalized, StringComparison.OrdinalIgnoreCase) ||
                NormalizeText(x.DescripcionSri).Contains(normalized, StringComparison.OrdinalIgnoreCase));

            if (match is not null)
                return match;
        }

        return formasPago.FirstOrDefault(x => x.Codigo == "01")
            ?? formasPago.FirstOrDefault(x =>
            string.Equals((x.Descripcion ?? string.Empty).Trim(), "EFECTIVO", StringComparison.OrdinalIgnoreCase))
            ?? formasPago.FirstOrDefault();
    }

    private static bool IsCreditPayment(FormasPago formaPago, string? requested)
        => formaPago.Codigo == "19" || NormalizeText(requested).Contains("credit", StringComparison.OrdinalIgnoreCase);

    private static string NormalizeText(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return string.Empty;

        return value
            .Trim()
            .Replace("á", "a", StringComparison.OrdinalIgnoreCase)
            .Replace("é", "e", StringComparison.OrdinalIgnoreCase)
            .Replace("í", "i", StringComparison.OrdinalIgnoreCase)
            .Replace("ó", "o", StringComparison.OrdinalIgnoreCase)
            .Replace("ú", "u", StringComparison.OrdinalIgnoreCase)
            .ToLowerInvariant();
    }

    private static async Task<int> ResolveOwnerIdAsync(AppDbContext context, int userId, CancellationToken cancellationToken)
    {
        if (TryGetValidCacheValue(OwnerCache, userId, out var cachedOwnerId))
            return cachedOwnerId;

        var user = await context.Usuarios
            .AsNoTracking()
            .Where(u => u.IdUsuario == userId)
            .Select(u => new { u.IdUsuario, u.idJefe })
            .FirstOrDefaultAsync(cancellationToken);

        var ownerId = user?.idJefe ?? user?.IdUsuario ?? 0;
        OwnerCache[userId] = new CachedValue<int>(ownerId, DateTimeOffset.UtcNow.Add(OwnerCacheLifetime));
        return ownerId;
    }

    private static bool TryGetValidCacheValue<T>(ConcurrentDictionary<int, CachedValue<T>> cache, int key, out T value)
    {
        if (cache.TryGetValue(key, out var entry) && entry.ExpiresAt > DateTimeOffset.UtcNow)
        {
            value = entry.Value;
            return true;
        }

        value = default!;
        return false;
    }

    private readonly record struct CachedValue<T>(T Value, DateTimeOffset ExpiresAt);
}
