using Microsoft.EntityFrameworkCore;
using Simetric.Data;
using Simetric.DTOs;
using Simetric.Models;

using Microsoft.Extensions.DependencyInjection;
using Simetric.Models.Glogales;

namespace Simetric.Services;

public class RetencionGeneradaService
{
    private readonly IDbContextFactory<AppDbContext> _dbFactory;
    private readonly Microsoft.AspNetCore.Hosting.IWebHostEnvironment _env;
    private readonly IRetencionPdfService _retencionPdfService;
    private readonly ComprobanteRetencionGenerator _retencionXmlGenerator;
    private readonly SriXmlProcessorService _sriXmlProcessorService;
    private readonly IServiceScopeFactory _serviceScopeFactory;

    public RetencionGeneradaService(
        IDbContextFactory<AppDbContext> dbFactory,
        Microsoft.AspNetCore.Hosting.IWebHostEnvironment env,
        IRetencionPdfService retencionPdfService,
        ComprobanteRetencionGenerator retencionXmlGenerator,
        SriXmlProcessorService sriXmlProcessorService,
        IServiceScopeFactory serviceScopeFactory)
    {
        _dbFactory = dbFactory;
        _env = env;
        _retencionPdfService = retencionPdfService;
        _retencionXmlGenerator = retencionXmlGenerator;
        _sriXmlProcessorService = sriXmlProcessorService;
        _serviceScopeFactory = serviceScopeFactory;
    }

    public async Task<List<RetencionGeneradaListDto>> ListarRetencionesUsuarioAsync(int idUsuario)
    {
        using var db = await _dbFactory.CreateDbContextAsync();
        var totalesRet = await db.ComprasRetValor
            .AsNoTracking()
            .Where(x => x.IdRetencionInfo != null)
            .GroupBy(x => x.IdRetencionInfo!.Value)
            .Select(g => new
            {
                IdRetencionInfo = g.Key,
                BaseTotal = g.Sum(x => x.Base ?? 0m),
                TotalRetenido = g.Sum(x => x.ValorRetenido ?? 0m)
            })
            .ToListAsync();

        var mapaTotales = totalesRet.ToDictionary(
            x => x.IdRetencionInfo,
            x => new { x.BaseTotal, x.TotalRetenido });

        var data = await (
            from r in db.RetencionInfo.AsNoTracking()
            join c in db.ComprasFacturas.AsNoTracking()
                on r.IcCompra equals c.CodFactura into compraJoin
            from c in compraJoin.DefaultIfEmpty()

            join p in db.Proveedores.AsNoTracking()
                on r.IdCliente equals p.ruc into provJoin
            from p in provJoin.DefaultIfEmpty()

            join ti in db.Identificacion.AsNoTracking()
                on p.tipoIdentificacion equals ti.IdeCodigo into tipoJoin
            from ti in tipoJoin.DefaultIfEmpty()

            where r.Usuario == idUsuario
            orderby r.Sec descending
            select new
            {
                r.Sec,
                r.NumRetencion,
                r.Serie,
                r.Fecha,
                r.IdCliente,
                r.Clave,
                r.NumAutorizacion,
                r.Autorizado,
                r.NombreXml,
                r.Estado,
                r.Mensaje,
                NumFactura = c != null ? c.NumFactura : "",
                SerieFactura = c != null ? c.Serie : "",
                NombreProveedor = p != null
                    ? (!string.IsNullOrWhiteSpace(p.nombre)
                        ? p.nombre
                        : ((p.primerNombre ?? "") + " " + (p.segundoNombre ?? "") + " " + (p.primerApellido ?? "") + " " + (p.segundoApellido ?? "")).Trim())
                    : (r.IdCliente ?? ""),
                TipoIdentificacionProveedor = ti != null ? (ti.IdeDescripcion ?? "") : ""
            })
            .ToListAsync();

        var resultado = data.Select(x =>
        {
            mapaTotales.TryGetValue(x.Sec, out var total);

            return new RetencionGeneradaListDto
            {
                Sec = x.Sec,
                NumeroRetencion = x.NumRetencion ?? "",
                Serie = x.Serie ?? "",
                Fecha = x.Fecha,
                Proveedor = x.NombreProveedor ?? "",
                IdentificacionProveedor = x.IdCliente ?? "",
                TipoIdentificacionProveedor = x.TipoIdentificacionProveedor,
                DocumentoSustento = FormatearDocumento(x.SerieFactura, x.NumFactura),
                Clave = x.Clave ?? "",
                NumeroAutorizacion = x.NumAutorizacion ?? "",
                Autorizado = x.Autorizado ?? "",
                Estado = x.Estado ?? "",
                Mensaje = x.Mensaje ?? "",
                BaseTotal = total?.BaseTotal ?? 0m,
                TotalRetenido = total?.TotalRetenido ?? 0m,
                XmlUrl = !string.IsNullOrWhiteSpace(x.NombreXml)
                    ? $"/comprobantes/generados/{x.NombreXml}"
                    : ""
            };
        }).ToList();

        return resultado;
    }

