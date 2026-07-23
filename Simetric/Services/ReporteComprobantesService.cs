using Microsoft.EntityFrameworkCore;
using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;
using Simetric.Data;
using Simetric.DTOs;
using Simetric.Models;
using System.Globalization;

namespace Simetric.Services;

public sealed class ReporteComprobantesService
{
    private readonly IDbContextFactory<AppDbContext> _dbFactory;
    private readonly FacturacionService _facturacionService;
    private readonly NotaCreditoService _notaCreditoService;
    private readonly NotaDebitoService _notaDebitoService;
    private readonly GuiaRemisionService _guiaRemisionService;
    private readonly RetencionGeneradaService _retencionGeneradaService;
    private readonly LiquidacionCompraService _liquidacionCompraService;

    public ReporteComprobantesService(
        IDbContextFactory<AppDbContext> dbFactory,
        FacturacionService facturacionService,
        NotaCreditoService notaCreditoService,
        NotaDebitoService notaDebitoService,
        GuiaRemisionService guiaRemisionService,
        RetencionGeneradaService retencionGeneradaService,
        LiquidacionCompraService liquidacionCompraService)
    {
        _dbFactory = dbFactory;
        _facturacionService = facturacionService;
        _notaCreditoService = notaCreditoService;
        _notaDebitoService = notaDebitoService;
        _guiaRemisionService = guiaRemisionService;
        _retencionGeneradaService = retencionGeneradaService;
        _liquidacionCompraService = liquidacionCompraService;
    }

