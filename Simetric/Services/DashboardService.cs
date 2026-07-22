using System.Globalization;
using Microsoft.EntityFrameworkCore;
using Simetric.Data;

namespace Simetric.Services;

public sealed class DashboardService
{
    private const string ConsumidorFinalIdentificacion = "9999999999999";
    private static readonly CultureInfo DashboardCulture = CultureInfo.GetCultureInfo("es-EC");
    private readonly IDbContextFactory<AppDbContext> _dbFactory;

    public DashboardService(IDbContextFactory<AppDbContext> dbFactory)
    {
        _dbFactory = dbFactory;
    }

    public async Task<DashboardSnapshotDto> GetSnapshotAsync(int currentUserId, int currentYear)
    {
        await using var db = await _dbFactory.CreateDbContextAsync();
        var usuariosCuentaIds = await ResolveAccountUserIdsAsync(db, currentUserId);
        var monthStart = new DateOnly(DateTime.Today.Year, DateTime.Today.Month, 1);

        var facturasBase = await db.Facturas
            .AsNoTracking()
            .Where(f => f.Idusuario.HasValue
                        && usuariosCuentaIds.Contains(f.Idusuario.Value)
                        && (f.CodemisorNavigation == null || f.CodemisorNavigation.EsEmisorSistema != true)
                        && (f.Estado == null || f.Estado == true))
            .Select(f => new InvoiceSource
            {
                Codfactura = f.Codfactura,
                Numfactura = f.Numfactura,
                Serie = f.Serie,
                FechaEmision = f.Fechaentrega ?? f.Fchautorizacion,
                FechaVence = f.Fechavence,
                FechaCancelado = f.Fechacancelado,
                Total = f.Valortotal ?? 0m,
                ValorRegistradoPorCobrar = f.Valorapagar,
                Autorizado = f.Autorizado == true,
                EstadoSri = f.Estadoenviosri,
                EstadoPago = f.Estadopago,
                TipoPago = f.Tipopago,
                Cliente = f.CodclientesNavigation != null
                    ? (!string.IsNullOrWhiteSpace(f.CodclientesNavigation.Nombrerazonsocial)
                        ? f.CodclientesNavigation.Nombrerazonsocial
                        : (((f.CodclientesNavigation.Nombres ?? string.Empty) + " " + (f.CodclientesNavigation.Apellidos ?? string.Empty)).Trim()))
                    : null,
                IdentificacionCliente = f.CodclientesNavigation != null
                    ? f.CodclientesNavigation.Numeroidentificacion
                    : null
            })
            .ToListAsync();

        var facturaIds = facturasBase.Select(f => f.Codfactura).ToList();
        var abonosPorFactura = facturaIds.Count == 0
            ? new Dictionary<int, decimal>()
            : await ExecuteSafeAsync(
                async () => await db.Abonos
                    .AsNoTracking()
                    .Where(a => a.estado == true && a.codFactura.HasValue && facturaIds.Contains(a.codFactura.Value))
                    .GroupBy(a => a.codFactura!.Value)
                    .Select(g => new
                    {
                        FacturaId = g.Key,
                        TotalAbonado = g.Sum(x => (decimal?)x.abono) ?? 0m
                    })
                    .ToDictionaryAsync(x => x.FacturaId, x => x.TotalAbonado),
                new Dictionary<int, decimal>());

        var facturas = facturasBase
            .Select(f => BuildInvoiceSnapshot(f, abonosPorFactura.TryGetValue(f.Codfactura, out var totalAbonado) ? totalAbonado : 0m))
            .ToList();

        var clientes = await ExecuteSafeAsync(
            async () => await db.Clientes
                .AsNoTracking()
                .Where(c => c.Usuario.HasValue
                            && usuariosCuentaIds.Contains(c.Usuario.Value)
                            && (c.Estado == null || c.Estado == true)
                            && c.Numeroidentificacion != ConsumidorFinalIdentificacion
                            && (!db.Facturas.Any(f => f.Codclientes == c.Codcliente
                                                      && f.CodemisorNavigation != null
                                                      && f.CodemisorNavigation.EsEmisorSistema)
                                || db.Facturas.Any(f => f.Codclientes == c.Codcliente
                                                        && (f.CodemisorNavigation == null
                                                            || f.CodemisorNavigation.EsEmisorSistema != true))))
                .Select(c => new ClienteDashboardInfo
                {
                    FechaIngreso = c.Fechaingreso
                })
                .ToListAsync(),
            new List<ClienteDashboardInfo>());

        var productos = await ExecuteSafeAsync(
            async () => await db.Productos
                .AsNoTracking()
                .Where(p => p.Idusuario.HasValue
                            && usuariosCuentaIds.Contains(p.Idusuario.Value)
                            && (p.Estado == null || p.Estado == true))
                .Select(p => new ProductoDashboardInfo
                {
                    Facturable = p.Facturable == true,
                    TieneImpuesto = p.Codigoimpuesto != null && p.Codigoimpuesto != string.Empty,
                    TieneIva = p.Porcentajeimpuesto != null && p.Porcentajeimpuesto != string.Empty
                })
                .ToListAsync(),
            new List<ProductoDashboardInfo>());

        var notasCredito = await ExecuteSafeAsync(
            async () => await db.NotaCreditos
                .AsNoTracking()
                .Where(n => n.Usuario.HasValue
                            && usuariosCuentaIds.Contains(n.Usuario.Value)
                            && !db.Emisores.Any(e => e.Codigo == n.CodEmisor && e.EsEmisorSistema)
                            && (n.Estado == null || n.Estado == true))
                .Select(n => n.Autorizado)
                .ToListAsync(),
            new List<string?>());

        var notasDebito = await ExecuteSafeAsync(
            async () => await db.NotaDebitos
                .AsNoTracking()
                .Where(n => n.Usuario.HasValue
                            && usuariosCuentaIds.Contains(n.Usuario.Value)
                            && !db.Emisores.Any(e => e.Codigo == n.CodEmisor && e.EsEmisorSistema)
                            && !string.Equals((n.Estado ?? string.Empty).Trim(), "I", StringComparison.OrdinalIgnoreCase))
                .Select(n => n.Autorizado)
                .ToListAsync(),
            new List<string?>());

        var guiasRemision = await ExecuteSafeAsync(
            async () => await db.GuiasRemision
                .AsNoTracking()
                .Where(g => g.IdUsuario.HasValue && usuariosCuentaIds.Contains(g.IdUsuario.Value))
                .Where(g => !db.Facturas.Any(f => f.Codfactura == g.Codfactura
                                                 && f.CodemisorNavigation != null
                                                 && f.CodemisorNavigation.EsEmisorSistema))
                .Select(g => g.EstadoSRI)
                .ToListAsync(),
            new List<string?>());

        var retenciones = await ExecuteSafeAsync(
            async () => await db.RetencionInfo
                .AsNoTracking()
                .Where(r => r.Usuario.HasValue
                            && usuariosCuentaIds.Contains(r.Usuario.Value)
                            && !string.Equals((r.Estado ?? string.Empty).Trim(), "I", StringComparison.OrdinalIgnoreCase))
                .Select(r => r.Autorizado)
                .ToListAsync(),
            new List<string?>());

        var liquidaciones = await ExecuteSafeAsync(
            async () => await db.ComprasFacturas
                .AsNoTracking()
                .Where(c => c.Usuario.HasValue
                            && usuariosCuentaIds.Contains(c.Usuario.Value)
                            && (c.Estado == null || c.Estado == true))
                .Select(c => new LiquidacionDashboardInfo
                {
                    Autorizado = c.Autorizado,
                    EstadoEnvioSri = c.EstadoEnvioSRI
                })
                .ToListAsync(),
            new List<LiquidacionDashboardInfo>());

        var facturasRegistradas = facturas.Count;
        var facturasAutorizadas = facturas.Count(IsAuthorizedInvoice);
        var ventasAcumuladas = facturas.Sum(f => f.Total);
        var ventasAnioActual = facturas.Where(f => f.FechaEmision?.Year == currentYear).Sum(f => f.Total);
        var ventasAnioAnterior = facturas.Where(f => f.FechaEmision?.Year == currentYear - 1).Sum(f => f.Total);
        var ticketPromedio = facturasRegistradas == 0 ? 0m : ventasAcumuladas / facturasRegistradas;

        var carteraAbierta = facturas.Where(IsOpenReceivable).ToList();
        var carteraVencida = carteraAbierta.Where(IsOverdueInvoice).ToList();
        var carteraAlDia = carteraAbierta.Where(f => !IsOverdueInvoice(f)).ToList();
        var cobradas = facturas.Where(f => f.IsCreditInvoice && IsPaidInvoice(f)).ToList();

        var clientesActivos = clientes.Count;
        var clientesNuevosMes = clientes.Count(c => c.FechaIngreso.HasValue && c.FechaIngreso.Value >= monthStart);
        var productosActivos = productos.Count;
        var productosFacturables = productos.Count(p => p.Facturable);
        var productosListosFacturar = productos.Count(p => p.Facturable && p.TieneImpuesto && p.TieneIva);

        var documentosPorTipo = BuildDocumentTypeItems(new[]
        {
            new DocumentTypeAggregate("Facturas", facturasRegistradas, facturasAutorizadas),
            new DocumentTypeAggregate("Notas de crédito", notasCredito.Count, notasCredito.Count(IsApprovedFlag)),
            new DocumentTypeAggregate("Notas de débito", notasDebito.Count, notasDebito.Count(IsApprovedFlag)),
            new DocumentTypeAggregate("Guías de remisión", guiasRemision.Count, guiasRemision.Count(IsAuthorizedGuide)),
            new DocumentTypeAggregate("Retenciones", retenciones.Count, retenciones.Count(IsApprovedFlag)),
            new DocumentTypeAggregate("Liquidaciones", liquidaciones.Count, liquidaciones.Count(x => IsApprovedFlag(x.Autorizado) || IsAuthorizedBySriState(x.EstadoEnvioSri)))
        });

        var documentosEmitidos = documentosPorTipo.Sum(x => x.Count);
        var documentosAutorizados = documentosPorTipo.Sum(x => x.AuthorizedCount);

        return new DashboardSnapshotDto
        {
            LoadedAt = DateTime.Now,
            Stats = new DashboardStatsDto
            {
                FacturasRegistradas = facturasRegistradas,
                FacturasAutorizadas = facturasAutorizadas,
                ClientesActivos = clientesActivos,
                ClientesNuevosMes = clientesNuevosMes,
                ProductosActivos = productosActivos,
                ProductosFacturables = productosFacturables,
                ProductosListosFacturar = productosListosFacturar,
                DocumentosEmitidos = documentosEmitidos,
                DocumentosAutorizados = documentosAutorizados,
                VentasAcumuladas = ventasAcumuladas,
                VentasAnioActual = ventasAnioActual,
                VentasAnioAnterior = ventasAnioAnterior,
                CuentasPorCobrar = carteraAbierta.Sum(f => f.OutstandingAmount),
                CarteraAlDia = carteraAlDia.Sum(f => f.OutstandingAmount),
                CarteraVencida = carteraVencida.Sum(f => f.OutstandingAmount),
                CobradoHistorico = cobradas.Sum(f => f.TotalAbonado > 0m ? Math.Min(f.Total, f.TotalAbonado) : f.Total),
                OpenReceivablesCount = carteraAbierta.Count,
                OverdueReceivablesCount = carteraVencida.Count,
                TicketPromedio = ticketPromedio
            },
            SalesByMonth = BuildMonthlySales(facturas, currentYear, currentYear - 1),
            RecentInvoices = BuildRecentInvoices(facturas),
            DocumentTypes = documentosPorTipo,
            ReceivableCustomers = BuildReceivableCustomers(carteraAbierta)
        };
    }