    public Task<RetencionGeneradaDetalleViewDto?> GetRetencionDetalleAsync(int sec)
        => GetRetencionDetalleCoreAsync(sec, null);

    public async Task<RetencionGeneradaDetalleViewDto?> GetRetencionDetalleUsuarioAsync(int sec, int idUsuario)
        => await GetRetencionDetalleCoreAsync(sec, idUsuario);

    public async Task<string?> AsegurarXmlRetencionUsuarioAsync(int sec, int idUsuario)
        => await AsegurarXmlRetencionCoreAsync(sec, idUsuario);

    public async Task<string?> AsegurarXmlRetencionAsync(int sec)
        => await AsegurarXmlRetencionCoreAsync(sec, null);

    public async Task<string?> AsegurarPdfRetencionAsync(int sec, FormatoImpresionDocumento formato = FormatoImpresionDocumento.A4)
        => await AsegurarPdfRetencionCoreAsync(sec, null, formato);

    public async Task<string?> AsegurarPdfRetencionUsuarioAsync(int sec, int idUsuario, FormatoImpresionDocumento formato = FormatoImpresionDocumento.A4)
        => await AsegurarPdfRetencionCoreAsync(sec, idUsuario, formato);

    public async Task<mensajeSRI> EmitirRetencionSriAsync(int sec, int? idUsuario = null, bool intentarEnviarCorreo = true)
    {
        using var db = await _dbFactory.CreateDbContextAsync();
        var retencion = await db.RetencionInfo.FirstOrDefaultAsync(x => x.Sec == sec);
        if (retencion == null)
        {
            return new mensajeSRI
            {
                estado = "ERROR",
                mensaje = "No se encontró la retención para enviar al SRI."
            };
        }

        if (idUsuario.HasValue && retencion.Usuario != idUsuario.Value)
        {
            return new mensajeSRI
            {
                estado = "ERROR",
                mensaje = "La retención no pertenece al usuario actual."
            };
        }

        if (DocumentoAutorizacionHelper.EstaAutorizado(retencion.Autorizado, retencion.Estado))
        {
            if (intentarEnviarCorreo)
                await IntentarEnviarRetencionPorCorreoAsync(sec);

            return new mensajeSRI
            {
                estado = DocumentoAutorizacionHelper.EstadoAutorizado,
                autorizacion = retencion.NumAutorizacion ?? string.Empty,
                fecha = retencion.FechaAutorizaSri ?? string.Empty,
                mensaje = "La retención ya se encuentra autorizada."
            };
        }

        var detalle = await GetRetencionDetalleCoreAsync(sec, idUsuario);
        if (detalle == null)
        {
            return new mensajeSRI
            {
                estado = "ERROR",
                mensaje = "No se pudo construir el detalle de la retención para el envío al SRI."
            };
        }

        var emisor = detalle.Emisor;
        if (emisor == null)
        {
            await ActualizarAutorizacionRetencionAsync(
                sec,
                string.Empty,
                DateTime.Now.ToString("O"),
                "No se encontró el emisor de la retención.",
                "ERROR INTERNO");

            return new mensajeSRI
            {
                estado = "ERROR",
                mensaje = "No se encontró el emisor asociado a la retención."
            };
        }

        if (string.IsNullOrWhiteSpace(emisor.PathCertificado) || string.IsNullOrWhiteSpace(emisor.ClaveCertificado))
        {
            await ActualizarAutorizacionRetencionAsync(
                sec,
                string.Empty,
                DateTime.Now.ToString("O"),
                "El emisor no tiene configurada una firma electrónica válida para emitir la retención.",
                "ERROR INTERNO");

            return new mensajeSRI
            {
                estado = "ERROR",
                mensaje = "El emisor no tiene configurado el certificado electrónico requerido para el envío al SRI."
            };
        }

        var rutaXml = await AsegurarRutaXmlRetencionLocalAsync(detalle);
        if (string.IsNullOrWhiteSpace(rutaXml) || !File.Exists(rutaXml))
        {
            await ActualizarAutorizacionRetencionAsync(
                sec,
                string.Empty,
                DateTime.Now.ToString("O"),
                "No se pudo generar el XML de la retención para el envío al SRI.",
                "ERROR INTERNO");

            return new mensajeSRI
            {
                estado = "ERROR",
                mensaje = "No se pudo generar el XML de la retención para el envío al SRI."
            };
        }

        var respuestaSri = await _sriXmlProcessorService.ProcessXmlAsync(
            rutaXml,
            emisor.PathCertificado,
            emisor.ClaveCertificado);

        var fechaRespuesta = string.IsNullOrWhiteSpace(respuestaSri.fecha)
            ? DateTime.Now.ToString("O")
            : respuestaSri.fecha;

        if (string.Equals(respuestaSri.estado, DocumentoAutorizacionHelper.EstadoAutorizado, StringComparison.OrdinalIgnoreCase) ||
            !string.IsNullOrWhiteSpace(respuestaSri.autorizacion))
        {
            await ActualizarAutorizacionRetencionAsync(
                sec,
                respuestaSri.autorizacion ?? string.Empty,
                fechaRespuesta,
                "ok",
                DocumentoAutorizacionHelper.EstadoAutorizado);

            if (idUsuario is > 0)
                await AsegurarPdfRetencionUsuarioAsync(sec, idUsuario.Value);
            else
                await AsegurarPdfRetencionAsync(sec);

            if (intentarEnviarCorreo)
                await IntentarEnviarRetencionPorCorreoAsync(sec, rutaXml);

            return respuestaSri;
        }

        await ActualizarAutorizacionRetencionAsync(
            sec,
            respuestaSri.autorizacion ?? string.Empty,
            fechaRespuesta,
            string.IsNullOrWhiteSpace(respuestaSri.mensaje) ? respuestaSri.estado : respuestaSri.mensaje,
            string.IsNullOrWhiteSpace(respuestaSri.estado) ? DocumentoAutorizacionHelper.EstadoPendiente : respuestaSri.estado);

        return respuestaSri;
    }