    public async Task<ReporteComprobantesCargaDto> ObtenerReporteUsuarioAsync(int idUsuario)
    {
        if (idUsuario <= 0)
        {
            return new ReporteComprobantesCargaDto();
        }

        var facturasTask = _facturacionService.ListarFacturasUsuarioAsync(idUsuario, 0);
        var notasCreditoTask = _notaCreditoService.ListarNotasCreditoUsuarioAsync(idUsuario);
        var notasDebitoTask = _notaDebitoService.ListarNotasDebitoUsuarioAsync(idUsuario);
        var guiasTask = _guiaRemisionService.ListarGuiasRemisionUsuarioAsync(idUsuario);
        var retencionesTask = _retencionGeneradaService.ListarRetencionesUsuarioAsync(idUsuario);
        var liquidacionesTask = _liquidacionCompraService.ListarLiquidacionesUsuarioAsync(idUsuario);

        await Task.WhenAll(
            facturasTask,
            notasCreditoTask,
            notasDebitoTask,
            guiasTask,
            retencionesTask,
            liquidacionesTask);

        var facturas = facturasTask.Result;
        var notasCredito = notasCreditoTask.Result;
        var notasDebito = notasDebitoTask.Result;
        var guias = guiasTask.Result;
        var retenciones = retencionesTask.Result;
        var liquidaciones = liquidacionesTask.Result;

        await using var context = await _dbFactory.CreateDbContextAsync();

        var usuario = await context.Usuarios
            .AsNoTracking()
            .FirstOrDefaultAsync(x => x.IdUsuario == idUsuario);

        var idUsuarioEmisor = await ResolverUsuarioEmisorAsync(context, idUsuario);
        var emisorActivo = await context.Emisores
            .AsNoTracking()
            .Where(x => x.IdUsuario == idUsuarioEmisor && x.Estado)
            .OrderByDescending(x => x.Codigo)
            .FirstOrDefaultAsync();

        var facturaIds = facturas.Select(x => x.Codfactura).Distinct().ToArray();
        var notaCreditoIds = notasCredito.Select(x => x.Sec).Distinct().ToArray();
        var notaDebitoIds = notasDebito.Select(x => x.Sec).Distinct().ToArray();
        var guiaIds = guias.Select(x => x.Sec).Distinct().ToArray();
        var facturaHeaders = await CargarFacturasHeaderAsync(context, facturaIds);
        var notaCreditoHeaders = await CargarNotasCreditoHeaderAsync(context, notaCreditoIds);
        var notaDebitoHeaders = await CargarNotasDebitoHeaderAsync(context, notaDebitoIds);
        var retencionHeaders = await CargarRetencionesHeaderAsync(context, retenciones.Select(x => x.Sec).Distinct().ToArray());

        var facturaDetalles = await CargarDetallesFacturaAsync(context, facturaIds);
        var notaCreditoDetalles = await CargarDetallesNotaCreditoAsync(context, notaCreditoIds);
        var notaDebitoDetalles = await CargarDetallesNotaDebitoAsync(context, notaDebitoIds);
        var guiaDetalles = await CargarDetallesGuiaAsync(context, guiaIds);

        var comprasIds = liquidaciones.Select(x => x.CodFactura)
            .Concat(retencionHeaders.Values.Where(x => x.IdCompra.HasValue).Select(x => x.IdCompra!.Value))
            .Distinct()
            .ToArray();
        var compraDetalles = await CargarDetallesCompraAsync(context, comprasIds);

        var items = new List<ReporteComprobanteItemDto>();

        items.AddRange(facturas.Select(item =>
        {
            facturaHeaders.TryGetValue(item.Codfactura, out var header);
            facturaDetalles.TryGetValue(item.Codfactura, out var detalle);
            var estaAutorizado = DocumentoAutorizacionHelper.EstaAutorizado(item.Autorizado, item.EstadoSri);
            var baseSinIva = header?.Subtotal0
                ?? detalle?.BaseSinIva
                ?? ObtenerBaseSinIvaFallback(header?.Subtotal ?? 0m, header?.Iva ?? 0m);
            var baseConIva = header?.SubtotalConIva
                ?? detalle?.BaseConIva
                ?? ObtenerBaseConIvaFallback(header?.Subtotal ?? 0m, header?.Iva ?? 0m);

            return new ReporteComprobanteItemDto
            {
                DocumentoId = item.Codfactura,
                TipoDocumento = "Facturas",
                TipoDocumentoCodigo = ReporteComprobantesTipos.Factura,
                FechaEmision = item.FechaEmision,
                NumeroDocumento = item.NumeroCompleto,
                TerceroNombre = item.Cliente ?? "Cliente",
                TerceroIdentificacion = item.IdentificacionCliente ?? string.Empty,
                TerceroRol = "Cliente",
                EstadoDocumento = DocumentoAutorizacionHelper.ObtenerEstadoVisual(item.EstadoSri, estaAutorizado),
                BaseImponible = header?.Subtotal ?? 0m,
                Iva = header?.Iva ?? 0m,
                Total = item.Total ?? 0m,
                DocumentoRelacionado = string.Empty,
                ClaveAcceso = header?.ClaveAcceso ?? string.Empty,
                NumeroAutorizacion = item.NumeroAutorizacion ?? string.Empty,
                EstaAutorizado = estaAutorizado,
                XmlUrl = header?.XmlUrl ?? string.Empty,
                PdfUrl = header?.PdfUrl ?? string.Empty,
                CodigosRelacionados = detalle?.Codigos ?? new List<string>(),
                ProductosRelacionados = detalle?.Descripciones ?? new List<string>(),
                BaseSinIva = baseSinIva,
                BaseConIva = baseConIva,
                TieneProducto = detalle?.TieneProducto == true,
                TieneServicio = detalle?.TieneServicio == true
            };
        }));

        items.AddRange(notasCredito.Select(item =>
        {
            notaCreditoHeaders.TryGetValue(item.Sec, out var header);
            notaCreditoDetalles.TryGetValue(item.Sec, out var detalle);
            var estaAutorizado = DocumentoAutorizacionHelper.EstaAutorizado(item.Autorizado);

            return new ReporteComprobanteItemDto
            {
                DocumentoId = item.Sec,
                TipoDocumento = "Notas de credito",
                TipoDocumentoCodigo = ReporteComprobantesTipos.NotaCredito,
                FechaEmision = item.FechaDocumentoModificado,
                NumeroDocumento = item.NumeroCompleto,
                TerceroNombre = item.Cliente,
                TerceroIdentificacion = item.IdentificacionCliente,
                TerceroRol = "Cliente",
                EstadoDocumento = DocumentoAutorizacionHelper.ObtenerEstadoVisual(item.Autorizado, estaAutorizado),
                BaseImponible = item.Subtotal,
                Iva = item.Iva,
                Total = item.Total,
                DocumentoRelacionado = item.NumeroDocModificado,
                ClaveAcceso = item.ClaveAcceso,
                NumeroAutorizacion = item.NumeroAutorizacion,
                EstaAutorizado = estaAutorizado,
                XmlUrl = item.XmlUrl,
                PdfUrl = header?.PdfUrl ?? string.Empty,
                CodigosRelacionados = detalle?.Codigos ?? new List<string>(),
                ProductosRelacionados = detalle?.Descripciones ?? new List<string>(),
                BaseSinIva = detalle?.BaseSinIva ?? ObtenerBaseSinIvaFallback(item.Subtotal, item.Iva),
                BaseConIva = detalle?.BaseConIva ?? ObtenerBaseConIvaFallback(item.Subtotal, item.Iva),
                TieneProducto = detalle?.TieneProducto == true,
                TieneServicio = detalle?.TieneServicio == true
            };
        }));

        items.AddRange(notasDebito.Select(item =>
        {
            notaDebitoHeaders.TryGetValue(item.Sec, out var header);
            notaDebitoDetalles.TryGetValue(item.Sec, out var detalle);
            var estaAutorizado = DocumentoAutorizacionHelper.EstaAutorizado(item.Autorizado);

            return new ReporteComprobanteItemDto
            {
                DocumentoId = item.Sec,
                TipoDocumento = "Notas de debito",
                TipoDocumentoCodigo = ReporteComprobantesTipos.NotaDebito,
                FechaEmision = item.FechaDocumentoModificado,
                NumeroDocumento = item.NumeroCompleto,
                TerceroNombre = item.Cliente,
                TerceroIdentificacion = item.IdentificacionCliente,
                TerceroRol = "Cliente",
                EstadoDocumento = DocumentoAutorizacionHelper.ObtenerEstadoVisual(item.Autorizado, estaAutorizado),
                BaseImponible = item.Subtotal,
                Iva = item.Iva,
                Total = item.Total,
                DocumentoRelacionado = item.NumeroDocModificadoVisual,
                ClaveAcceso = header?.ClaveAcceso ?? string.Empty,
                NumeroAutorizacion = item.NumeroAutorizacion,
                EstaAutorizado = estaAutorizado,
                XmlUrl = item.XmlUrl,
                PdfUrl = header?.PdfUrl ?? string.Empty,
                CodigosRelacionados = detalle?.Codigos ?? new List<string>(),
                ProductosRelacionados = detalle?.Descripciones ?? new List<string>(),
                BaseSinIva = detalle?.BaseSinIva ?? ObtenerBaseSinIvaFallback(item.Subtotal, item.Iva),
                BaseConIva = detalle?.BaseConIva ?? ObtenerBaseConIvaFallback(item.Subtotal, item.Iva),
                TieneProducto = detalle?.TieneProducto == true,
                TieneServicio = detalle?.TieneServicio == true
            };
        }));

        items.AddRange(guias.Select(item =>
        {
            guiaDetalles.TryGetValue(item.Sec, out var detalle);
            var estaAutorizado = !string.IsNullOrWhiteSpace(item.NumeroAutorizacion)
                || DocumentoAutorizacionHelper.EsEstadoAutorizado(item.EstadoSri);

            return new ReporteComprobanteItemDto
            {
                DocumentoId = item.Sec,
                TipoDocumento = "Guias de remision",
                TipoDocumentoCodigo = ReporteComprobantesTipos.GuiaRemision,
                FechaEmision = item.FechaEmision,
                NumeroDocumento = item.NumeroCompleto,
                TerceroNombre = item.Destinatario,
                TerceroIdentificacion = item.IdentificacionDestinatario,
                TerceroRol = "Destinatario",
                EstadoDocumento = DocumentoAutorizacionHelper.ObtenerEstadoVisual(item.EstadoSri, estaAutorizado),
                BaseImponible = 0m,
                Iva = 0m,
                Total = 0m,
                DocumentoRelacionado = item.FacturaSustento,
                ClaveAcceso = item.ClaveAcceso,
                NumeroAutorizacion = item.NumeroAutorizacion,
                EstaAutorizado = estaAutorizado,
                XmlUrl = item.XmlUrl,
                PdfUrl = item.PdfUrl,
                CodigosRelacionados = detalle?.Codigos ?? new List<string>(),
                ProductosRelacionados = detalle?.Descripciones ?? new List<string>(),
                BaseSinIva = 0m,
                BaseConIva = 0m,
                TieneProducto = detalle?.TieneProducto == true,
                TieneServicio = detalle?.TieneServicio == true
            };
        }));

        items.AddRange(retenciones.Select(item =>
        {
            retencionHeaders.TryGetValue(item.Sec, out var header);
            var estaAutorizado = DocumentoAutorizacionHelper.EstaAutorizado(item.Autorizado, item.Estado);
            if (header?.IdCompra is > 0)
            {
                compraDetalles.TryGetValue(header.IdCompra.Value, out var detalle);

                return new ReporteComprobanteItemDto
                {
                    DocumentoId = item.Sec,
                    TipoDocumento = "Retenciones",
                    TipoDocumentoCodigo = ReporteComprobantesTipos.Retencion,
                    FechaEmision = item.Fecha,
                    NumeroDocumento = item.NumeroCompleto,
                    TerceroNombre = item.Proveedor,
                    TerceroIdentificacion = item.IdentificacionProveedor,
                    TerceroRol = "Proveedor",
                    EstadoDocumento = DocumentoAutorizacionHelper.ObtenerEstadoVisual(item.Estado, estaAutorizado),
                    BaseImponible = item.BaseTotal,
                    Iva = 0m,
                    Total = item.TotalRetenido,
                    DocumentoRelacionado = item.DocumentoSustento,
                    ClaveAcceso = item.Clave,
                    NumeroAutorizacion = item.NumeroAutorizacion,
                    EstaAutorizado = estaAutorizado,
                    XmlUrl = item.XmlUrl,
                    PdfUrl = header.PdfUrl,
                    CodigosRelacionados = detalle?.Codigos ?? new List<string>(),
                    ProductosRelacionados = detalle?.Descripciones ?? new List<string>(),
                    BaseSinIva = detalle?.BaseSinIva ?? ObtenerBaseSinIvaFallback(item.BaseTotal, 0m),
                    BaseConIva = detalle?.BaseConIva ?? ObtenerBaseConIvaFallback(item.BaseTotal, 0m),
                    TieneProducto = detalle?.TieneProducto == true,
                    TieneServicio = detalle?.TieneServicio == true
                };
            }

            return new ReporteComprobanteItemDto
            {
                DocumentoId = item.Sec,
                TipoDocumento = "Retenciones",
                TipoDocumentoCodigo = ReporteComprobantesTipos.Retencion,
                FechaEmision = item.Fecha,
                NumeroDocumento = item.NumeroCompleto,
                TerceroNombre = item.Proveedor,
                TerceroIdentificacion = item.IdentificacionProveedor,
                TerceroRol = "Proveedor",
                EstadoDocumento = DocumentoAutorizacionHelper.ObtenerEstadoVisual(item.Estado, estaAutorizado),
                BaseImponible = item.BaseTotal,
                Iva = 0m,
                Total = item.TotalRetenido,
                DocumentoRelacionado = item.DocumentoSustento,
                ClaveAcceso = item.Clave,
                NumeroAutorizacion = item.NumeroAutorizacion,
                EstaAutorizado = estaAutorizado,
                XmlUrl = item.XmlUrl,
                PdfUrl = header?.PdfUrl ?? string.Empty,
                BaseSinIva = ObtenerBaseSinIvaFallback(item.BaseTotal, 0m),
                BaseConIva = ObtenerBaseConIvaFallback(item.BaseTotal, 0m)
            };
        }));

        items.AddRange(liquidaciones.Select(item =>
        {
            compraDetalles.TryGetValue(item.CodFactura, out var detalle);
            var estaAutorizado = DocumentoAutorizacionHelper.EstaAutorizado(item.Autorizado, item.EstadoSri);

            return new ReporteComprobanteItemDto
            {
                DocumentoId = item.CodFactura,
                TipoDocumento = "Liquidaciones de compra",
                TipoDocumentoCodigo = ReporteComprobantesTipos.LiquidacionCompra,
                FechaEmision = item.FechaEmision,
                NumeroDocumento = item.NumeroDocumento,
                TerceroNombre = item.Proveedor,
                TerceroIdentificacion = item.IdentificacionProveedor,
                TerceroRol = "Proveedor",
                EstadoDocumento = DocumentoAutorizacionHelper.ObtenerEstadoVisual(item.EstadoSri, estaAutorizado),
                BaseImponible = item.TotalSinImpuestos,
                Iva = item.IvaTotal,
                Total = item.ImporteTotal,
                DocumentoRelacionado = string.Empty,
                ClaveAcceso = item.ClaveAcceso,
                NumeroAutorizacion = item.NumeroAutorizacion,
                EstaAutorizado = estaAutorizado,
                XmlUrl = item.XmlUrl,
                PdfUrl = item.PdfUrl,
                CodigosRelacionados = detalle?.Codigos ?? new List<string>(),
                ProductosRelacionados = detalle?.Descripciones ?? new List<string>(),
                BaseSinIva = detalle?.BaseSinIva ?? ObtenerBaseSinIvaFallback(item.TotalSinImpuestos, item.IvaTotal),
                BaseConIva = detalle?.BaseConIva ?? ObtenerBaseConIvaFallback(item.TotalSinImpuestos, item.IvaTotal),
                TieneProducto = detalle?.TieneProducto == true,
                TieneServicio = detalle?.TieneServicio == true
            };
        }));

        items = items
            .OrderByDescending(x => x.FechaEmision ?? DateTime.MinValue)
            .ThenByDescending(x => x.DocumentoId)
            .ToList();

        return new ReporteComprobantesCargaDto
        {
            NombreEmisor = emisorActivo?.RazonSocial?.Trim()
                ?? emisorActivo?.NomComercial?.Trim()
                ?? usuario?.NombreEmpresa?.Trim()
                ?? string.Empty,
            RucEmisor = emisorActivo?.Ruc?.Trim() ?? string.Empty,
            NombreUsuario = usuario?.NombreCompleto ?? string.Empty,
            GeneradoEn = DateTime.Now,
            Items = items,
            ClientesDisponibles = items
                .Select(x => x.TerceroNombre)
                .Where(x => !string.IsNullOrWhiteSpace(x))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .OrderBy(x => x)
                .ToList(),
            ProductosDisponibles = items
                .SelectMany(x => x.ProductosRelacionados)
                .Where(x => !string.IsNullOrWhiteSpace(x))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .OrderBy(x => x)
                .Take(300)
                .ToList(),
            EstadosDisponibles = items
                .Select(x => x.EstadoDocumento)
                .Where(x => !string.IsNullOrWhiteSpace(x))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .OrderBy(x => x)
                .ToList()
        };
    }