    public async Task<bool> TieneClientesActivosAsync(int currentUserId)
    {
        if (currentUserId <= 0)
        {
            return false;
        }

        await using var db = await _dbFactory.CreateDbContextAsync();
        var usuariosCuentaIds = await ResolveAccountUserIdsAsync(db, currentUserId);

        return await db.Clientes
            .AsNoTracking()
            .AnyAsync(c => c.Usuario.HasValue
                           && usuariosCuentaIds.Contains(c.Usuario.Value)
                           && (c.Estado == null || c.Estado == true)
                           && c.Numeroidentificacion != ConsumidorFinalIdentificacion);
    }

    public async Task<bool> TieneProductosActivosAsync(int currentUserId)
    {
        if (currentUserId <= 0)
        {
            return false;
        }

        await using var db = await _dbFactory.CreateDbContextAsync();
        var usuariosCuentaIds = await ResolveAccountUserIdsAsync(db, currentUserId);

        return await db.Productos
            .AsNoTracking()
            .AnyAsync(p => p.Idusuario.HasValue
                           && usuariosCuentaIds.Contains(p.Idusuario.Value)
                           && (p.Estado == null || p.Estado == true));
    }

    public async Task<int> GetAuthorizedDocumentCountAsync(int currentUserId)
    {
        if (currentUserId <= 0)
        {
            return 0;
        }

        await using var db = await _dbFactory.CreateDbContextAsync();
        var usuariosCuentaIds = await ResolveAccountUserIdsAsync(db, currentUserId);

        var facturas = await ExecuteSafeAsync(
            async () => await db.Facturas
                .AsNoTracking()
                .Where(f => f.Idusuario.HasValue
                            && usuariosCuentaIds.Contains(f.Idusuario.Value)
                            && (f.CodemisorNavigation == null || f.CodemisorNavigation.EsEmisorSistema != true)
                            && (f.Estado == null || f.Estado == true))
                .Select(f => new InvoiceAuthorizationInfo
                {
                    Autorizado = f.Autorizado == true,
                    EstadoSri = f.Estadoenviosri
                })
                .ToListAsync(),
            new List<InvoiceAuthorizationInfo>());

        var notasCredito = await ExecuteSafeAsync(
            async () => await db.NotaCreditos
                .AsNoTracking()
                .Where(n => n.Usuario.HasValue
                            && usuariosCuentaIds.Contains(n.Usuario.Value)
                            && !db.Emisores.Any(e => e.Codigo == n.CodEmisor && e.EsEmisorSistema)
                            && (n.Estado == null || n.Estado == true))
                .Select(n => n.Autorizado)
                .ToListAsync(),
            new List<string?>());

        var notasDebito = await ExecuteSafeAsync(
            async () => await db.NotaDebitos
                .AsNoTracking()
                .Where(n => n.Usuario.HasValue
                            && usuariosCuentaIds.Contains(n.Usuario.Value)
                            && !db.Emisores.Any(e => e.Codigo == n.CodEmisor && e.EsEmisorSistema))
                .Select(n => n.Autorizado)
                .ToListAsync(),
            new List<string?>());

        var guias = await ExecuteSafeAsync(
            async () => await db.GuiasRemision
                .AsNoTracking()
                .Where(g => g.IdUsuario.HasValue && usuariosCuentaIds.Contains(g.IdUsuario.Value))
                .Where(g => !db.Facturas.Any(f => f.Codfactura == g.Codfactura
                                                 && f.CodemisorNavigation != null
                                                 && f.CodemisorNavigation.EsEmisorSistema))
                .Select(g => g.EstadoSRI)
                .ToListAsync(),
            new List<string?>());

        var retenciones = await ExecuteSafeAsync(
            async () => await db.RetencionInfo
                .AsNoTracking()
                .Where(r => r.Usuario.HasValue
                            && usuariosCuentaIds.Contains(r.Usuario.Value))
                .Select(r => r.Autorizado)
                .ToListAsync(),
            new List<string?>());

        var liquidaciones = await ExecuteSafeAsync(
            async () => await db.ComprasFacturas
                .AsNoTracking()
                .Where(c => c.Usuario.HasValue
                            && usuariosCuentaIds.Contains(c.Usuario.Value)
                            && (c.Estado == null || c.Estado == true))
                .Select(c => new LiquidacionDashboardInfo
                {
                    Autorizado = c.Autorizado,
                    EstadoEnvioSri = c.EstadoEnvioSRI
                })
                .ToListAsync(),
            new List<LiquidacionDashboardInfo>());

        return facturas.Count(f => f.Autorizado || IsAuthorizedBySriState(f.EstadoSri))
               + notasCredito.Count(IsApprovedFlag)
               + notasDebito.Count(IsApprovedFlag)
               + guias.Count(IsAuthorizedGuide)
               + retenciones.Count(IsApprovedFlag)
               + liquidaciones.Count(l => IsApprovedFlag(l.Autorizado) || IsAuthorizedBySriState(l.EstadoEnvioSri));
    }