    public async Task<FacturaCorreoEnvioResultadoDto> IntentarEnviarRetencionPorCorreoAsync(int sec, string? rutaXmlExistente = null)
    {
        using var scope = _serviceScopeFactory.CreateScope();
        var correoService = scope.ServiceProvider.GetRequiredService<RetencionCorreoService>();
        return await correoService.IntentarEnviarRetencionPorCorreoAsync(sec, rutaXmlExistente);
    }

    private async Task<string?> AsegurarXmlRetencionCoreAsync(int sec, int? idUsuario)
    {
        var detalle = idUsuario.HasValue
            ? await GetRetencionDetalleUsuarioAsync(sec, idUsuario.Value)
            : await GetRetencionDetalleAsync(sec);

        if (detalle?.RetencionInfo == null || string.IsNullOrWhiteSpace(detalle.Emisor?.Ruc))
            return null;

        var estaAutorizada = DocumentoAutorizacionHelper.EstaAutorizado(
            detalle.RetencionInfo.Autorizado,
            detalle.RetencionInfo.Estado);
        var nombreArchivo = estaAutorizada
            ? ResolverNombreArchivoXml(detalle.RetencionInfo)
            : await GenerarXmlRetencionAsync(detalle);

        return string.IsNullOrWhiteSpace(nombreArchivo)
            ? null
            : ConstruirXmlUrl(nombreArchivo);
    }