    public Task<string?> AsegurarXmlDocumentoUsuarioAsync(string tipoDocumentoCodigo, int documentoId, int idUsuario)
    {
        if (documentoId <= 0 || idUsuario <= 0)
        {
            return Task.FromResult<string?>(null);
        }

        return tipoDocumentoCodigo switch
        {
            ReporteComprobantesTipos.Factura => _facturacionService.AsegurarXmlFacturaUsuarioAsync(documentoId, idUsuario),
            ReporteComprobantesTipos.NotaCredito => _notaCreditoService.AsegurarXmlNotaCreditoUsuarioAsync(documentoId, idUsuario),
            ReporteComprobantesTipos.NotaDebito => _notaDebitoService.AsegurarXmlNotaDebitoUsuarioAsync(documentoId, idUsuario),
            ReporteComprobantesTipos.GuiaRemision => _guiaRemisionService.AsegurarXmlGuiaRemisionUsuarioAsync(documentoId, idUsuario),
            ReporteComprobantesTipos.Retencion => _retencionGeneradaService.AsegurarXmlRetencionUsuarioAsync(documentoId, idUsuario),
            ReporteComprobantesTipos.LiquidacionCompra => _liquidacionCompraService.AsegurarXmlLiquidacionUsuarioAsync(documentoId, idUsuario),
            _ => Task.FromResult<string?>(null)
        };
    }

    public Task<string?> AsegurarPdfDocumentoUsuarioAsync(string tipoDocumentoCodigo, int documentoId, int idUsuario, FormatoImpresionDocumento formato = FormatoImpresionDocumento.A4)
    {
        if (documentoId <= 0 || idUsuario <= 0)
        {
            return Task.FromResult<string?>(null);
        }

        return tipoDocumentoCodigo switch
        {
            ReporteComprobantesTipos.Factura => _facturacionService.AsegurarPdfFacturaUsuarioAsync(documentoId, idUsuario, formato),
            ReporteComprobantesTipos.NotaCredito => _notaCreditoService.AsegurarPdfNotaCreditoUsuarioAsync(documentoId, idUsuario, formato),
            ReporteComprobantesTipos.NotaDebito => _notaDebitoService.AsegurarPdfNotaDebitoUsuarioAsync(documentoId, idUsuario, formato),
            ReporteComprobantesTipos.GuiaRemision => _guiaRemisionService.AsegurarPdfGuiaRemisionUsuarioAsync(documentoId, idUsuario, formato),
            ReporteComprobantesTipos.Retencion => _retencionGeneradaService.AsegurarPdfRetencionUsuarioAsync(documentoId, idUsuario, formato),
            ReporteComprobantesTipos.LiquidacionCompra => _liquidacionCompraService.AsegurarPdfLiquidacionUsuarioAsync(documentoId, idUsuario, formato),
            _ => Task.FromResult<string?>(null)
        };
    }