    public async Task<List<DashboardAuthorizedDocumentDto>> GetRecentAuthorizedDocumentsAsync(int currentUserId, int take = 12)
    {
        if (currentUserId <= 0 || take <= 0)
        {
            return new List<DashboardAuthorizedDocumentDto>();
        }

        await using var db = await _dbFactory.CreateDbContextAsync();
        var usuariosCuentaIds = await ResolveAccountUserIdsAsync(db, currentUserId);
        var documentos = new List<AuthorizedDocumentCandidate>();

        documentos.AddRange(await ExecuteSafeAsync(
            async () => await db.Facturas
                .AsNoTracking()
                .Where(f => f.Idusuario.HasValue
                            && usuariosCuentaIds.Contains(f.Idusuario.Value)
                            && (f.CodemisorNavigation == null || f.CodemisorNavigation.EsEmisorSistema != true)
                            && (f.Estado == null || f.Estado == true))
                .Select(f => new AuthorizedDocumentCandidate
                {
                    DocumentoId = f.Codfactura,
                    Titulo = "Factura autorizada",
                    Numero = f.Numfactura,
                    Serie = f.Serie,
                    Fecha = f.Fchautorizacion ?? f.Fechaentrega,
                    FechaSriTexto = f.Fechaautosri,
                    BanderaAutorizada = f.Autorizado == true,
                    EstadoSri = f.Estadoenviosri,
                    Detalleextra = f.Detalleextra,
                    Ruta = "/facturas",
                    NotificarPendienteAutorizacion = true
                })
                .ToListAsync(),
            new List<AuthorizedDocumentCandidate>()));

        documentos.AddRange(await ExecuteSafeAsync(
            async () => await db.NotaCreditos
                .AsNoTracking()
                .Where(n => n.Usuario.HasValue
                            && usuariosCuentaIds.Contains(n.Usuario.Value)
                            && !db.Emisores.Any(e => e.Codigo == n.CodEmisor && e.EsEmisorSistema)
                            && (n.Estado == null || n.Estado == true))
                .Select(n => new AuthorizedDocumentCandidate
                {
                    DocumentoId = n.Sec,
                    Titulo = "Nota de credito autorizada",
                    Numero = n.NumNotaCredito,
                    Serie = n.Serie,
                    Fecha = n.FchAutorizacion ?? n.FechaEmiDocModificado,
                    FechaSriTexto = n.FechaAutoSri,
                    Autorizado = n.Autorizado,
                    Ruta = "/facturacion/notas-credito-generadas",
                    NotificarPendienteAutorizacion = true
                })
                .ToListAsync(),
            new List<AuthorizedDocumentCandidate>()));

        documentos.AddRange(await ExecuteSafeAsync(
            async () => await db.NotaDebitos
                .AsNoTracking()
                .Where(n => n.Usuario.HasValue
                            && usuariosCuentaIds.Contains(n.Usuario.Value)
                            && !db.Emisores.Any(e => e.Codigo == n.CodEmisor && e.EsEmisorSistema))
                .Select(n => new AuthorizedDocumentCandidate
                {
                    DocumentoId = n.Sec,
                    Titulo = "Nota de debito autorizada",
                    Numero = n.NumNotaDebito,
                    Serie = n.Serie,
                    Fecha = n.FchAutorizacion ?? n.FechaEmiDocModificado,
                    FechaSriTexto = n.FechaAutoSri,
                    Autorizado = n.Autorizado,
                    Ruta = "/facturacion/notas-debito-generadas",
                    NotificarPendienteAutorizacion = true
                })
                .ToListAsync(),
            new List<AuthorizedDocumentCandidate>()));

        documentos.AddRange(await ExecuteSafeAsync(
            async () => await db.GuiasRemision
                .AsNoTracking()
                .Where(g => g.IdUsuario.HasValue && usuariosCuentaIds.Contains(g.IdUsuario.Value))
                .Where(g => !db.Facturas.Any(f => f.Codfactura == g.Codfactura
                                                 && f.CodemisorNavigation != null
                                                 && f.CodemisorNavigation.EsEmisorSistema))
                .Select(g => new AuthorizedDocumentCandidate
                {
                    DocumentoId = g.Sec,
                    Titulo = "Guia de remision autorizada",
                    Numero = g.NumGuiaRemision,
                    Serie = g.Serie,
                    Fecha = g.Fecha,
                    FechaSriTexto = g.FechaAutorizacion,
                    EstadoSri = g.EstadoSRI,
                    Ruta = "/facturacion/guias-remision-generadas",
                    NotificarPendienteAutorizacion = true
                })
                .ToListAsync(),
            new List<AuthorizedDocumentCandidate>()));

        documentos.AddRange(await ExecuteSafeAsync(
            async () => await db.RetencionInfo
                .AsNoTracking()
                .Where(r => r.Usuario.HasValue
                            && usuariosCuentaIds.Contains(r.Usuario.Value))
                .Select(r => new AuthorizedDocumentCandidate
                {
                    DocumentoId = r.Sec,
                    Titulo = "Retencion autorizada",
                    Numero = r.NumRetencion,
                    Serie = r.Serie,
                    Fecha = r.Fecha,
                    FechaSriTexto = r.FechaAutorizaSri,
                    Autorizado = r.Autorizado,
                    EstadoSri = r.Estado,
                    Ruta = "/facturacion/retenciones-generadas",
                    NotificarPendienteAutorizacion = true
                })
                .ToListAsync(),
            new List<AuthorizedDocumentCandidate>()));

        documentos.AddRange(await ExecuteSafeAsync(
            async () => await db.ComprasFacturas
                .AsNoTracking()
                .Where(c => c.Usuario.HasValue
                            && usuariosCuentaIds.Contains(c.Usuario.Value)
                            && (c.Estado == null || c.Estado == true))
                .Select(c => new AuthorizedDocumentCandidate
                {
                    DocumentoId = c.CodFactura,
                    Titulo = "Liquidacion de compra autorizada",
                    Numero = c.NumFactura,
                    Serie = c.Serie,
                    Fecha = c.FchAutorizacion ?? c.FechaRegistro ?? c.FechaEntrega,
                    FechaSriTexto = c.FechaAutoSRI,
                    Autorizado = c.Autorizado,
                    EstadoSri = c.EstadoEnvioSRI,
                    Ruta = "/compras/liquidaciones-generadas",
                    NotificarPendienteAutorizacion = true
                })
                .ToListAsync(),
            new List<AuthorizedDocumentCandidate>()));

        return documentos
            .Select(documento =>
            {
                var estaAutorizado = documento.BanderaAutorizada
                                     || IsApprovedFlag(documento.Autorizado)
                                     || IsAuthorizedBySriState(documento.EstadoSri);
                var pendienteAutorizacion = documento.NotificarPendienteAutorizacion &&
                                            !estaAutorizado &&
                                            documento.Fecha.HasValue &&
                                            documento.Fecha.Value.Date < DateTime.Today;

                return new
                {
                    Documento = documento,
                    EstaAutorizado = estaAutorizado,
                    PendienteAutorizacion = pendienteAutorizacion
                };
            })
            .Where(item => item.EstaAutorizado || item.PendienteAutorizacion)
            .Select(documento => new
            {
                documento.Documento,
                documento.EstaAutorizado,
                documento.PendienteAutorizacion,
                FechaAutorizacion = ParseAuthorizationDate(documento.Documento.FechaSriTexto) ?? documento.Documento.Fecha
            })
            .OrderByDescending(item => item.FechaAutorizacion ?? DateTime.MinValue)
            .ThenByDescending(item => item.Documento.DocumentoId)
            .Take(take)
            .Select(item =>
            {
                var documento = item.Documento;
                var numero = BuildInvoiceNumber(documento.Serie, documento.Numero);
                return new DashboardAuthorizedDocumentDto
                {
                    DocumentoId = documento.DocumentoId,
                    Titulo = item.PendienteAutorizacion
                        ? documento.Titulo.Replace("autorizada", "pendiente", StringComparison.OrdinalIgnoreCase)
                        : documento.Titulo,
                    NumeroDocumento = numero == "-" ? $"#{documento.DocumentoId}" : numero,
                    FechaAutorizacion = item.FechaAutorizacion,
                    Ruta = documento.Ruta,
                    Clave = $"{documento.Ruta}:{documento.DocumentoId}:{(item.PendienteAutorizacion ? "pendiente" : "autorizado")}",
                    EsPendienteAutorizacion = item.PendienteAutorizacion
                };
            })
            .ToList();
    }