    private async Task<string?> AsegurarPdfRetencionCoreAsync(int sec, int? idUsuario, FormatoImpresionDocumento formato = FormatoImpresionDocumento.A4)
    {
        var detalle = idUsuario.HasValue
            ? await GetRetencionDetalleUsuarioAsync(sec, idUsuario.Value)
            : await GetRetencionDetalleAsync(sec);

        if (detalle?.RetencionInfo == null || string.IsNullOrWhiteSpace(detalle.Emisor?.Ruc))
            return null;

        var rutaPdf = await _retencionPdfService.GenerarPdfRetencionAsync(detalle, formato);

        if (string.IsNullOrWhiteSpace(rutaPdf) || !File.Exists(rutaPdf))
            return null;

        return ConstruirPdfUrlDesdeRutaLocal(rutaPdf);
    }

    private async Task<RetencionGeneradaDetalleViewDto?> GetRetencionDetalleCoreAsync(int sec, int? idUsuario)
    {
        using var db = await _dbFactory.CreateDbContextAsync();
        var queryCabecera = db.RetencionInfo
            .AsNoTracking()
            .Where(x => x.Sec == sec);

        if (idUsuario.HasValue)
            queryCabecera = queryCabecera.Where(x => x.Usuario == idUsuario.Value);

        var cabecera = await queryCabecera.FirstOrDefaultAsync();

        if (cabecera == null)
            return null;

        ComprasFactura? compra = null;
        if (cabecera.IcCompra.HasValue)
        {
            compra = await db.ComprasFacturas
                .AsNoTracking()
                .FirstOrDefaultAsync(x => x.CodFactura == cabecera.IcCompra.Value);
        }

        Proveedor? proveedor = null;
        if (!string.IsNullOrWhiteSpace(cabecera.IdCliente))
        {
            proveedor = await db.Proveedores
                .AsNoTracking()
                .FirstOrDefaultAsync(x => x.ruc == cabecera.IdCliente);
        }

        string tipoIdentificacionProveedor = "";

        if (proveedor != null && !string.IsNullOrWhiteSpace(proveedor.tipoIdentificacion))
        {
            var tipo = await db.Identificacion
                .AsNoTracking()
                .FirstOrDefaultAsync(x => x.IdeCodigo == proveedor.tipoIdentificacion);

            tipoIdentificacionProveedor = tipo?.IdeDescripcion ?? "";
        }

        Emisor? emisor = null;

        if (compra?.CodEmisor is > 0)
        {
            emisor = await db.Emisores
                .AsNoTracking()
                .FirstOrDefaultAsync(x => x.Codigo == compra.CodEmisor.Value);
        }

        var usuarioEmisor = emisor == null
            ? await ResolveEmisorOwnerUserIdAsync(db, cabecera.Usuario)
            : null;

        if (usuarioEmisor.HasValue)
        {
            emisor = await db.Emisores
                .AsNoTracking()
                .Where(x => x.Estado && x.IdUsuario == usuarioEmisor.Value)
                .OrderByDescending(x => x.Codigo)
                .FirstOrDefaultAsync();
        }

        if (emisor == null)
        {
            emisor = await db.Emisores
                .AsNoTracking()
                .FirstOrDefaultAsync(x =>
                    x.IdEmpresa == cabecera.IdEmpresa &&
                    x.IdSucursal == cabecera.IdSucursal &&
                    x.Estado);
        }

        var detalleDb = await db.ComprasRetValor
            .AsNoTracking()
            .Where(x => x.IdRetencionInfo == sec)
            .ToListAsync();

        detalleDb = detalleDb
            .OrderBy(x => OrdenTipoRetencion(x.Tipo))
            .ThenBy(x => x.IdRet)
            .ToList();

        var ivaMap = await db.Set<RetencionIva>()
            .AsNoTracking()
            .ToDictionaryAsync(x => x.Codigo, x => x.Descripcion ?? "");

        var isdMap = await db.Set<RetencionIsd>()
            .AsNoTracking()
            .ToDictionaryAsync(x => x.Codigo, x => x.Descripcion ?? "");

        var rentaMap = await db.Set<RetencionRenta>()
            .AsNoTracking()
            .ToDictionaryAsync(x => x.Codigo, x => x.Descripcion ?? "");

        var lineas = new List<RetencionGeneradaDetalleLineaDto>();
        var codigosRenta = new Queue<string?>(
        [
            cabecera.IdRetRenta,
            cabecera.IdRetRenta1
        ]);
        var codigosIva = new Queue<string?>(
        [
            cabecera.IdRetIva,
            cabecera.IdRetIva1
        ]);

        foreach (var x in detalleDb)
        {
            var tipo = (x.Tipo ?? "").Trim().ToUpperInvariant();
            var codigoTexto = tipo switch
            {
                "RENTA" => codigosRenta.Count > 0 ? (codigosRenta.Dequeue() ?? string.Empty).Trim() : (x.IdRet?.ToString() ?? ""),
                "IVA" => codigosIva.Count > 0 ? (codigosIva.Dequeue() ?? string.Empty).Trim() : (x.IdRet?.ToString() ?? ""),
                _ => x.IdRet?.ToString() ?? ""
            };

            string descripcion = tipo switch
            {
                "IVA" => (x.IdRet.HasValue && ivaMap.TryGetValue(x.IdRet.Value, out var descIva)) ? descIva : "",
                "ISD" => (x.IdRet.HasValue && isdMap.TryGetValue(x.IdRet.Value, out var descIsd)) ? descIsd : "",
                "RENTA" => rentaMap.TryGetValue(codigoTexto, out var descRenta) ? descRenta : "",
                _ => ""
            };

            lineas.Add(new RetencionGeneradaDetalleLineaDto
            {
                Tipo = tipo,
                CodigoRetencion = codigoTexto,
                Descripcion = descripcion,
                BaseImponible = x.Base ?? 0m,
                PorcentajeRetener = x.PorcentajeRetencion ?? x.Valor ?? 0m,
                ValorRetenido = x.ValorRetenido ?? 0m
            });
        }

        return new RetencionGeneradaDetalleViewDto
        {
            RetencionInfo = cabecera,
            Compra = compra,
            Proveedor = proveedor,
            Emisor = emisor,
            TipoIdentificacionProveedor = tipoIdentificacionProveedor,
            NumeroCompleto = FormatearDocumento(cabecera.Serie, cabecera.NumRetencion),
            DocumentoSustentoVisual = compra != null
                ? FormatearDocumento(compra.Serie, compra.NumFactura)
                : "",
            FechaEmisionDocumentoSustento = compra?.FchAutorizacion ?? compra?.FechaEntrega ?? compra?.FechaRegistro,
            XmlUrl = !string.IsNullOrWhiteSpace(cabecera.NombreXml)
                ? $"/comprobantes/generados/{cabecera.NombreXml}"
                : "",
            BaseTotal = lineas.Sum(x => x.BaseImponible),
            TotalRetenido = lineas.Sum(x => x.ValorRetenido),
            Retenciones = lineas
        };
    }