    private static async Task<int> ResolverUsuarioEmisorAsync(AppDbContext context, int idUsuario)
    {
        var usuario = await context.Usuarios
            .AsNoTracking()
            .FirstOrDefaultAsync(x => x.IdUsuario == idUsuario);

        return usuario?.estadoAsociado == true && usuario.idJefe is > 0
            ? usuario.idJefe.Value
            : idUsuario;
    }

    private static async Task<Dictionary<int, FacturaHeaderLookup>> CargarFacturasHeaderAsync(AppDbContext context, IReadOnlyCollection<int> ids)
    {
        if (ids.Count == 0)
        {
            return new Dictionary<int, FacturaHeaderLookup>();
        }

        return await context.Facturas
            .AsNoTracking()
            .Where(x => ids.Contains(x.Codfactura))
            .Select(x => new
            {
                x.Codfactura,
                Subtotal = x.Subtotal ?? 0m,
                Subtotal0 = x.Subtotal0,
                SubtotalConIva = x.Subtotal12,
                Iva = x.Iva ?? 0m,
                ClaveAcceso = x.Codclave ?? string.Empty,
                RucEmisor = x.CodemisorNavigation != null ? (x.CodemisorNavigation.Ruc ?? string.Empty) : string.Empty,
                x.Serie,
                x.Numfactura
            })
            .ToDictionaryAsync(
                x => x.Codfactura,
                x => new FacturaHeaderLookup
                {
                    Subtotal = x.Subtotal,
                    Subtotal0 = x.Subtotal0,
                    SubtotalConIva = x.SubtotalConIva,
                    Iva = x.Iva,
                    ClaveAcceso = x.ClaveAcceso,
                    XmlUrl = ConstruirUrlFactura(x.RucEmisor, x.Serie, x.Numfactura, "xml"),
                    PdfUrl = ConstruirUrlFactura(x.RucEmisor, x.Serie, x.Numfactura, "pdf")
                });
    }

    private static async Task<Dictionary<int, NotaHeaderLookup>> CargarNotasCreditoHeaderAsync(AppDbContext context, IReadOnlyCollection<int> ids)
    {
        if (ids.Count == 0)
        {
            return new Dictionary<int, NotaHeaderLookup>();
        }

        var data = await (
            from nc in context.NotaCreditos.AsNoTracking()
            join e in context.Emisores.AsNoTracking()
                on nc.CodEmisor equals e.Codigo into emJoin
            from e in emJoin.DefaultIfEmpty()
            where ids.Contains(nc.Sec)
            select new
            {
                nc.Sec,
                nc.CodClave,
                nc.NumNotaCredito,
                RucEmisor = e != null ? (e.Ruc ?? string.Empty) : string.Empty
            })
            .ToListAsync();

        return data.ToDictionary(
            x => x.Sec,
            x => new NotaHeaderLookup
            {
                ClaveAcceso = x.CodClave ?? string.Empty,
                PdfUrl = ConstruirUrlNotaCredito(x.RucEmisor, x.NumNotaCredito, "pdf")
            });
    }

    private static async Task<Dictionary<int, NotaHeaderLookup>> CargarNotasDebitoHeaderAsync(AppDbContext context, IReadOnlyCollection<int> ids)
    {
        if (ids.Count == 0)
        {
            return new Dictionary<int, NotaHeaderLookup>();
        }

        var data = await (
            from nd in context.NotaDebitos.AsNoTracking()
            join e in context.Emisores.AsNoTracking()
                on nd.CodEmisor equals e.Codigo into emJoin
            from e in emJoin.DefaultIfEmpty()
            where ids.Contains(nd.Sec)
            select new
            {
                nd.Sec,
                nd.CodClave,
                nd.NumNotaDebito,
                RucEmisor = e != null ? (e.Ruc ?? string.Empty) : string.Empty
            })
            .ToListAsync();

        return data.ToDictionary(
            x => x.Sec,
            x => new NotaHeaderLookup
            {
                ClaveAcceso = x.CodClave ?? string.Empty,
                PdfUrl = ConstruirUrlNotaDebito(x.RucEmisor, x.NumNotaDebito, "pdf")
            });
    }

    private static async Task<Dictionary<int, RetencionHeaderLookup>> CargarRetencionesHeaderAsync(AppDbContext context, IReadOnlyCollection<int> ids)
    {
        if (ids.Count == 0)
        {
            return new Dictionary<int, RetencionHeaderLookup>();
        }

        var data = await context.RetencionInfo
            .AsNoTracking()
            .Where(x => ids.Contains(x.Sec))
            .Select(x => new
            {
                x.Sec,
                x.IcCompra,
                x.Clave,
                x.NombreXml,
                x.NumRetencion,
                x.IdEmpresa,
                x.IdSucursal
            })
            .ToListAsync();

        var emisoresRelacionados = await context.Emisores
            .AsNoTracking()
            .Where(x => x.Estado)
            .Select(x => new
            {
                x.IdEmpresa,
                x.IdSucursal,
                RucEmisor = x.Ruc ?? string.Empty
            })
            .ToListAsync();

        var rucPorEmpresaSucursal = emisoresRelacionados
            .GroupBy(x => $"{x.IdEmpresa}|{x.IdSucursal}", StringComparer.OrdinalIgnoreCase)
            .ToDictionary(
                g => g.Key,
                g => g.Select(x => x.RucEmisor).FirstOrDefault(x => !string.IsNullOrWhiteSpace(x)) ?? string.Empty,
                StringComparer.OrdinalIgnoreCase);

        return data.ToDictionary(
            x => x.Sec,
            x => new RetencionHeaderLookup
            {
                IdCompra = x.IcCompra,
                PdfUrl = ConstruirUrlRetencionPdf(
                    rucPorEmpresaSucursal.GetValueOrDefault($"{x.IdEmpresa}|{x.IdSucursal}", string.Empty),
                    x.NumRetencion,
                    x.Sec),
                XmlUrl = ConstruirUrlRetencionXml(x.NombreXml, x.Clave)
            });
    }

    private static async Task<Dictionary<int, ReporteDetalleLookup>> CargarDetallesFacturaAsync(AppDbContext context, IReadOnlyCollection<int> ids)
    {
        if (ids.Count == 0)
        {
            return new Dictionary<int, ReporteDetalleLookup>();
        }

        var detalles = await (
            from d in context.Detallefacturas.AsNoTracking()
            join p in context.Productos.AsNoTracking()
                on d.Codproducto equals p.Codigo into prodJoin
            from p in prodJoin.DefaultIfEmpty()
            where ids.Contains(d.Codfactura)
            select new DetalleFlat
            {
                DocumentoId = d.Codfactura,
                Codigo = !string.IsNullOrWhiteSpace(d.Codprincipal)
                    ? d.Codprincipal
                    : (!string.IsNullOrWhiteSpace(d.Codauxiliar) ? d.Codauxiliar : d.Codproducto.ToString()),
                Descripcion = !string.IsNullOrWhiteSpace(d.Descripproducto)
                    ? d.Descripproducto
                    : (p != null ? (p.Nombre ?? string.Empty) : string.Empty),
                TipoCompravena = p != null ? p.Tipocompravena : null,
                BaseSinIva = d.Tarifa == 0 ? d.Valortproducto : 0m,
                BaseConIva = d.Tarifa > 0 || d.Valoriva > 0 ? d.Valortproducto : 0m,
                ForzarProducto = true
            })
            .ToListAsync();

        return AgruparDetalles(detalles);
    }