    private static DateTime? ParseAuthorizationDate(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        return DateTime.TryParse(value, DashboardCulture, DateTimeStyles.AllowWhiteSpaces, out var localDate)
            ? localDate
            : DateTime.TryParse(value, CultureInfo.InvariantCulture, DateTimeStyles.AllowWhiteSpaces, out var invariantDate)
                ? invariantDate
                : null;
    }

    private static bool DebeMostrarAlertaReenvioFactura(string? detalleextra)
    {
        if (string.IsNullOrWhiteSpace(detalleextra))
            return false;

        try
        {
            using var document = System.Text.Json.JsonDocument.Parse(detalleextra);
            return document.RootElement.TryGetProperty("SriMostrarAlertaPendiente", out var alertNode) &&
                   alertNode.ValueKind == System.Text.Json.JsonValueKind.True;
        }
        catch
        {
            return false;
        }
    }

    private static DashboardInvoiceSnapshot BuildInvoiceSnapshot(InvoiceSource source, decimal totalAbonado)
    {
        var outstandingAmount = BuildOutstandingAmount(source, totalAbonado);

        return new DashboardInvoiceSnapshot
        {
            Codfactura = source.Codfactura,
            Numfactura = source.Numfactura,
            Serie = source.Serie,
            FechaEmision = source.FechaEmision,
            FechaVence = source.FechaVence,
            FechaCancelado = source.FechaCancelado,
            Total = source.Total,
            OutstandingAmount = outstandingAmount,
            TotalAbonado = totalAbonado,
            Autorizado = source.Autorizado,
            EstadoSri = source.EstadoSri,
            EstadoPago = source.EstadoPago,
            Cliente = source.Cliente,
            IdentificacionCliente = source.IdentificacionCliente,
            IsCreditInvoice = string.Equals((source.TipoPago ?? string.Empty).Trim(), "19", StringComparison.OrdinalIgnoreCase)
        };
    }