    private static string FormatearDocumento(string? serie, string? numero)
    {
        var s = (serie ?? "").Replace("-", "").Trim();
        var n = new string((numero ?? string.Empty).Where(char.IsDigit).ToArray());
        if (n.Length > 9)
            n = n[^9..];
        n = string.IsNullOrWhiteSpace(n) ? "000000000" : n.PadLeft(9, '0');

        if (s.Length == 6)
            return $"{s[..3]}-{s.Substring(3, 3)}-{n}";

        return n;
    }

    private async Task<string?> GenerarXmlRetencionAsync(RetencionGeneradaDetalleViewDto detalle)
    {
        var preview = ConstruirPreviewRetencion(detalle);
        await _retencionXmlGenerator.GenerarXmlDinamicamenteAsync(preview);

        if (string.IsNullOrWhiteSpace(preview.NombreArchivoRetencion))
            return null;

        using var db = await _dbFactory.CreateDbContextAsync();
        var retencion = await db.RetencionInfo.FirstOrDefaultAsync(x => x.Sec == detalle.RetencionInfo.Sec);
        if (retencion != null)
        {
            retencion.Clave = preview.ClaveAccesoRetencion;
            retencion.NombreXml = preview.NombreArchivoRetencion;
            await db.SaveChangesAsync();
        }

        return preview.NombreArchivoRetencion;
    }