    private static async Task<Dictionary<int, ReporteDetalleLookup>> CargarDetallesNotaCreditoAsync(AppDbContext context, IReadOnlyCollection<int> ids)
    {
        if (ids.Count == 0)
        {
            return new Dictionary<int, ReporteDetalleLookup>();
        }

        var detalles = await (
            from d in context.DetallesNotaCredito.AsNoTracking()
            join p in context.Productos.AsNoTracking()
                on d.CodProducto equals p.Codigo into prodJoin
            from p in prodJoin.DefaultIfEmpty()
            where ids.Contains(d.CodNotaCredito)
            select new DetalleFlat
            {
                DocumentoId = d.CodNotaCredito,
                Codigo = !string.IsNullOrWhiteSpace(d.CodPrincipal)
                    ? d.CodPrincipal
                    : (!string.IsNullOrWhiteSpace(d.CodAuxiliar) ? d.CodAuxiliar : d.CodProducto.ToString()),
                Descripcion = !string.IsNullOrWhiteSpace(d.DescripProducto)
                    ? d.DescripProducto
                    : (p != null ? (p.Nombre ?? string.Empty) : string.Empty),
                TipoCompravena = p != null ? p.Tipocompravena : null,
                BaseSinIva = (d.Tarifa ?? 0) == 0 ? d.ValorTProducto ?? 0m : 0m,
                BaseConIva = (d.Tarifa ?? 0) > 0 || (d.ValorIVA ?? 0m) > 0m ? d.ValorTProducto ?? 0m : 0m,
                ForzarProducto = true
            })
            .ToListAsync();

        return AgruparDetalles(detalles);
    }

    private static async Task<Dictionary<int, ReporteDetalleLookup>> CargarDetallesNotaDebitoAsync(AppDbContext context, IReadOnlyCollection<int> ids)
    {
        if (ids.Count == 0)
        {
            return new Dictionary<int, ReporteDetalleLookup>();
        }

        var detalles = await (
            from d in context.DetallesNotaDebito.AsNoTracking()
            join p in context.Productos.AsNoTracking()
                on d.CodProducto equals p.Codigo into prodJoin
            from p in prodJoin.DefaultIfEmpty()
            where ids.Contains(d.CodNotaDebito)
            select new DetalleFlat
            {
                DocumentoId = d.CodNotaDebito,
                Codigo = !string.IsNullOrWhiteSpace(d.CodPrincipal)
                    ? d.CodPrincipal
                    : (!string.IsNullOrWhiteSpace(d.CodAuxiliar) ? d.CodAuxiliar : d.CodProducto.ToString()),
                Descripcion = !string.IsNullOrWhiteSpace(d.DescripProducto)
                    ? d.DescripProducto
                    : (p != null ? (p.Nombre ?? string.Empty) : string.Empty),
                TipoCompravena = p != null ? p.Tipocompravena : null,
                BaseSinIva = (d.PorcentajeIva ?? 0m) == 0m ? d.ValorTProducto ?? 0m : 0m,
                BaseConIva = (d.PorcentajeIva ?? 0m) > 0m || (d.ValorIva ?? 0m) > 0m ? d.ValorTProducto ?? 0m : 0m,
                ForzarProducto = true
            })
            .ToListAsync();

        return AgruparDetalles(detalles);
    }

    private static async Task<Dictionary<int, ReporteDetalleLookup>> CargarDetallesGuiaAsync(AppDbContext context, IReadOnlyCollection<int> ids)
    {
        if (ids.Count == 0)
        {
            return new Dictionary<int, ReporteDetalleLookup>();
        }

        var detalles = await context.DetallesGuiaRemision
            .AsNoTracking()
            .Where(x => x.IdGuiaRemision.HasValue && ids.Contains(x.IdGuiaRemision.Value))
            .Select(x => new DetalleFlat
            {
                DocumentoId = x.IdGuiaRemision ?? 0,
                Codigo = !string.IsNullOrWhiteSpace(x.CodInterno) ? x.CodInterno : x.CodAdicional ?? string.Empty,
                Descripcion = x.Descripcion ?? string.Empty,
                TipoCompravena = "PRODUCTO",
                ForzarProducto = true
            })
            .ToListAsync();

        return AgruparDetalles(detalles);
    }

    private static async Task<Dictionary<int, ReporteDetalleLookup>> CargarDetallesCompraAsync(AppDbContext context, IReadOnlyCollection<int> ids)
    {
        if (ids.Count == 0)
        {
            return new Dictionary<int, ReporteDetalleLookup>();
        }

        var detalles = await (
            from d in context.ComprasDetalleFac.AsNoTracking()
            join p in context.Productos.AsNoTracking()
                on d.CodProducto equals p.Codigo into prodJoin
            from p in prodJoin.DefaultIfEmpty()
            where ids.Contains(d.CodFactura)
            select new DetalleFlat
            {
                DocumentoId = d.CodFactura,
                Codigo = !string.IsNullOrWhiteSpace(d.CodPrincipal)
                    ? d.CodPrincipal
                    : (!string.IsNullOrWhiteSpace(d.CodAuxiliar) ? d.CodAuxiliar : d.CodProducto.ToString()),
                Descripcion = !string.IsNullOrWhiteSpace(d.DescripProducto)
                    ? d.DescripProducto
                    : (p != null ? (p.Nombre ?? string.Empty) : string.Empty),
                TipoCompravena = p != null ? p.Tipocompravena : null,
                BaseSinIva = (d.Tarifa ?? 0) == 0 ? d.ValorTProducto ?? 0m : 0m,
                BaseConIva = (d.Tarifa ?? 0) > 0 || (d.ValorIVA ?? 0m) > 0m ? d.ValorTProducto ?? 0m : 0m,
                ForzarProducto = true
            })
            .ToListAsync();

        return AgruparDetalles(detalles);
    }

    private static Dictionary<int, ReporteDetalleLookup> AgruparDetalles(IEnumerable<DetalleFlat> detalles)
    {
        return detalles
            .Where(x => x.DocumentoId > 0)
            .GroupBy(x => x.DocumentoId)
            .ToDictionary(
                g => g.Key,
                g =>
                {
                    var lookup = new ReporteDetalleLookup();

                    foreach (var item in g)
                    {
                        var descripcion = (item.Descripcion ?? string.Empty).Trim();
                        if (!string.IsNullOrWhiteSpace(descripcion))
                        {
                            lookup.Descripciones.Add(descripcion);
                        }

                        var codigo = (item.Codigo ?? string.Empty).Trim();
                        if (!string.IsNullOrWhiteSpace(codigo))
                        {
                            lookup.Codigos.Add(codigo);
                        }

                        var tipo = (item.TipoCompravena ?? string.Empty).Trim().ToUpperInvariant();
                        lookup.BaseSinIva += item.BaseSinIva;
                        lookup.BaseConIva += item.BaseConIva;

                        if (tipo == "SERVICIO")
                        {
                            lookup.TieneServicio = true;
                        }
                        else if (item.ForzarProducto || !string.IsNullOrWhiteSpace(descripcion))
                        {
                            lookup.TieneProducto = true;
                        }
                    }

                    lookup.Descripciones = lookup.Descripciones
                        .Distinct(StringComparer.OrdinalIgnoreCase)
                        .OrderBy(x => x)
                        .ToList();

                    lookup.Codigos = lookup.Codigos
                        .Distinct(StringComparer.OrdinalIgnoreCase)
                        .OrderBy(x => x)
                        .ToList();

                    return lookup;
                });
    }