    private static decimal BuildOutstandingAmount(InvoiceSource source, decimal totalAbonado)
    {
        if (!string.Equals((source.TipoPago ?? string.Empty).Trim(), "19", StringComparison.OrdinalIgnoreCase))
        {
            return 0m;
        }

        if (totalAbonado > 0m)
        {
            return Math.Max(0m, source.Total - totalAbonado);
        }

        if (source.ValorRegistradoPorCobrar.HasValue)
        {
            return Math.Max(0m, source.ValorRegistradoPorCobrar.Value);
        }

        return Math.Max(0m, source.Total);
    }

    private static List<DashboardMonthlySalesPointDto> BuildMonthlySales(
        IEnumerable<DashboardInvoiceSnapshot> facturas,
        int currentYear,
        int previousYear)
    {
        var points = Enumerable.Range(1, 12)
            .Select(month => new DashboardMonthlySalesPointDto
            {
                MonthLabel = DashboardCulture.DateTimeFormat
                    .GetAbbreviatedMonthName(month)
                    .Replace(".", string.Empty)
                    .ToUpperInvariant(),
                CurrentTotal = facturas
                    .Where(f => f.FechaEmision?.Year == currentYear && f.FechaEmision?.Month == month)
                    .Sum(f => f.Total),
                PreviousTotal = facturas
                    .Where(f => f.FechaEmision?.Year == previousYear && f.FechaEmision?.Month == month)
                    .Sum(f => f.Total)
            })
            .ToList();

        var maxValue = Math.Max(points.Max(x => Math.Max(x.CurrentTotal, x.PreviousTotal)), 1m);

        foreach (var point in points)
        {
            point.CurrentHeightPercent = CalculateHeight(point.CurrentTotal, maxValue);
            point.PreviousHeightPercent = CalculateHeight(point.PreviousTotal, maxValue);
        }

        return points;
    }