    private async Task<string?> AsegurarRutaXmlRetencionLocalAsync(RetencionGeneradaDetalleViewDto detalle)
    {
        var estaAutorizada = DocumentoAutorizacionHelper.EstaAutorizado(
            detalle.RetencionInfo.Autorizado,
            detalle.RetencionInfo.Estado);
        var nombreArchivo = estaAutorizada
            ? ResolverNombreArchivoXml(detalle.RetencionInfo)
            : await GenerarXmlRetencionAsync(detalle);

        if (string.IsNullOrWhiteSpace(nombreArchivo))
            return null;

        var rutaXml = Path.Combine(ObtenerWebRootPath(), "comprobantes", "generados", nombreArchivo);
        return File.Exists(rutaXml) ? rutaXml : null;
    }

    private static CompraXmlPreviewDto ConstruirPreviewRetencion(RetencionGeneradaDetalleViewDto detalle)
    {
        var serie = (detalle.RetencionInfo.Serie ?? detalle.Compra?.Serie ?? string.Empty).Replace("-", "").Trim();
        var estab = serie.Length >= 3 ? serie[..3] : "001";
        var ptoEmi = serie.Length >= 6 ? serie.Substring(3, 3) : "001";
        var proveedor = detalle.Proveedor;
        var emisor = detalle.Emisor;
        var compra = detalle.Compra;
        var retencionInfo = detalle.RetencionInfo;
        var tipoIdentificacion = !string.IsNullOrWhiteSpace(proveedor?.tipoIdentificacion)
            ? proveedor!.tipoIdentificacion!
            : (!string.IsNullOrWhiteSpace(retencionInfo.TipoIdentificacion) ? retencionInfo.TipoIdentificacion! : "04");

        return new CompraXmlPreviewDto
        {
            Usuario = retencionInfo.Usuario,
            Ambiente = retencionInfo.Ambiente ?? compra?.Ambiente ?? 1,
            ClaveAcceso = compra?.CodClave ?? string.Empty,
            ClaveAccesoRetencion = string.Empty,
            NombreArchivoRetencion = string.Empty,
            Estab = estab,
            PtoEmi = ptoEmi,
            Secuencial = compra?.NumFactura ?? string.Empty,
            RucProveedor = proveedor?.ruc ?? retencionInfo.IdCliente ?? string.Empty,
            RazonSocialProveedor = ConstruirNombreProveedor(proveedor, retencionInfo.IdCliente),
            DireccionProveedor = proveedor?.direccion ?? string.Empty,
            TelefonoProveedor = proveedor?.telefonoMovil ?? proveedor?.telefono ?? string.Empty,
            TelefonoFijoProveedor = proveedor?.telefono ?? string.Empty,
            EmailProveedor = proveedor?.email ?? string.Empty,
            RucEmisor = emisor?.Ruc ?? string.Empty,
            DireccionMatriz = emisor?.DireccionMatriz ?? string.Empty,
            DireccionEstablecimiento = emisor?.DirEstablecimiento ?? emisor?.DireccionMatriz ?? string.Empty,
            IdentificacionComprador = emisor?.Ruc ?? string.Empty,
            TipoIdentificacionComprador = tipoIdentificacion,
            ObligadoContabilidad = emisor?.LlevaContabilidad ?? "NO",
            FechaEmision = retencionInfo.Fecha ?? compra?.FechaEntrega ?? compra?.FchAutorizacion ?? DateTime.Today,
            FechaEmisionDocumentoSustento = compra?.FchAutorizacion ?? compra?.FechaEntrega ?? retencionInfo.Fecha ?? DateTime.Today,
            TotalSinImpuestos = compra?.Subtotal ?? detalle.BaseTotal,
            TotalDescuento = compra?.Descuentos ?? 0m,
            ImporteTotal = compra?.ValorTotal ?? detalle.BaseTotal,
            Moneda = "DOLAR",
            FormaPago = compra?.TipoPago ?? "20",
            GuiaRemision = compra?.GuiaRemision ?? string.Empty,
            NumeroRetencionGenerado = retencionInfo.NumRetencion ?? string.Empty,
            NumeroAutorizacion = compra?.NumAutorizacion ?? retencionInfo.NumAutorizacion ?? string.Empty,
            FechaAutorizacionSri = compra?.FechaAutoSRI ?? retencionInfo.FechaAutorizaSri ?? string.Empty,
            NombreEmisorEncontrado = emisor?.RazonSocial ?? emisor?.NomComercial ?? string.Empty,
            Subtotal12 = compra?.Subtotal12 ?? compra?.SubDoceTotal ?? 0m,
            Subtotal0 = compra?.Subtotal0 ?? compra?.SubCeroTotal ?? 0m,
            NoImp = compra?.NoImp ?? compra?.SubNoImpTotal ?? 0m,
            ExIva = compra?.ExIva ?? compra?.SubExIvaTotal ?? 0m,
            Iva = compra?.Iva ?? 0m,
            Retenciones = detalle.Retenciones
                .Select(x => new CompraRetValorDto
                {
                    Tipo = x.Tipo,
                    IdRet = ParseIdRetencion(x.CodigoRetencion),
                    CodigoRetencion = x.CodigoRetencion,
                    Valor = x.PorcentajeRetener,
                    Base = x.BaseImponible,
                    PorcentajeRetencion = x.PorcentajeRetener,
                    ValorRetenido = x.ValorRetenido,
                    Estado = true,
                    Serie = serie,
                    Autorizacion = compra?.NumAutorizacion ?? retencionInfo.NumAutorizacion
                })
                .ToList()
        };
    }