    private static decimal ObtenerBaseSinIvaFallback(decimal baseImponible, decimal iva)
        => iva > 0m ? 0m : baseImponible;

    private static decimal ObtenerBaseConIvaFallback(decimal baseImponible, decimal iva)
        => iva > 0m ? baseImponible : 0m;

    private static string ConstruirUrlFactura(string? ruc, string? serie, string? numero, string extension)
    {
        if (string.IsNullOrWhiteSpace(ruc))
        {
            return string.Empty;
        }

        return $"/FacturasGeneradas/{LimpiarSegmento(ruc, "factura")}_{LimpiarSegmento(serie, "001001")}_{LimpiarSegmento(numero, "000000001")}.{extension}";
    }

    private static string ConstruirUrlNotaCredito(string? ruc, string? numero, string extension)
    {
        if (string.IsNullOrWhiteSpace(ruc))
        {
            return string.Empty;
        }

        var secuencial = (numero ?? string.Empty).Replace("-", string.Empty).Trim().PadLeft(9, '0');
        return $"/notas_de_credito/{ruc}_{4:00}_{secuencial}.{extension}";
    }

    private static string ConstruirUrlNotaDebito(string? ruc, string? numero, string extension)
    {
        if (string.IsNullOrWhiteSpace(ruc))
        {
            return string.Empty;
        }

        var secuencial = (numero ?? string.Empty).Replace("-", string.Empty).Trim().PadLeft(9, '0');
        return $"/notas_de_debito/{LimpiarSegmento(ruc, "nota_debito")}_{5:00}_{secuencial}.{extension}";
    }

    private static string ConstruirUrlRetencionXml(string? nombreXml, string? clave)
    {
        if (!string.IsNullOrWhiteSpace(nombreXml))
        {
            return $"/comprobantes/generados/{nombreXml.Trim()}";
        }

        return string.IsNullOrWhiteSpace(clave)
            ? string.Empty
            : $"/comprobantes/generados/RET_{clave.Trim()}.xml";
    }

    private static string ConstruirUrlRetencionPdf(string? ruc, string? numeroRetencion, int sec)
    {
        if (string.IsNullOrWhiteSpace(ruc))
        {
            return string.Empty;
        }

        var numeroSeguro = LimpiarSegmento(numeroRetencion, sec.ToString()).PadLeft(9, '0');
        return $"/retenciones/{LimpiarSegmento(ruc, "retencion")}_{7:00}_{numeroSeguro}.pdf";
    }

    private static string LimpiarSegmento(string? valor, string reemplazo)
    {
        var limpio = string.IsNullOrWhiteSpace(valor) ? reemplazo : valor.Trim();

        foreach (var caracter in Path.GetInvalidFileNameChars())
        {
            limpio = limpio.Replace(caracter, '_');
        }

        return limpio.Replace(" ", "_");
    }

    private sealed class FacturaHeaderLookup
    {
        public decimal Subtotal { get; init; }
        public decimal? Subtotal0 { get; init; }
        public decimal? SubtotalConIva { get; init; }
        public decimal Iva { get; init; }
        public string ClaveAcceso { get; init; } = string.Empty;
        public string XmlUrl { get; init; } = string.Empty;
        public string PdfUrl { get; init; } = string.Empty;
    }

    private sealed class NotaHeaderLookup
    {
        public string ClaveAcceso { get; init; } = string.Empty;
        public string PdfUrl { get; init; } = string.Empty;
    }

    private sealed class RetencionHeaderLookup
    {
        public int? IdCompra { get; init; }
        public string XmlUrl { get; init; } = string.Empty;
        public string PdfUrl { get; init; } = string.Empty;
    }

    private sealed class DetalleFlat
    {
        public int DocumentoId { get; init; }
        public string Codigo { get; init; } = string.Empty;
        public string Descripcion { get; init; } = string.Empty;
        public string? TipoCompravena { get; init; }
        public decimal BaseSinIva { get; init; }
        public decimal BaseConIva { get; init; }
        public bool ForzarProducto { get; init; }
    }

    private sealed class ReporteDetalleLookup
    {
        public List<string> Codigos { get; set; } = new();
        public List<string> Descripciones { get; set; } = new();
        public decimal BaseSinIva { get; set; }
        public decimal BaseConIva { get; set; }
        public bool TieneProducto { get; set; }
        public bool TieneServicio { get; set; }
    }
}

public interface IReporteComprobantesPdfService
{
    Task<string> GenerarPdfAsync(ReporteComprobantesPdfRequest request);
}

public sealed class ReporteComprobantesPdfService : IReporteComprobantesPdfService
{
    private static readonly CultureInfo Cultura = new("es-EC");
    private readonly IWebHostEnvironment _environment;

    public ReporteComprobantesPdfService(IWebHostEnvironment environment)
    {
        _environment = environment;
    }

    public Task<string> GenerarPdfAsync(ReporteComprobantesPdfRequest request)
    {
        if (request.Items.Count == 0)
        {
            throw new InvalidOperationException("No hay documentos filtrados para exportar.");
        }

        var webRoot = ObtenerWebRootPath();
        var carpeta = Path.Combine(webRoot, "reportes_documentos");
        Directory.CreateDirectory(carpeta);

        var nombreArchivo = $"reporte_documentos_{DateTime.Now:yyyyMMdd_HHmmss}.pdf";
        var rutaPdf = Path.Combine(carpeta, nombreArchivo);
        var logo = CargarLogoSistema(webRoot);

        var totalBase = request.Items.Sum(x => x.BaseImponible);
        var totalIva = request.Items.Sum(x => x.Iva);
        var totalGeneral = request.Items.Sum(x => x.Total);
        var porTipo = request.Items
            .GroupBy(x => x.TipoDocumento)
            .Select(g => new ResumenTipoPdf
            {
                TipoDocumento = g.Key,
                Cantidad = g.Count(),
                Total = g.Sum(x => x.Total)
            })
            .OrderByDescending(x => x.Cantidad)
            .ThenBy(x => x.TipoDocumento)
            .ToList();

        Document.Create(container =>
        {
            container.Page(page =>
            {
                page.Size(PageSizes.A4.Landscape());
                page.Margin(1.2f, Unit.Centimetre);
                page.PageColor(Colors.White);
                page.DefaultTextStyle(x => x.FontSize(9).FontColor(Colors.Grey.Darken4));

                page.Header().Element(header => ComponerEncabezado(header, request, logo));
                page.Content().Element(content => ComponerContenido(content, request, porTipo, totalBase, totalIva, totalGeneral));
                page.Footer()
                    .AlignRight()
                    .DefaultTextStyle(x => x.FontSize(8).FontColor(Colors.Grey.Darken1))
                    .Text(text =>
                    {
                        text.Span("Generado ");
                        text.Span(request.GeneradoEn.ToString("dd/MM/yyyy HH:mm", Cultura)).SemiBold();
                    });
            });
        }).GeneratePdf(rutaPdf);

        return Task.FromResult($"/reportes_documentos/{nombreArchivo}");
    }