    private static List<DashboardRecentInvoiceItemDto> BuildRecentInvoices(IEnumerable<DashboardInvoiceSnapshot> facturas) =>
        facturas
            .OrderByDescending(f => f.FechaEmision ?? DateTime.MinValue)
            .ThenByDescending(f => f.Codfactura)
            .Select(f =>
            {
                var isPaid = IsPaidInvoice(f);
                var isOverdue = IsOverdueInvoice(f);
                var sriStatusText = DocumentoAutorizacionHelper.ObtenerEstadoVisual(
                    f.EstadoSri,
                    DocumentoAutorizacionHelper.EstaAutorizado(f.Autorizado, f.EstadoSri));

                return new DashboardRecentInvoiceItemDto
                {
                    Codfactura = f.Codfactura,
                    DisplayNumber = BuildInvoiceNumber(f.Serie, f.Numfactura),
                    ClientName = string.IsNullOrWhiteSpace(f.Cliente) ? "Cliente sin nombre" : f.Cliente.Trim(),
                    ClientDocument = string.IsNullOrWhiteSpace(f.IdentificacionCliente) ? "Sin identificación" : f.IdentificacionCliente.Trim(),
                    DateLabel = f.FechaEmision?.ToString("dd MMM yyyy", DashboardCulture) ?? "Sin fecha",
                    RelativeLabel = string.Empty,
                    SriStatusText = sriStatusText,
                    SriStatusClass = GetSriStatusClass(sriStatusText),
                    Total = f.Total,
                    IsPaid = isPaid,
                    IsOverdue = isOverdue
                };
            })
            .ToList();

    private static List<DashboardDocumentTypeItemDto> BuildDocumentTypeItems(IEnumerable<DocumentTypeAggregate> items)
    {
        var list = items.Select(x => new DashboardDocumentTypeItemDto
            {
                Label = x.Label,
                Count = x.Count,
                AuthorizedCount = x.AuthorizedCount
            })
            .ToList();

        var total = Math.Max(list.Sum(x => x.Count), 1);
        var max = Math.Max(list.Max(x => x.Count), 1);

        foreach (var item in list)
        {
            item.SharePercent = (int)Math.Round((double)item.Count / total * 100d, MidpointRounding.AwayFromZero);
            item.FillPercent = item.Count <= 0 ? 0 : Math.Max(8, (int)Math.Round((double)item.Count / max * 100d, MidpointRounding.AwayFromZero));
        }

        return list;
    }

    private static List<DashboardReceivableCustomerItemDto> BuildReceivableCustomers(IEnumerable<DashboardInvoiceSnapshot> carteraAbierta)
    {
        var items = carteraAbierta
            .GroupBy(f => new
            {
                ClientName = string.IsNullOrWhiteSpace(f.Cliente) ? "Cliente sin nombre" : f.Cliente.Trim(),
                ClientDocument = string.IsNullOrWhiteSpace(f.IdentificacionCliente) ? "Sin identificación" : f.IdentificacionCliente.Trim()
            })
            .Select(g =>
            {
                var dueDates = g.Where(x => x.FechaVence.HasValue).Select(x => x.FechaVence!.Value).OrderBy(x => x).ToList();
                var overdueCount = g.Count(IsOverdueInvoice);
                var openCount = g.Count();

                var statusText = overdueCount > 0
                    ? $"{overdueCount} vencida(s)"
                    : dueDates.Count > 0
                        ? $"Próximo vencimiento {dueDates[0]:dd/MM/yyyy}"
                        : $"{openCount} factura(s) activa(s)";

                return new DashboardReceivableCustomerItemDto
                {
                    ClientName = g.Key.ClientName,
                    ClientDocument = g.Key.ClientDocument,
                    OutstandingAmount = g.Sum(x => x.OutstandingAmount),
                    OpenInvoices = openCount,
                    HasOverdueInvoices = overdueCount > 0,
                    StatusText = statusText
                };
            })
            .OrderByDescending(x => x.OutstandingAmount)
            .ThenByDescending(x => x.OpenInvoices)
            .Take(6)
            .ToList();

        var max = Math.Max(items.Select(x => x.OutstandingAmount).DefaultIfEmpty(0m).Max(), 1m);
        foreach (var item in items)
        {
            item.FillPercent = item.OutstandingAmount <= 0m
                ? 0
                : Math.Max(10, (int)Math.Round((double)(item.OutstandingAmount / max * 100m), MidpointRounding.AwayFromZero));
        }

        return items;
    }

    private static async Task<List<int>> ResolveAccountUserIdsAsync(AppDbContext db, int currentUserId)
    {
        var usuarioCuenta = await db.Usuarios
            .AsNoTracking()
            .Where(u => u.IdUsuario == currentUserId)
            .Select(u => new
            {
                u.idJefe,
                u.estadoAsociado
            })
            .FirstOrDefaultAsync();

        var titularCuentaId = usuarioCuenta is not null
                              && usuarioCuenta.estadoAsociado == true
                              && usuarioCuenta.idJefe is > 0
            ? usuarioCuenta.idJefe.Value
            : currentUserId;

        var usuariosCuentaIds = await db.Usuarios
            .AsNoTracking()
            .Where(u => u.IdUsuario == titularCuentaId || (u.idJefe == titularCuentaId && u.estadoAsociado == true))
            .Select(u => u.IdUsuario)
            .ToListAsync();

        if (!usuariosCuentaIds.Contains(titularCuentaId))
        {
            usuariosCuentaIds.Add(titularCuentaId);
        }

        return usuariosCuentaIds;
    }