    private static string ConstruirNombreProveedor(Proveedor? proveedor, string? fallback)
    {
        if (proveedor == null)
            return fallback ?? string.Empty;

        if (!string.IsNullOrWhiteSpace(proveedor.nombre))
            return proveedor.nombre;

        var nombre = string.Join(" ", new[]
        {
            proveedor.primerNombre,
            proveedor.segundoNombre,
            proveedor.primerApellido,
            proveedor.segundoApellido
        }.Where(x => !string.IsNullOrWhiteSpace(x))).Trim();

        return string.IsNullOrWhiteSpace(nombre) ? (fallback ?? string.Empty) : nombre;
    }

    private static async Task<int?> ResolveEmisorOwnerUserIdAsync(AppDbContext context, int? idUsuario)
    {
        if (!idUsuario.HasValue || idUsuario.Value <= 0)
            return idUsuario;

        var usuario = await context.Usuarios
            .AsNoTracking()
            .FirstOrDefaultAsync(x => x.IdUsuario == idUsuario.Value);

        if (usuario == null)
            return idUsuario;

        return usuario.estadoAsociado == true && usuario.idJefe is > 0
            ? usuario.idJefe.Value
            : idUsuario.Value;
    }

    private static int? ParseIdRetencion(string? codigo)
    {
        if (string.IsNullOrWhiteSpace(codigo))
            return null;

        return int.TryParse(codigo.Trim(), out var valor) ? valor : null;
    }

    private static int OrdenTipoRetencion(string? tipo)
    {
        return (tipo ?? string.Empty).Trim().ToUpperInvariant() switch
        {
            "RENTA" => 0,
            "IVA" => 1,
            "ISD" => 2,
            _ => 3
        };
    }

    private string? ResolverNombreArchivoXml(RetencionInfo retencion)
    {
        if (!string.IsNullOrWhiteSpace(retencion.NombreXml))
        {
            var rutaPorNombre = Path.Combine(ObtenerWebRootPath(), "comprobantes", "generados", retencion.NombreXml);
            if (File.Exists(rutaPorNombre))
                return retencion.NombreXml;
        }

        if (!string.IsNullOrWhiteSpace(retencion.Clave))
        {
            var nombreFallback = $"RET_{retencion.Clave}.xml";
            var rutaFallback = Path.Combine(ObtenerWebRootPath(), "comprobantes", "generados", nombreFallback);
            if (File.Exists(rutaFallback))
                return nombreFallback;
        }

        return null;
    }

    private static string ConstruirXmlUrl(string nombreArchivo)
        => $"/comprobantes/generados/{nombreArchivo}";