    private static void ComponerEncabezado(IContainer container, ReporteComprobantesPdfRequest request, byte[]? logo)
    {
        container.PaddingBottom(12).Row(row =>
        {
            row.RelativeItem()
                .Border(1)
                .BorderColor("#D6E5F1")
                .Background("#F7FBFF")
                .Padding(12)
                .Column(column =>
                {
                    if (logo != null)
                    {
                        column.Item().MaxWidth(120).Image(logo).FitWidth();
                    }

                    column.Item().PaddingTop(logo != null ? 8 : 0).Text("Reporte consolidado de comprobantes")
                        .FontSize(18)
                        .SemiBold()
                        .FontColor("#0B5B97");

                    column.Item().PaddingTop(4).Text("Filtros, resumen operativo y accesos a XML/PDF de los documentos emitidos.")
                        .FontColor("#60768B");

                    if (!string.IsNullOrWhiteSpace(request.NombreEmisor))
                    {
                        column.Item().PaddingTop(8).Text($"Emisor: {request.NombreEmisor}")
                            .SemiBold();
                    }

                    if (!string.IsNullOrWhiteSpace(request.RucEmisor))
                    {
                        column.Item().Text($"RUC: {request.RucEmisor}");
                    }
                });

            row.ConstantItem(220)
                .Border(1)
                .BorderColor("#C8DDEE")
                .Background("#EEF6FC")
                .Padding(12)
                .Column(column =>
                {
                    column.Item().Text("Control del reporte")
                        .FontSize(12)
                        .SemiBold()
                        .FontColor("#0B5B97");

                    column.Item().PaddingTop(8).Text($"Usuario: {ValorOrDash(request.NombreUsuario)}");
                    column.Item().Text($"Emitidos visibles: {request.Items.Count}");
                    column.Item().Text($"Generado: {request.GeneradoEn:dd/MM/yyyy HH:mm}");
                });
        });
    }

    private static void ComponerContenido(
        IContainer container,
        ReporteComprobantesPdfRequest request,
        IReadOnlyCollection<ResumenTipoPdf> porTipo,
        decimal totalBase,
        decimal totalIva,
        decimal totalGeneral)
    {
        container.Column(column =>
        {
            column.Spacing(12);
            column.Item().Element(x => ComponerFiltros(x, request.Filtros));
            column.Item().Element(x => ComponerMetricas(x, request.Items.Count, totalBase, totalIva, totalGeneral));
            column.Item().Element(x => ComponerResumenPorTipo(x, porTipo));
            column.Item().Element(x => ComponerTablaDocumentos(x, request.Items));
            column.Item().Element(x => ComponerRutas(x, request.Items));
        });
    }

    private static void ComponerFiltros(IContainer container, ReporteComprobantesFiltroDto filtros)
    {
        container.Border(1)
            .BorderColor("#DCE8F2")
            .Background("#FFFFFF")
            .Padding(12)
            .Column(column =>
            {
                column.Spacing(6);
                column.Item().Text("Filtros aplicados")
                    .FontSize(11)
                    .SemiBold()
                    .FontColor("#0B5B97");

                column.Item().Row(row =>
                {
                    row.RelativeItem().Text($"Tipo: {ResolverValorFiltro(filtros.TipoDocumento)}");
                    row.RelativeItem().Text($"Estado: {ResolverValorFiltro(filtros.EstadoDocumento)}");
                    row.RelativeItem().Text($"Autorizacion: {ResolverValorFiltro(filtros.Autorizacion)}");
                });

                column.Item().Row(row =>
                {
                    row.RelativeItem().Text($"Desde: {ResolverFechaFiltro(filtros.FechaDesde)}");
                    row.RelativeItem().Text($"Hasta: {ResolverFechaFiltro(filtros.FechaHasta)}");
                    row.RelativeItem().Text($"Categoria: {ResolverValorFiltro(filtros.CategoriaDetalle)}");
                });

                column.Item().Row(row =>
                {
                    row.RelativeItem().Text($"Cliente / tercero: {ResolverValorFiltro(filtros.Cliente)}");
                    row.RelativeItem().Text($"Producto / servicio: {ResolverValorFiltro(filtros.Producto)}");
                });
            });
    }

    private static void ComponerMetricas(IContainer container, int totalItems, decimal totalBase, decimal totalIva, decimal totalGeneral)
    {
        container.Row(row =>
        {
            row.RelativeItem().Element(card => ComponerTarjetaMetrica(card, "Documentos", totalItems.ToString("N0", Cultura), "Comprobantes visibles en el reporte."));
            row.RelativeItem().Element(card => ComponerTarjetaMetrica(card, "Base", FormatearMoneda(totalBase), "Subtotal o base imponible consolidada."));
            row.RelativeItem().Element(card => ComponerTarjetaMetrica(card, "IVA", FormatearMoneda(totalIva), "Carga tributaria visible en el filtro."));
            row.RelativeItem().Element(card => ComponerTarjetaMetrica(card, "Total", FormatearMoneda(totalGeneral), "Importe global de los documentos filtrados."));
        });
    }

    private static void ComponerTarjetaMetrica(IContainer container, string titulo, string valor, string ayuda)
    {
        container.Border(1)
            .BorderColor("#D6E5F1")
            .Background("#F7FBFF")
            .Padding(10)
            .Column(column =>
            {
                column.Spacing(4);
                column.Item().Text(titulo)
                    .FontSize(9)
                    .SemiBold()
                    .FontColor("#60768B");
                column.Item().Text(valor)
                    .FontSize(14)
                    .SemiBold()
                    .FontColor("#17324A");
                column.Item().Text(ayuda)
                    .FontSize(8)
                    .FontColor("#60768B");
            });
    }

    private static void ComponerResumenPorTipo(IContainer container, IReadOnlyCollection<ResumenTipoPdf> resumen)
    {
        container.Border(1)
            .BorderColor("#DCE8F2")
            .Background("#FFFFFF")
            .Padding(12)
            .Column(column =>
            {
                column.Spacing(8);
                column.Item().Text("Distribucion por tipo de documento")
                    .FontSize(11)
                    .SemiBold()
                    .FontColor("#0B5B97");

                column.Item().Table(table =>
                {
                    table.ColumnsDefinition(columns =>
                    {
                        columns.RelativeColumn(2.2f);
                        columns.ConstantColumn(70);
                        columns.ConstantColumn(95);
                    });

                    table.Header(header =>
                    {
                        header.Cell().Element(HeaderCellStyle).Text("Tipo");
                        header.Cell().Element(HeaderCellStyle).AlignRight().Text("Cantidad");
                        header.Cell().Element(HeaderCellStyle).AlignRight().Text("Total");
                    });

                    foreach (var item in resumen)
                    {
                        table.Cell().Element(BodyCellStyle).Text(item.TipoDocumento);
                        table.Cell().Element(BodyCellStyle).AlignRight().Text(item.Cantidad.ToString("N0", Cultura));
                        table.Cell().Element(BodyCellStyle).AlignRight().Text(FormatearMoneda(item.Total));
                    }
                });
            });
    }