    private static async Task<T> ExecuteSafeAsync<T>(Func<Task<T>> action, T fallback)
    {
        try
        {
            return await action();
        }
        catch
        {
            return fallback;
        }
    }

    private static bool IsAuthorizedInvoice(DashboardInvoiceSnapshot factura) =>
        factura.Autorizado || IsAuthorizedBySriState(factura.EstadoSri);

    private static bool IsPaidInvoice(DashboardInvoiceSnapshot factura)
    {
        if (factura.OutstandingAmount <= 0m)
        {
            return true;
        }

        if (factura.Total > 0m && factura.TotalAbonado >= factura.Total)
        {
            return true;
        }

        if (factura.FechaCancelado.HasValue)
        {
            return true;
        }

        if (string.IsNullOrWhiteSpace(factura.EstadoPago))
        {
            return false;
        }

        return factura.EstadoPago.Contains("PAG", StringComparison.OrdinalIgnoreCase)
               || factura.EstadoPago.Contains("CANCEL", StringComparison.OrdinalIgnoreCase)
               || factura.EstadoPago.Contains("COBR", StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsOpenReceivable(DashboardInvoiceSnapshot factura) =>
        factura.IsCreditInvoice && !IsPaidInvoice(factura) && factura.OutstandingAmount > 0m;

    private static bool IsOverdueInvoice(DashboardInvoiceSnapshot factura) =>
        IsOpenReceivable(factura)
        && factura.FechaVence.HasValue
        && factura.FechaVence.Value.Date < DateTime.Today;

    private static bool IsApprovedFlag(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return false;
        }

        var normalized = value.Trim();
        return normalized.Equals("true", StringComparison.OrdinalIgnoreCase)
               || normalized.Equals("1", StringComparison.OrdinalIgnoreCase)
               || normalized.Equals("t", StringComparison.OrdinalIgnoreCase)
               || normalized.Equals("s", StringComparison.OrdinalIgnoreCase)
               || normalized.Equals("si", StringComparison.OrdinalIgnoreCase)
               || normalized.Equals("sí", StringComparison.OrdinalIgnoreCase)
               || normalized.Equals("a", StringComparison.OrdinalIgnoreCase)
               || normalized.Equals("autorizado", StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsAuthorizedGuide(string? estadoSri) => IsAuthorizedBySriState(estadoSri);

    private static bool IsAuthorizedBySriState(string? estadoSri) =>
        DocumentoAutorizacionHelper.EsEstadoAutorizado(estadoSri);

    private static int CalculateHeight(decimal value, decimal maxValue)
    {
        if (value <= 0m || maxValue <= 0m)
        {
            return 6;
        }

        return Math.Max(10, (int)Math.Round((double)(value / maxValue * 100m), MidpointRounding.AwayFromZero));
    }

    private static string BuildInvoiceNumber(string? serie, string? numero)
    {
        var serieLimpia = (serie ?? string.Empty).Replace("-", string.Empty).Trim();
        var numeroLimpio = (numero ?? string.Empty).Trim();

        if (string.IsNullOrWhiteSpace(serieLimpia))
        {
            return string.IsNullOrWhiteSpace(numeroLimpio) ? "-" : numeroLimpio;
        }

        if (serieLimpia.Length == 6)
        {
            serieLimpia = $"{serieLimpia[..3]}-{serieLimpia.Substring(3, 3)}";
        }

        if (string.IsNullOrWhiteSpace(numeroLimpio))
        {
            return serieLimpia;
        }

        return $"{serieLimpia}-{numeroLimpio.PadLeft(9, '0')}";
    }

    private static string BuildInvoiceRelativeLabel(DashboardInvoiceSnapshot factura, bool isPaid, bool isOverdue)
    {
        if (!factura.IsCreditInvoice && isPaid)
        {
            return "Pago inmediato";
        }

        if (isPaid)
        {
            return "Cobro registrado";
        }

        if (isOverdue && factura.FechaVence.HasValue)
        {
            return $"Venció el {factura.FechaVence.Value:dd/MM/yyyy}";
        }

        if (factura.FechaVence.HasValue)
        {
            return $"Vence el {factura.FechaVence.Value:dd/MM/yyyy}";
        }

        if (!factura.FechaEmision.HasValue)
        {
            return "Sin fecha de emisión";
        }

        var dias = (DateTime.Today - factura.FechaEmision.Value.Date).Days;
        if (dias <= 0)
        {
            return "Emitida hoy";
        }

        if (dias == 1)
        {
            return "Emitida ayer";
        }

        return $"Emitida hace {dias} días";
    }

    private static string GetSriStatusText(string? estadoSri) =>
        DocumentoAutorizacionHelper.ObtenerEstadoVisual(
            estadoSri,
            DocumentoAutorizacionHelper.EsEstadoAutorizado(estadoSri));

    private static string GetSriStatusClass(string? estadoSri)
    {
        if (string.IsNullOrWhiteSpace(estadoSri))
        {
            return "status-pill-warning";
        }

        if (DocumentoAutorizacionHelper.EsEstadoAutorizado(estadoSri))
        {
            return "status-pill-success";
        }

        if (DocumentoAutorizacionHelper.EsNoAutorizado(estadoSri))
        {
            return "status-pill-danger";
        }

        return "status-pill-warning";
    }

    private sealed class InvoiceSource
    {
        public int Codfactura { get; init; }
        public string? Numfactura { get; init; }
        public string? Serie { get; init; }
        public DateTime? FechaEmision { get; init; }
        public DateTime? FechaVence { get; init; }
        public DateOnly? FechaCancelado { get; init; }
        public decimal Total { get; init; }
        public decimal? ValorRegistradoPorCobrar { get; init; }
        public bool Autorizado { get; init; }
        public string? EstadoSri { get; init; }
        public string? EstadoPago { get; init; }
        public string? TipoPago { get; init; }
        public string? Cliente { get; init; }
        public string? IdentificacionCliente { get; init; }
    }

    private sealed record DocumentTypeAggregate(string Label, int Count, int AuthorizedCount);

    private sealed class ClienteDashboardInfo
    {
        public DateOnly? FechaIngreso { get; init; }
    }

    private sealed class ProductoDashboardInfo
    {
        public bool Facturable { get; init; }
        public bool TieneImpuesto { get; init; }
        public bool TieneIva { get; init; }
    }

    private sealed class LiquidacionDashboardInfo
    {
        public string? Autorizado { get; init; }
        public string? EstadoEnvioSri { get; init; }
    }

    private sealed class InvoiceAuthorizationInfo
    {
        public bool Autorizado { get; init; }
        public string? EstadoSri { get; init; }
    }

    private sealed class AuthorizedDocumentCandidate
    {
        public int DocumentoId { get; init; }
        public string Titulo { get; init; } = string.Empty;
        public string? Numero { get; init; }
        public string? Serie { get; init; }
        public DateTime? Fecha { get; init; }
        public string? FechaSriTexto { get; init; }
        public bool BanderaAutorizada { get; init; }
        public string? Autorizado { get; init; }
        public string? EstadoSri { get; init; }
        public string? Detalleextra { get; init; }
        public string Ruta { get; init; } = string.Empty;
        public bool NotificarPendienteAutorizacion { get; init; }
    }
}

public sealed class DashboardSnapshotDto
{
    public DateTime LoadedAt { get; init; }
    public DashboardStatsDto Stats { get; init; } = new();
    public List<DashboardMonthlySalesPointDto> SalesByMonth { get; init; } = new();
    public List<DashboardRecentInvoiceItemDto> RecentInvoices { get; init; } = new();
    public List<DashboardDocumentTypeItemDto> DocumentTypes { get; init; } = new();
    public List<DashboardReceivableCustomerItemDto> ReceivableCustomers { get; init; } = new();
}

public sealed class DashboardStatsDto
{
    public int FacturasRegistradas { get; init; }
    public int FacturasAutorizadas { get; init; }
    public int ClientesActivos { get; init; }
    public int ClientesNuevosMes { get; init; }
    public int ProductosActivos { get; init; }
    public int ProductosFacturables { get; init; }
    public int ProductosListosFacturar { get; init; }
    public int DocumentosEmitidos { get; init; }
    public int DocumentosAutorizados { get; init; }
    public decimal VentasAcumuladas { get; init; }
    public decimal VentasAnioActual { get; init; }
    public decimal VentasAnioAnterior { get; init; }
    public decimal CuentasPorCobrar { get; init; }
    public decimal CarteraAlDia { get; init; }
    public decimal CarteraVencida { get; init; }
    public decimal CobradoHistorico { get; init; }
    public int OpenReceivablesCount { get; init; }
    public int OverdueReceivablesCount { get; init; }
    public decimal TicketPromedio { get; init; }
}

public sealed class DashboardMonthlySalesPointDto
{
    public string MonthLabel { get; init; } = string.Empty;
    public decimal CurrentTotal { get; init; }
    public decimal PreviousTotal { get; init; }
    public int CurrentHeightPercent { get; set; }
    public int PreviousHeightPercent { get; set; }
}

public sealed class DashboardRecentInvoiceItemDto
{
    public int Codfactura { get; init; }
    public string DisplayNumber { get; init; } = "-";
    public string ClientName { get; init; } = "Cliente sin nombre";
    public string ClientDocument { get; init; } = "Sin identificación";
    public string DateLabel { get; init; } = "Sin fecha";
    public string RelativeLabel { get; init; } = string.Empty;
    public string SriStatusText { get; init; } = "PENDIENTE";
    public string SriStatusClass { get; init; } = "status-pill-muted";
    public decimal Total { get; init; }
    public bool IsPaid { get; init; }
    public bool IsOverdue { get; init; }
}

public sealed class DashboardAuthorizedDocumentDto
{
    public int DocumentoId { get; init; }
    public string Titulo { get; init; } = string.Empty;
    public string NumeroDocumento { get; init; } = string.Empty;
    public DateTime? FechaAutorizacion { get; init; }
    public string Ruta { get; init; } = string.Empty;
    public string Clave { get; init; } = string.Empty;
    public bool EsPendienteAutorizacion { get; init; }
}

public sealed class DashboardDocumentTypeItemDto
{
    public string Label { get; init; } = string.Empty;
    public int Count { get; init; }
    public int AuthorizedCount { get; init; }
    public int SharePercent { get; set; }
    public int FillPercent { get; set; }
}

public sealed class DashboardReceivableCustomerItemDto
{
    public string ClientName { get; init; } = "Cliente sin nombre";
    public string ClientDocument { get; init; } = "Sin identificación";
    public decimal OutstandingAmount { get; init; }
    public int OpenInvoices { get; init; }
    public bool HasOverdueInvoices { get; init; }
    public string StatusText { get; init; } = string.Empty;
    public int FillPercent { get; set; }
}

public sealed class DashboardInvoiceSnapshot
{
    public int Codfactura { get; init; }
    public string? Numfactura { get; init; }
    public string? Serie { get; init; }
    public DateTime? FechaEmision { get; init; }
    public DateTime? FechaVence { get; init; }
    public DateOnly? FechaCancelado { get; init; }
    public decimal Total { get; init; }
    public decimal OutstandingAmount { get; init; }
    public decimal TotalAbonado { get; init; }
    public bool Autorizado { get; init; }
    public string? EstadoSri { get; init; }
    public string? EstadoPago { get; init; }
    public string? Cliente { get; init; }
    public string? IdentificacionCliente { get; init; }
    public bool IsCreditInvoice { get; init; }
}