    private async Task ActualizarAutorizacionRetencionAsync(int sec, string? numeroAutorizacion, string? fechaAutorizacion, string? mensaje, string? estadoSri)
    {
        using var db = await _dbFactory.CreateDbContextAsync();
        var retencion = await db.RetencionInfo.FirstOrDefaultAsync(x => x.Sec == sec);
        if (retencion == null)
            return;

        retencion.NumAutorizacion = string.IsNullOrWhiteSpace(numeroAutorizacion)
            ? retencion.NumAutorizacion
            : numeroAutorizacion;
        retencion.FechaAutorizaSri = string.IsNullOrWhiteSpace(fechaAutorizacion)
            ? retencion.FechaAutorizaSri
            : fechaAutorizacion;
        retencion.Mensaje = string.IsNullOrWhiteSpace(mensaje)
            ? retencion.Mensaje
            : mensaje;
        retencion.Estado = ResolverEstadoSriRetencion(estadoSri, retencion.Estado);
        retencion.Autorizado = ResolverBanderaAutorizacionRetencion(estadoSri, retencion.Autorizado);

        await db.SaveChangesAsync();
    }

    private string ConstruirPdfRutaLocal(string? ruc, string? numeroRetencion, int sec, FormatoImpresionDocumento formato = FormatoImpresionDocumento.A4)
    {
        var carpeta = Path.Combine(ObtenerWebRootPath(), "retenciones");
        Directory.CreateDirectory(carpeta);
        return Path.Combine(carpeta, ConstruirNombreArchivoPdf(ruc, numeroRetencion, sec, formato));
    }

    private static string ConstruirPdfUrl(string? ruc, string? numeroRetencion, int sec, FormatoImpresionDocumento formato = FormatoImpresionDocumento.A4)
        => $"/retenciones/{ConstruirNombreArchivoPdf(ruc, numeroRetencion, sec, formato)}";

    private string ConstruirPdfUrlDesdeRutaLocal(string rutaPdf)
    {
        var webRoot = ObtenerWebRootPath();
        var relativa = Path.GetRelativePath(webRoot, rutaPdf).Replace(Path.DirectorySeparatorChar, '/');
        return "/" + relativa.TrimStart('/');
    }

    private static string ConstruirNombreArchivoPdf(string? ruc, string? numeroRetencion, int sec, FormatoImpresionDocumento formato = FormatoImpresionDocumento.A4)
    {
        var rucSeguro = LimpiarSegmentoArchivo(ruc, "retencion");
        var numeroSeguro = LimpiarSegmentoArchivo(numeroRetencion, sec.ToString()).PadLeft(9, '0');
        return $"{rucSeguro}_07_{numeroSeguro}{formato.ObtenerSufijoArchivo()}.pdf";
    }

    private string ObtenerWebRootPath()
    {
        if (!string.IsNullOrWhiteSpace(_env.WebRootPath))
            return _env.WebRootPath;

        return Path.Combine(Directory.GetCurrentDirectory(), "wwwroot");
    }

    private static string LimpiarSegmentoArchivo(string? valor, string reemplazo)
    {
        var limpio = string.IsNullOrWhiteSpace(valor) ? reemplazo : valor.Trim();

        foreach (var caracter in Path.GetInvalidFileNameChars())
            limpio = limpio.Replace(caracter, '_');

        return limpio.Replace(" ", "_");
    }

    private static string? ResolverEstadoSriRetencion(string? estadoSri, string? estadoActual)
    {
        if (string.IsNullOrWhiteSpace(estadoSri))
            return estadoActual;

        var valor = estadoSri.Trim();
        if (valor is "0" or "1")
            return estadoActual;

        return valor;
    }

    private static string? ResolverBanderaAutorizacionRetencion(string? estadoSri, string? valorActual)
    {
        if (string.IsNullOrWhiteSpace(estadoSri))
            return valorActual;

        var valor = estadoSri.Trim();
        return DocumentoAutorizacionHelper.EsEstadoAutorizado(valor) || DocumentoAutorizacionHelper.EsBanderaAutorizada(valor)
            ? "1"
            : "0";
    }
}
