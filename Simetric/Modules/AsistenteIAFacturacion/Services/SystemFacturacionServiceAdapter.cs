using Microsoft.AspNetCore.Hosting;
using Microsoft.EntityFrameworkCore;
using Simetric.Data;
using Simetric.DTOs;
using Simetric.Models;
using Simetric.Modules.AsistenteIAFacturacion.DTOs;
using Simetric.Services;
using Simetric.Services.ESign;
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
    private readonly NotaDebitoService _notaDebitoService;
    private readonly GuiaRemisionService _guiaRemisionService;
    private readonly LiquidacionCompraService _liquidacionCompraService;
    private readonly RetencionGeneradaService _retencionGeneradaService;
    private readonly ICajaSerieResolver _cajaSerieResolver;
    private readonly FirmaConfiguracionService _firmaConfiguracionService;
    private readonly FirmaPathResolver _firmaPathResolver;
    private readonly SolicitudService _solicitudService;
    private readonly UanatacaApiService _uanatacaApiService;
    private readonly IWebHostEnvironment _environment;

    public SystemFacturacionServiceAdapter(
        IDbContextFactory<AppDbContext> dbFactory,
        Simetric.Services.FacturacionService facturacionService,
        NotaCreditoService notaCreditoService,
        NotaDebitoService notaDebitoService,
        GuiaRemisionService guiaRemisionService,
        LiquidacionCompraService liquidacionCompraService,
        RetencionGeneradaService retencionGeneradaService,
        ICajaSerieResolver cajaSerieResolver,
        FirmaConfiguracionService firmaConfiguracionService,
        FirmaPathResolver firmaPathResolver,
        SolicitudService solicitudService,
        UanatacaApiService uanatacaApiService,
        IWebHostEnvironment environment)
    {
        _dbFactory = dbFactory;
        _facturacionService = facturacionService;
        _notaCreditoService = notaCreditoService;
        _notaDebitoService = notaDebitoService;
        _guiaRemisionService = guiaRemisionService;
        _liquidacionCompraService = liquidacionCompraService;
        _retencionGeneradaService = retencionGeneradaService;
        _cajaSerieResolver = cajaSerieResolver;
        _firmaConfiguracionService = firmaConfiguracionService;
        _firmaPathResolver = firmaPathResolver;
        _solicitudService = solicitudService;
        _uanatacaApiService = uanatacaApiService;
        _environment = environment;
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

    public async Task<IReadOnlyList<FacturaListDto>> ListarFacturasUsuarioAsync(
        int userId,
        int top = 200,
        CancellationToken cancellationToken = default)
        => await _facturacionService.ListarFacturasUsuarioAsync(userId, top);

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

    public async Task<FacturaEmissionResult> EmitirAsync(int userId, FacturaDraftDto draft, string? requestId = null, CancellationToken cancellationToken = default)
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

        var ok = await _facturacionService.GuardarFacturaCompletaAsync(userId, factura, cliente, detalles, requestId: requestId);
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

    public async Task<NotaDebitoEmissionResult> EmitirNotaDebitoAsync(
        int userId,
        string referenciaFactura,
        string motivo,
        decimal valor,
        decimal tarifaPorcentaje = 0,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(referenciaFactura))
            return FailNotaDebito("Indica el número de la factura autorizada de origen.");
        if (string.IsNullOrWhiteSpace(motivo))
            return FailNotaDebito("Indica el motivo de la nota de débito.");
        if (valor <= 0)
            return FailNotaDebito("El valor de la nota de débito debe ser mayor a cero.");
        if (tarifaPorcentaje is not (0m or 5m or 8m or 12m or 13m or 14m or 15m))
            return FailNotaDebito("La tarifa de IVA debe ser 0, 5, 8, 12, 13, 14 o 15 por ciento.");

        await using var context = await _dbFactory.CreateDbContextAsync(cancellationToken);
        var ownerId = await ResolveOwnerIdAsync(context, userId, cancellationToken);
        if (ownerId <= 0)
            return FailNotaDebito("No fue posible resolver el usuario propietario de la cuenta.");

        var facturas = await _notaDebitoService.BuscarFacturasAutocompleteAsync(referenciaFactura.Trim(), ownerId);
        if (facturas.Count == 0)
            return FailNotaDebito($"No encontré una factura autorizada para '{referenciaFactura}'.");
        if (facturas.Count > 1)
            return FailNotaDebito($"Encontré varias facturas para '{referenciaFactura}'. Indica el número exacto.");

        var factura = await context.Facturas
            .AsNoTracking()
            .FirstOrDefaultAsync(x => x.Codfactura == facturas[0].Codfactura && x.Idusuario == ownerId, cancellationToken);
        if (factura is null)
            return FailNotaDebito("La factura seleccionada ya no existe en la cuenta actual.");
        if (!DocumentoAutorizacionHelper.EstaAutorizado(factura.Autorizado, factura.Estadoenviosri))
            return FailNotaDebito("La factura de origen debe estar autorizada por el SRI.");

        var resolucion = await _cajaSerieResolver.ResolverNotaDebitoAsync(ownerId);
        var secuencial = await _facturacionService.GetNextSecuencialNotaDebitoAsync(ownerId, resolucion.SerieRaw);
        var detalle = new NotaDebitoService.DetalleNdDto
        {
            Codproducto = 0,
            Descripcion = motivo.Trim(),
            Cantidad = 1m,
            Preciounitario = decimal.Round(valor, 2),
            Iva = (int)tarifaPorcentaje,
            CodigoImp = 2,
            CodigoPorcentajeSri = ResolveSriTaxCode(tarifaPorcentaje)
        };

        var nota = new NotaDebito
        {
            CodClientes = factura.Codclientes,
            CodEmisor = factura.Codemisor,
            Usuario = ownerId,
            Serie = resolucion.SerieRaw,
            NumNotaDebito = secuencial,
            CodDocModificado = "01",
            NumDocModificado = factura.Numfactura,
            IdDocModificado = factura.Codfactura,
            FechaEmiDocModificado = factura.Fechaentrega ?? DateTime.Today
        };

        try
        {
            var sec = await _notaDebitoService.CrearAsync(nota, new List<NotaDebitoService.DetalleNdDto> { detalle });
            var resultadoSri = await _notaDebitoService.EmitirNotaDebitoSriAsync(sec, ownerId);
            var autorizada = string.Equals(resultadoSri.estado, DocumentoAutorizacionHelper.EstadoAutorizado, StringComparison.OrdinalIgnoreCase);
            var mensaje = string.IsNullOrWhiteSpace(resultadoSri.mensaje)
                ? (autorizada ? "Nota de débito emitida y autorizada correctamente." : "Nota de débito emitida, pero quedó pendiente/no autorizada en SRI.")
                : resultadoSri.mensaje.Trim();

            return new NotaDebitoEmissionResult
            {
                Success = true,
                Autorizada = autorizada,
                Message = $"{mensaje} Número: {nota.Serie}-{nota.NumNotaDebito}.",
                NumeroNotaDebito = $"{nota.Serie}-{nota.NumNotaDebito}"
            };
        }
        catch (Exception ex)
        {
            return FailNotaDebito(ex.Message);
        }
    }

    public async Task<GuiaEmissionResult> EmitirGuiaDesdeFacturaAsync(
        int userId,
        string referenciaFactura,
        string transportistaIdentificacion,
        string transportistaRazonSocial,
        string placa,
        string direccionPartida,
        string destinatarioIdentificacion,
        string destinatarioRazonSocial,
        string direccionDestino,
        string motivoTraslado = "VENTA",
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(referenciaFactura))
            return FailGuia("Indica el número de la factura autorizada que se trasladará.");
        if (string.IsNullOrWhiteSpace(transportistaIdentificacion) || string.IsNullOrWhiteSpace(transportistaRazonSocial) || string.IsNullOrWhiteSpace(placa))
            return FailGuia("Indica identificación, razón social y placa del transportista.");
        if (string.IsNullOrWhiteSpace(direccionPartida))
            return FailGuia("Indica la dirección de partida.");
        if (string.IsNullOrWhiteSpace(destinatarioIdentificacion) || string.IsNullOrWhiteSpace(destinatarioRazonSocial) || string.IsNullOrWhiteSpace(direccionDestino))
            return FailGuia("Indica identificación, razón social y dirección del destinatario.");

        await using var context = await _dbFactory.CreateDbContextAsync(cancellationToken);
        var ownerId = await ResolveOwnerIdAsync(context, userId, cancellationToken);
        if (ownerId <= 0)
            return FailGuia("No fue posible resolver el usuario propietario de la cuenta.");

        var facturas = await _guiaRemisionService.BuscarFacturasDisponiblesAsync(ownerId, referenciaFactura.Trim());
        if (facturas.Count == 0)
            return FailGuia($"No encontré una factura disponible para '{referenciaFactura}'.");
        if (facturas.Count > 1)
            return FailGuia($"Encontré varias facturas para '{referenciaFactura}'. Indica el número exacto.");

        var factura = await context.Facturas
            .AsNoTracking()
            .FirstOrDefaultAsync(x => x.Codfactura == facturas[0].Codfactura && x.Idusuario == ownerId, cancellationToken);
        if (factura is null)
            return FailGuia("La factura seleccionada ya no existe en la cuenta actual.");
        if (!DocumentoAutorizacionHelper.EstaAutorizado(factura.Autorizado, factura.Estadoenviosri))
            return FailGuia("La factura de origen debe estar autorizada por el SRI.");

        var detallesFactura = await context.Detallefacturas
            .AsNoTracking()
            .Where(x => x.Codfactura == factura.Codfactura)
            .OrderBy(x => x.Codlinea)
            .ToListAsync(cancellationToken);
        if (detallesFactura.Count == 0)
            return FailGuia("La factura seleccionada no tiene productos para trasladar.");

        var detalles = detallesFactura.Select(x => new DetalleGuiaRemision
        {
            CodInterno = x.Codprincipal,
            CodAdicional = x.Codauxiliar,
            Descripcion = x.Descripproducto,
            Cantidad = Math.Max(1, (int)Math.Ceiling(x.Cantproducto))
        }).ToList();

        var hoy = DateTime.Today;
        var transportista = new Transportista
        {
            TipoIdentificacion = ResolveTransportistaIdentificationType(transportistaIdentificacion),
            NumeroIdentificacion = transportistaIdentificacion.Trim(),
            RazonSocial = transportistaRazonSocial.Trim(),
            Placa = placa.Trim(),
            OblCont = "NO"
        };
        var guia = new GuiaRemision
        {
            Fecha = hoy,
            FechaIniTransporte = hoy,
            FechaFinTransporte = hoy,
            Placa = placa.Trim(),
            DireccionPartida = direccionPartida.Trim()
        };
        var destinatario = new GuiaDestinatario
        {
            IdDestinatario = destinatarioIdentificacion.Trim(),
            RazonSocial = destinatarioRazonSocial.Trim(),
            Direccion = direccionDestino.Trim(),
            MotivoTraslado = string.IsNullOrWhiteSpace(motivoTraslado) ? "VENTA" : motivoTraslado.Trim(),
            FechaEmiSustento = factura.Fechaentrega ?? hoy
        };

        try
        {
            var guardado = await _guiaRemisionService.GuardarGuiaRemisionCompletaAsync(
                ownerId,
                factura.Codfactura,
                factura.Codemisor,
                transportista,
                guia,
                destinatario,
                detalles);
            var resultadoSri = await _guiaRemisionService.EmitirGuiaRemisionSriAsync(guardado.SecGuiaRemision, ownerId);
            var autorizada = string.Equals(resultadoSri.estado, DocumentoAutorizacionHelper.EstadoAutorizado, StringComparison.OrdinalIgnoreCase);
            var mensaje = string.IsNullOrWhiteSpace(resultadoSri.mensaje)
                ? (autorizada ? "Guía de remisión emitida y autorizada correctamente." : "Guía de remisión emitida, pero quedó pendiente/no autorizada en SRI.")
                : resultadoSri.mensaje.Trim();

            return new GuiaEmissionResult
            {
                Success = true,
                Autorizada = autorizada,
                Message = $"{mensaje} Número: {guardado.NumeroCompleto}.",
                NumeroGuia = guardado.NumeroCompleto
            };
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            return FailGuia(ex.Message);
        }
    }

    public async Task<LiquidacionEmissionResult> EmitirLiquidacionCompraAsync(
        int userId,
        string tipoIdentificacionProveedor,
        string identificacionProveedor,
        string razonSocialProveedor,
        string direccionProveedor,
        string descripcion,
        decimal cantidad,
        decimal precioUnitario,
        decimal tarifaPorcentaje,
        string formaPago,
        string? emailProveedor = null,
        int? plazo = null,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(identificacionProveedor) || string.IsNullOrWhiteSpace(razonSocialProveedor))
            return FailLiquidacion("Indica la identificación y razón social del proveedor.");
        if (string.IsNullOrWhiteSpace(direccionProveedor))
            return FailLiquidacion("Indica la dirección del proveedor.");
        if (string.IsNullOrWhiteSpace(descripcion) || cantidad <= 0m || precioUnitario <= 0m)
            return FailLiquidacion("Indica una descripción, cantidad y precio unitario válidos.");
        if (tarifaPorcentaje is not (0m or 5m or 8m or 15m))
            return FailLiquidacion("La liquidación admite IVA 0%, 5%, 8% o 15%.");

        cancellationToken.ThrowIfCancellationRequested();
        var tipoIdentificacion = ResolveLiquidacionIdentificationType(tipoIdentificacionProveedor, identificacionProveedor);

        try
        {
            var formasPago = await _liquidacionCompraService.ObtenerFormasPagoCompraAsync();
            var formaPagoCodigo = ResolveLiquidacionPaymentCode(formasPago, formaPago);
            if (string.IsNullOrWhiteSpace(formaPagoCodigo))
                return FailLiquidacion("No reconocí la forma de pago indicada. Usa el código o nombre del catálogo de compras.");

            var preview = await _liquidacionCompraService.CrearPreviewManualAsync(userId);
            preview.TipoIdentificacionProveedor = tipoIdentificacion;
            preview.IdentificacionProveedor = identificacionProveedor.Trim();
            preview.RazonSocialProveedor = razonSocialProveedor.Trim();
            preview.DireccionProveedor = direccionProveedor.Trim();
            preview.EmailProveedor = emailProveedor?.Trim() ?? "";
            preview.FormaPago = formaPagoCodigo;
            preview.Plazo = plazo;
            preview.Detalles = new List<LiquidacionCompraDetalleDto>
            {
                new()
                {
                    Descripcion = descripcion.Trim(),
                    Cantidad = cantidad,
                    PrecioUnitario = precioUnitario,
                    CodigoPorcentaje = ResolveLiquidacionTaxCode(tarifaPorcentaje),
                    Tarifa = (int)tarifaPorcentaje
                }
            };

            await _liquidacionCompraService.ResolverDatosManualAsync(preview);
            var guardado = await _liquidacionCompraService.GuardarLiquidacionConArchivosAsync(preview);
            var resultadoSri = await _liquidacionCompraService.EmitirLiquidacionSriAsync(guardado.CodFactura, userId);
            var autorizada = string.Equals(resultadoSri.estado, DocumentoAutorizacionHelper.EstadoAutorizado, StringComparison.OrdinalIgnoreCase);
            var mensajeSri = string.IsNullOrWhiteSpace(resultadoSri.mensaje)
                ? (autorizada ? "Liquidación de compra emitida y autorizada correctamente." : "Liquidación de compra guardada, pero quedó pendiente/no autorizada en SRI.")
                : resultadoSri.mensaje.Trim();

            var numero = $"{preview.SerieVisual}-{preview.Secuencial}";
            var archivos = guardado.ProblemasArchivos.Count == 0
                ? " XML y PDF generados."
                : $" Archivos: {string.Join(" ", guardado.ProblemasArchivos)}";

            return new LiquidacionEmissionResult
            {
                Success = true,
                Autorizada = autorizada,
                Message = $"{mensajeSri} Número: {numero}.{archivos}",
                NumeroLiquidacion = numero,
                XmlUrl = guardado.XmlUrl,
                PdfUrl = guardado.PdfUrl
            };
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            return FailLiquidacion(ex.Message);
        }
    }

    private static GuiaEmissionResult FailGuia(string message) => new()
    {
        Success = false,
        Message = message
    };

    private static string ResolveTransportistaIdentificationType(string identification)
    {
        var digits = new string(identification.Where(char.IsDigit).ToArray());
        return digits.Length switch
        {
            13 => "04",
            10 => "05",
            _ => "06"
        };
    }

    public async Task<RetencionEmissionResult> EmitirRetencionAsync(
        int userId,
        string referenciaRetencion,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(referenciaRetencion))
            return FailRetencion("Indica el número o referencia de la retención pendiente.");

        cancellationToken.ThrowIfCancellationRequested();

        try
        {
            await using var context = await _dbFactory.CreateDbContextAsync(cancellationToken);
            var ownerId = await ResolveOwnerIdAsync(context, userId, cancellationToken);
            if (ownerId <= 0)
                return FailRetencion("No fue posible resolver el usuario propietario de la cuenta.");

            var referencia = referenciaRetencion.Trim();
            var digitos = new string(referencia.Where(char.IsDigit).ToArray());
            var numeroNormalizado = digitos.Length > 9 ? digitos[^9..] : digitos.PadLeft(9, '0');
            var sec = int.TryParse(referencia, out var secDirecto) ? secDirecto : 0;

            var consultaPorNumero = context.RetencionInfo
                .AsNoTracking()
                .Where(x => x.Usuario == ownerId &&
                    (x.NumRetencion == referencia ||
                     x.NumRetencion == numeroNormalizado ||
                     x.Clave == referencia));
            var candidatos = await consultaPorNumero
                .OrderByDescending(x => x.Sec)
                .Take(5)
                .ToListAsync(cancellationToken);

            if (candidatos.Count == 0 && sec > 0)
            {
                candidatos = await context.RetencionInfo
                    .AsNoTracking()
                    .Where(x => x.Usuario == ownerId && x.Sec == sec)
                    .Take(1)
                    .ToListAsync(cancellationToken);
            }

            if (candidatos.Count == 0)
                return FailRetencion($"No encontré una retención pendiente para '{referenciaRetencion}'.");
            if (candidatos.Count > 1)
                return FailRetencion("Encontré varias retenciones con esa referencia. Indica el número exacto o la serie completa.");

            var retencion = candidatos[0];
            var resultadoSri = await _retencionGeneradaService.EmitirRetencionSriAsync(
                retencion.Sec,
                ownerId,
                intentarEnviarCorreo: true);
            var autorizada = string.Equals(
                resultadoSri.estado,
                DocumentoAutorizacionHelper.EstadoAutorizado,
                StringComparison.OrdinalIgnoreCase);
            var errorSri = string.Equals(resultadoSri.estado, "ERROR", StringComparison.OrdinalIgnoreCase)
                || (resultadoSri.estado?.Contains("ERROR", StringComparison.OrdinalIgnoreCase) ?? false);
            var mensaje = string.IsNullOrWhiteSpace(resultadoSri.mensaje)
                ? (autorizada ? "Retención emitida y autorizada correctamente." : "Retención procesada, pero quedó pendiente/no autorizada en SRI.")
                : resultadoSri.mensaje.Trim();
            var numeroDocumento = new string((retencion.NumRetencion ?? "").Where(char.IsDigit).ToArray()).PadLeft(9, '0');
            var serie = (retencion.Serie ?? "").Replace("-", "").Trim();
            var serieVisual = serie.Length == 6 ? $"{serie[..3]}-{serie[3..]}" : serie;
            var numero = string.IsNullOrWhiteSpace(serieVisual) ? numeroDocumento : $"{serieVisual}-{numeroDocumento}";
            var xmlUrl = await _retencionGeneradaService.AsegurarXmlRetencionUsuarioAsync(retencion.Sec, ownerId);
            var pdfUrl = await _retencionGeneradaService.AsegurarPdfRetencionUsuarioAsync(retencion.Sec, ownerId);

            return new RetencionEmissionResult
            {
                Success = !errorSri,
                Autorizada = autorizada,
                Message = $"{mensaje} Número: {numero}.",
                NumeroRetencion = numero,
                XmlUrl = xmlUrl,
                PdfUrl = pdfUrl
            };
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            return FailRetencion(ex.Message);
        }
    }

    private static RetencionEmissionResult FailRetencion(string message) => new()
    {
        Success = false,
        Message = message
    };

    public async Task<IReadOnlyList<DocumentoConsultaDto>> ConsultarDocumentosAsync(
        int userId,
        string? tipo = null,
        string? filtro = null,
        string? periodo = null,
        int limite = 10,
        CancellationToken cancellationToken = default)
    {
        await using var context = await _dbFactory.CreateDbContextAsync(cancellationToken);
        var ownerId = await ResolveOwnerIdAsync(context, userId, cancellationToken);
        if (ownerId <= 0)
            return Array.Empty<DocumentoConsultaDto>();

        var tipoNormalizado = NormalizeText(tipo);
        var documentos = new List<DocumentoConsultaDto>();

        if (TipoSolicitado(tipoNormalizado, "factura", "facturas"))
        {
            var facturas = await _facturacionService.ListarFacturasUsuarioAsync(ownerId, 200);
            documentos.AddRange(facturas.Select(x => new DocumentoConsultaDto
            {
                Tipo = "Factura",
                Numero = x.NumeroCompleto,
                Fecha = x.FechaEmision,
                Tercero = x.Cliente ?? string.Empty,
                Identificacion = x.IdentificacionCliente ?? string.Empty,
                Estado = ResolverEstadoDocumento(x.Autorizado?.ToString(), x.EstadoSri),
                Total = x.Total ?? 0m,
                NumeroAutorizacion = x.NumeroAutorizacion ?? string.Empty,
                Mensaje = x.MensajeSri ?? string.Empty
            }));
        }

        if (TipoSolicitado(tipoNormalizado, "liquidacion", "liquidaciones", "compra"))
        {
            var liquidaciones = await _liquidacionCompraService.ListarLiquidacionesUsuarioAsync(ownerId);
            documentos.AddRange(liquidaciones.Select(x => new DocumentoConsultaDto
            {
                Tipo = "Liquidación de compra",
                Numero = x.NumeroDocumento,
                Fecha = x.FechaEmision,
                Tercero = x.Proveedor,
                Identificacion = x.IdentificacionProveedor,
                Estado = ResolverEstadoDocumento(x.Autorizado, x.EstadoSri),
                Total = x.ImporteTotal,
                NumeroAutorizacion = x.NumeroAutorizacion,
                Mensaje = x.MensajeSri,
                XmlUrl = x.XmlUrl,
                PdfUrl = x.PdfUrl
            }));
        }

        if (TipoSolicitado(tipoNormalizado, "retencion", "retenciones"))
        {
            var retenciones = await _retencionGeneradaService.ListarRetencionesUsuarioAsync(ownerId);
            documentos.AddRange(retenciones.Select(x => new DocumentoConsultaDto
            {
                Tipo = "Retención",
                Numero = x.NumeroCompleto,
                Fecha = x.Fecha,
                Tercero = x.Proveedor,
                Identificacion = x.IdentificacionProveedor,
                Estado = ResolverEstadoDocumento(x.Autorizado, x.Estado),
                Total = x.TotalRetenido,
                NumeroAutorizacion = x.NumeroAutorizacion,
                Mensaje = x.Mensaje,
                XmlUrl = x.XmlUrl
            }));
        }

        if (TipoSolicitado(tipoNormalizado, "guia", "guias", "remision"))
        {
            var guias = await _guiaRemisionService.ListarGuiasRemisionUsuarioAsync(ownerId);
            documentos.AddRange(guias.Select(x => new DocumentoConsultaDto
            {
                Tipo = "Guía de remisión",
                Numero = x.NumeroCompleto,
                Fecha = x.FechaEmision,
                Tercero = x.Destinatario,
                Identificacion = x.IdentificacionDestinatario,
                Estado = ResolverEstadoDocumento(null, x.EstadoSri),
                NumeroAutorizacion = x.NumeroAutorizacion,
                Mensaje = x.EstadoSri,
                XmlUrl = x.XmlUrl,
                PdfUrl = x.PdfUrl
            }));
        }

        if (TipoSolicitado(tipoNormalizado, "nota credito", "notas credito", "nota de credito", "notas de credito"))
        {
            var notasCredito = await _notaCreditoService.ListarNotasCreditoUsuarioAsync(ownerId);
            documentos.AddRange(notasCredito.Select(x => new DocumentoConsultaDto
            {
                Tipo = "Nota de crédito",
                Numero = x.NumeroCompleto,
                Fecha = x.FechaAutorizacion ?? x.FechaDocumentoModificado,
                Tercero = x.Cliente,
                Identificacion = x.IdentificacionCliente,
                Estado = ResolverEstadoDocumento(x.Autorizado, x.MensajeSri),
                Total = x.Total,
                NumeroAutorizacion = x.NumeroAutorizacion,
                Mensaje = x.MensajeSri,
                XmlUrl = x.XmlUrl
            }));
        }

        if (TipoSolicitado(tipoNormalizado, "nota debito", "notas debito", "nota de debito", "notas de debito"))
        {
            var notasDebito = await _notaDebitoService.ListarNotasDebitoUsuarioAsync(ownerId);
            documentos.AddRange(notasDebito.Select(x => new DocumentoConsultaDto
            {
                Tipo = "Nota de débito",
                Numero = x.NumeroCompleto,
                Fecha = x.FechaAutorizacion ?? x.FechaDocumentoModificado,
                Tercero = x.Cliente,
                Identificacion = x.IdentificacionCliente,
                Estado = ResolverEstadoDocumento(x.Autorizado, x.MensajeSri),
                Total = x.Total,
                NumeroAutorizacion = x.NumeroAutorizacion,
                Mensaje = x.MensajeSri,
                XmlUrl = x.XmlUrl
            }));
        }

        var textoFiltro = NormalizeText(filtro);
        var fechaDesde = ResolverFechaDesde(periodo);
        var maximo = Math.Clamp(limite, 1, 50);

        return documentos
            .Where(x => !fechaDesde.HasValue || (x.Fecha ?? DateTime.MinValue).Date >= fechaDesde.Value)
            .Where(x => string.IsNullOrWhiteSpace(textoFiltro) ||
                NormalizeText($"{x.Tipo} {x.Numero} {x.Tercero} {x.Identificacion} {x.Estado} {x.Mensaje}")
                    .Contains(textoFiltro, StringComparison.OrdinalIgnoreCase))
            .OrderByDescending(x => x.Fecha ?? DateTime.MinValue)
            .ThenByDescending(x => x.Numero)
            .Take(maximo)
            .ToList();
    }

    public async Task<ESignEstadoDto> ConsultarESignEstadoAsync(int userId, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var configuracion = await _firmaConfiguracionService.ObtenerAsync(userId);
        var tieneCertificado = configuracion is not null &&
            !string.IsNullOrWhiteSpace(configuracion.PathCertificado) &&
            _firmaPathResolver.ResolverRutaExistente(configuracion.PathCertificado) is not null;
        var tieneClave = configuracion?.TieneClaveCertificado == true;

        return new ESignEstadoDto
        {
            Configurado = tieneCertificado && tieneClave,
            TieneCertificado = tieneCertificado,
            TieneClave = tieneClave,
            EsConfiguracionCuenta = configuracion?.EsConfiguracionCuenta == true,
            Estado = tieneCertificado && tieneClave
                ? "Listo para firmar"
                : configuracion is null
                    ? "Sin configurar"
                    : "Configuración incompleta",
            Mensaje = tieneCertificado && tieneClave
                ? "Tu certificado está configurado y tiene una clave registrada."
                : "Falta configurar el certificado y/o su clave desde la configuración de firma."
        };
    }

    public async Task<IReadOnlyList<ESignSolicitudDto>> ConsultarESignSolicitudesAsync(
        int userId,
        string? filtro = null,
        string? estado = null,
        int limite = 10,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var solicitudes = await _solicitudService.ObtenerSolicitudesClienteAsync(userId);
        var filtroNormalizado = NormalizeText(filtro);
        var estadoNormalizado = NormalizeText(estado);
        var maximo = Math.Clamp(limite, 1, 50);

        return solicitudes
            .Where(x => string.IsNullOrWhiteSpace(filtroNormalizado) ||
                NormalizeText($"{x.SolId} {x.EstadoSolicitud} {x.SolFormatoFirma} {x.SolVigencia} {x.SolUanatacaStatus} {x.SolUanatacaStatusText}")
                    .Contains(filtroNormalizado, StringComparison.OrdinalIgnoreCase))
            .Where(x => string.IsNullOrWhiteSpace(estadoNormalizado) ||
                NormalizeText($"{x.EstadoSolicitud} {x.SolUanatacaStatus} {x.SolUanatacaStatusText}")
                    .Contains(estadoNormalizado, StringComparison.OrdinalIgnoreCase))
            .OrderByDescending(x => x.SolFechaActualizacion ?? x.SolFechaSolicitud)
            .Take(maximo)
            .Select(x => new ESignSolicitudDto
            {
                Id = x.SolId,
                Estado = x.EstadoSolicitud ?? x.SolUanatacaStatusText ?? x.SolUanatacaStatus ?? "Sin estado",
                FormatoFirma = x.SolFormatoFirma,
                Vigencia = x.SolVigencia,
                FechaSolicitud = x.SolFechaSolicitud,
                FechaAprobacion = x.SolFechaAprobacion,
                PagoExitoso = x.SolPagoExitoso,
                MontoPago = x.SolMontoPago,
                EstadoProveedor = x.SolUanatacaStatus,
                DetalleProveedor = x.SolUanatacaStatusText ?? x.SolUanatacaComments,
                ObservacionesPendientes = x.ObservacionesPendientes,
                TieneCertificadoEmitido = string.Equals(x.SolUanatacaStatus, "APPROVED", StringComparison.OrdinalIgnoreCase)
                    || string.Equals(x.SolUanatacaStatus, "ISSUED", StringComparison.OrdinalIgnoreCase)
            })
            .ToList();
    }

    public Task<IReadOnlyList<ESignDocumentoDto>> ConsultarESignDocumentosAsync(
        int userId,
        string? filtro = null,
        int limite = 10,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var webRoot = string.IsNullOrWhiteSpace(_environment.WebRootPath)
            ? Path.Combine(AppContext.BaseDirectory, "wwwroot")
            : _environment.WebRootPath;
        var directory = Path.Combine(webRoot, "uploads", "e-rubrica", "estampados", userId.ToString());
        if (!Directory.Exists(directory))
            return Task.FromResult<IReadOnlyList<ESignDocumentoDto>>(Array.Empty<ESignDocumentoDto>());

        var filtroNormalizado = NormalizeText(filtro);
        var maximo = Math.Clamp(limite, 1, 50);
        var documentos = Directory.EnumerateFiles(directory, "*.pdf", SearchOption.TopDirectoryOnly)
            .Select(path => new FileInfo(path))
            .Where(file => string.IsNullOrWhiteSpace(filtroNormalizado) ||
                NormalizeText(file.Name).Contains(filtroNormalizado, StringComparison.OrdinalIgnoreCase))
            .OrderByDescending(file => file.LastWriteTimeUtc)
            .Take(maximo)
            .Select(file => new ESignDocumentoDto
            {
                Nombre = file.Name,
                TamanoBytes = file.Length,
                FechaActualizacion = file.LastWriteTime,
                Url = $"/uploads/e-rubrica/estampados/{userId}/{Uri.EscapeDataString(file.Name)}"
            })
            .ToList();

        return Task.FromResult<IReadOnlyList<ESignDocumentoDto>>(documentos);
    }

    public async Task<ESignSincronizacionDto> SincronizarESignSolicitudAsync(
        int userId,
        int solicitudId,
        CancellationToken cancellationToken = default)
    {
        if (userId <= 0 || solicitudId <= 0)
        {
            return new ESignSincronizacionDto
            {
                Success = false,
                Message = "Indica una solicitud de E-Rúbrica válida."
            };
        }

        var resultado = await _solicitudService.SincronizarSolicitudClienteUanatacaAsync(
            solicitudId,
            userId,
            cancellationToken);
        return new ESignSincronizacionDto
        {
            Success = resultado.Success,
            Created = resultado.Created,
            Message = resultado.Message,
            EstadoProveedor = resultado.ProviderStatus
        };
    }

    public async Task<IReadOnlyList<ESignPlanDto>> ConsultarESignPlanesAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var productos = await _uanatacaApiService.ObtenerProductosAsync(cancellationToken);
        var productosStakeholder = await _uanatacaApiService.ObtenerProductosStakeholderAsync(cancellationToken: cancellationToken);
        var preciosPorProducto = productosStakeholder
            .Where(x => x.Active && !string.IsNullOrWhiteSpace(x.ProductUuid))
            .GroupBy(x => x.ProductUuid, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(x => x.Key, x => x.OrderBy(y => y.Price).First().Price, StringComparer.OrdinalIgnoreCase);

        return productos
            .Where(x => x.Active && preciosPorProducto.ContainsKey(x.Uuid))
            .Select(x => new ESignPlanDto
            {
                Nombre = x.Name,
                Codigo = x.Code,
                Precio = preciosPorProducto[x.Uuid] > 0 ? preciosPorProducto[x.Uuid] : x.Price
            })
            .OrderBy(x => x.Precio)
            .ThenBy(x => x.Nombre)
            .ToList();
    }

    private static bool TipoSolicitado(string tipo, params string[] opciones)
        => string.IsNullOrWhiteSpace(tipo)
            || tipo.Contains("todo", StringComparison.OrdinalIgnoreCase)
            || opciones.Any(tipo.Contains);

    private static DateTime? ResolverFechaDesde(string? periodo)
    {
        var valor = NormalizeText(periodo);
        if (valor.Contains("hoy", StringComparison.OrdinalIgnoreCase))
            return DateTime.Today;
        if (valor.Contains("mes pasado", StringComparison.OrdinalIgnoreCase))
            return new DateTime(DateTime.Today.Year, DateTime.Today.Month, 1).AddMonths(-1);
        if (valor.Contains("mes", StringComparison.OrdinalIgnoreCase))
            return new DateTime(DateTime.Today.Year, DateTime.Today.Month, 1);

        return null;
    }

    private static string ResolverEstadoDocumento(string? autorizado, string? estado)
    {
        if (DocumentoAutorizacionHelper.EstaAutorizado(autorizado, estado))
            return "AUTORIZADO";

        var valor = (estado ?? string.Empty).Trim().ToUpperInvariant();
        if (valor.Contains("ERROR") || valor.Contains("FALL"))
            return "ERROR";
        if (DocumentoAutorizacionHelper.EsNoAutorizado(valor))
            return "RECHAZADO";

        return "PENDIENTE";
    }

    private static LiquidacionEmissionResult FailLiquidacion(string message) => new()
    {
        Success = false,
        Message = message
    };

    private static string ResolveLiquidacionIdentificationType(string? requested, string identification)
    {
        var normalized = NormalizeText(requested);
        if (normalized is "ruc" or "04")
            return "04";
        if (normalized is "cedula" or "05")
            return "05";
        if (normalized is "pasaporte" or "06")
            return "06";
        if (normalized is "exterior" or "identificacion del exterior" or "08")
            return "08";

        var digits = new string((identification ?? "").Where(char.IsDigit).ToArray());
        return digits.Length == 13 ? "04" : digits.Length == 10 ? "05" : "06";
    }

    private static string? ResolveLiquidacionPaymentCode(IEnumerable<LiquidacionCatalogoOptionDto> formasPago, string? requested)
    {
        if (string.IsNullOrWhiteSpace(requested))
            return null;

        var value = requested.Trim();
        var normalized = NormalizeText(value);
        return formasPago.FirstOrDefault(x => string.Equals(x.Codigo?.Trim(), value, StringComparison.OrdinalIgnoreCase))?.Codigo
            ?? formasPago.FirstOrDefault(x => string.Equals(NormalizeText(x.Descripcion), normalized, StringComparison.OrdinalIgnoreCase))?.Codigo
            ?? formasPago.FirstOrDefault(x => NormalizeText(x.Descripcion).Contains(normalized, StringComparison.OrdinalIgnoreCase))?.Codigo;
    }

    private static int ResolveLiquidacionTaxCode(decimal tarifaPorcentaje)
        => tarifaPorcentaje switch
        {
            5m => 5,
            8m => 8,
            15m => 4,
            _ => 0
        };

    private static NotaDebitoEmissionResult FailNotaDebito(string message) => new()
    {
        Success = false,
        Message = message
    };

    private static string ResolveSriTaxCode(decimal tarifaPorcentaje)
        => tarifaPorcentaje switch
        {
            0m => "0",
            5m => "5",
            8m => "8",
            12m => "2",
            13m => "10",
            14m => "3",
            15m => "4",
            _ => "0"
        };

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