    private static void ComponerTablaDocumentos(IContainer container, IReadOnlyCollection<ReporteComprobanteItemDto> items)
    {
        container.Border(1)
            .BorderColor("#DCE8F2")
            .Background("#FFFFFF")
            .Padding(12)
            .Column(column =>
            {
                column.Spacing(8);
                column.Item().Text("Documentos visibles")
                    .FontSize(11)
                    .SemiBold()
                    .FontColor("#0B5B97");

                column.Item().Table(table =>
                {
                    table.ColumnsDefinition(columns =>
                    {
                        columns.ConstantColumn(58);
                        columns.ConstantColumn(88);
                        columns.ConstantColumn(92);
                        columns.RelativeColumn(1.3f);
                        columns.RelativeColumn(1.1f);
                        columns.ConstantColumn(88);
                        columns.ConstantColumn(72);
                        columns.ConstantColumn(78);
                    });

                    table.Header(header =>
                    {
                        header.Cell().Element(HeaderCellStyle).Text("Fecha");
                        header.Cell().Element(HeaderCellStyle).Text("Tipo");
                        header.Cell().Element(HeaderCellStyle).Text("Numero");
                        header.Cell().Element(HeaderCellStyle).Text("Tercero");
                        header.Cell().Element(HeaderCellStyle).Text("Detalle");
                        header.Cell().Element(HeaderCellStyle).Text("Accesos");
                        header.Cell().Element(HeaderCellStyle).Text("Estado");
                        header.Cell().Element(HeaderCellStyle).AlignRight().Text("Total");
                    });

                    foreach (var item in items)
                    {
                        table.Cell().Element(BodyCellStyle).Text(item.FechaEmision?.ToString("dd/MM/yyyy", Cultura) ?? "-");
                        table.Cell().Element(BodyCellStyle).Text(item.TipoDocumento);
                        table.Cell().Element(BodyCellStyle).Text(ValorOrDash(item.NumeroDocumento));
                        table.Cell().Element(BodyCellStyle).Column(col =>
                        {
                            col.Item().Text(ValorOrDash(item.TerceroNombre)).SemiBold();
                            col.Item().Text(ValorOrDash(item.TerceroIdentificacion)).FontSize(8).FontColor("#60768B");
                        });
                        table.Cell().Element(BodyCellStyle).Column(col =>
                        {
                            col.Item().Text(item.ResumenProductos);
                            col.Item().Text(item.CategoriaDetalle).FontSize(8).FontColor("#60768B");
                        });
                        table.Cell().Element(BodyCellStyle).Column(col =>
                        {
                            col.Spacing(3);

                            if (!string.IsNullOrWhiteSpace(item.XmlUrl))
                            {
                                col.Item().Element(link => ComponerEnlaceAcceso(link, "Abrir XML", item.XmlUrl));
                            }

                            if (!string.IsNullOrWhiteSpace(item.PdfUrl))
                            {
                                col.Item().Element(link => ComponerEnlaceAcceso(link, "Abrir PDF", item.PdfUrl));
                            }

                            if (string.IsNullOrWhiteSpace(item.XmlUrl) && string.IsNullOrWhiteSpace(item.PdfUrl))
                            {
                                col.Item().Text("No disponible").FontSize(8).FontColor("#60768B");
                            }
                        });
                        table.Cell().Element(BodyCellStyle).Text(ValorOrDash(item.EstadoDocumento));
                        table.Cell().Element(BodyCellStyle).AlignRight().Text(FormatearMoneda(item.Total));
                    }
                });
            });
    }

    private static void ComponerRutas(IContainer container, IReadOnlyCollection<ReporteComprobanteItemDto> items)
    {
        container.Border(1)
            .BorderColor("#DCE8F2")
            .Background("#FFFFFF")
            .Padding(12)
            .Column(column =>
            {
                column.Spacing(8);
                column.Item().Text("Accesos XML y PDF")
                    .FontSize(11)
                    .SemiBold()
                    .FontColor("#0B5B97");

                foreach (var item in items.Where(x => !string.IsNullOrWhiteSpace(x.XmlUrl) || !string.IsNullOrWhiteSpace(x.PdfUrl)))
                {
                    column.Item().BorderBottom(1).BorderColor("#EEF4F8").PaddingBottom(8).Column(card =>
                    {
                        card.Item().Text($"{item.TipoDocumento} | {ValorOrDash(item.NumeroDocumento)}")
                            .SemiBold()
                            .FontColor("#17324A");

                        card.Item().PaddingTop(4).Row(row =>
                        {
                            if (!string.IsNullOrWhiteSpace(item.XmlUrl))
                            {
                                row.AutoItem().Element(link => ComponerEnlaceAcceso(link, "XML del documento", item.XmlUrl));
                            }

                            if (!string.IsNullOrWhiteSpace(item.PdfUrl))
                            {
                                row.AutoItem().PaddingLeft(string.IsNullOrWhiteSpace(item.XmlUrl) ? 0 : 8)
                                    .Element(link => ComponerEnlaceAcceso(link, "PDF del documento", item.PdfUrl));
                            }
                        });

                        card.Item().PaddingTop(4).DefaultTextStyle(x => x.FontSize(8)).Text(text =>
                        {
                            text.Span("Los accesos son clicables desde el PDF exportado. ")
                                .FontColor("#60768B");

                            var valorAcceso = DocumentoAutorizacionHelper.ObtenerValorAcceso(item.EstaAutorizado, item.NumeroAutorizacion, item.ClaveAcceso);
                            if (!string.IsNullOrWhiteSpace(valorAcceso))
                            {
                                text.Span($"{DocumentoAutorizacionHelper.ObtenerEtiquetaAcceso(item.EstaAutorizado, item.NumeroAutorizacion)}: {valorAcceso}")
                                    .SemiBold()
                                    .FontColor("#17324A");
                            }
                        });
                    });
                }
            });
    }

    private static void ComponerEnlaceAcceso(IContainer container, string etiqueta, string url)
    {
        container.Hyperlink(url)
            .Border(1)
            .BorderColor("#B7D2E5")
            .Background("#F3FAFF")
            .PaddingVertical(4)
            .PaddingHorizontal(8)
            .Text(etiqueta)
            .FontSize(8)
            .SemiBold()
            .FontColor("#0B5B97");
    }

    private static IContainer HeaderCellStyle(IContainer container)
        => container.BorderBottom(1).BorderColor("#CFE0ED").PaddingVertical(6).PaddingRight(4);

    private static IContainer BodyCellStyle(IContainer container)
        => container.BorderBottom(1).BorderColor("#EEF4F8").PaddingVertical(5).PaddingRight(4);

    private string ObtenerWebRootPath()
    {
        if (!string.IsNullOrWhiteSpace(_environment.WebRootPath))
        {
            return _environment.WebRootPath;
        }

        return Path.Combine(Directory.GetCurrentDirectory(), "wwwroot");
    }

    private static byte[]? CargarLogoSistema(string webRoot)
    {
        var rutaLogo = Path.Combine(webRoot, "images", "logo.png");
        return File.Exists(rutaLogo) ? File.ReadAllBytes(rutaLogo) : null;
    }

    private static string ResolverValorFiltro(string? valor)
        => string.IsNullOrWhiteSpace(valor) || string.Equals(valor, "TODOS", StringComparison.OrdinalIgnoreCase)
            ? "Todos"
            : valor.Trim();

    private static string ResolverFechaFiltro(DateTime? fecha)
        => fecha.HasValue ? fecha.Value.ToString("dd/MM/yyyy", Cultura) : "Todas";

    private static string FormatearMoneda(decimal valor)
        => $"$ {valor:N2}";

    private static string ValorOrDash(string? valor)
        => string.IsNullOrWhiteSpace(valor) ? "-" : valor.Trim();

    private sealed class ResumenTipoPdf
    {
        public string TipoDocumento { get; init; } = string.Empty;
        public int Cantidad { get; init; }
        public decimal Total { get; init; }
    }
}
