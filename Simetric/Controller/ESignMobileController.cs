using System.Globalization;
using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Simetric.Data;
using Simetric.Models;
using Simetric.Services;
using Simetric.Services.ESign;

namespace Simetric.Controllers;

/// <summary>
/// Fachada de e-Rúbrica para clientes móviles.
/// El usuario se obtiene siempre de la sesión autenticada; no se acepta por query string.
/// </summary>
[Authorize]
[ApiController]
[Route("api/mobile/e-rubrica")]
public sealed class ESignMobileController : ControllerBase
{
    private readonly IDbContextFactory<AppDbContext> _dbFactory;
    private readonly SolicitudService _solicitudService;
    private readonly FirmaRenovacionService _firmaRenovacionService;
    private readonly EmisorCertificadoValidator _certificadoValidator;
    private readonly EmisorCertificadoProtector _certificadoProtector;
    private readonly FirmaPathResolver _firmaPathResolver;
    private readonly UanatacaApiService _uanatacaApiService;
    private readonly FirmaStampApiService _firmaStampApiService;
    private readonly IESignMenuService _eSignMenuService;
    private readonly PagoService _pagoService;
    private readonly IWebHostEnvironment _hostEnvironment;

    public ESignMobileController(
        IDbContextFactory<AppDbContext> dbFactory,
        SolicitudService solicitudService,
        FirmaRenovacionService firmaRenovacionService,
        EmisorCertificadoValidator certificadoValidator,
        EmisorCertificadoProtector certificadoProtector,
        FirmaPathResolver firmaPathResolver,
        UanatacaApiService uanatacaApiService,
        FirmaStampApiService firmaStampApiService,
        IESignMenuService eSignMenuService,
        PagoService pagoService,
        IWebHostEnvironment hostEnvironment)
    {
        _dbFactory = dbFactory;
        _solicitudService = solicitudService;
        _firmaRenovacionService = firmaRenovacionService;
        _certificadoValidator = certificadoValidator;
        _certificadoProtector = certificadoProtector;
        _firmaPathResolver = firmaPathResolver;
        _uanatacaApiService = uanatacaApiService;
        _firmaStampApiService = firmaStampApiService;
        _eSignMenuService = eSignMenuService;
        _pagoService = pagoService;
        _hostEnvironment = hostEnvironment;
    }

    [HttpGet("dashboard")]
    public async Task<IActionResult> Dashboard([FromQuery] int take = 8, CancellationToken cancellationToken = default)
    {
        var userId = GetUserId();
        if (userId <= 0) return Unauthorized();

        take = Math.Clamp(take, 1, 50);
        var solicitudes = await _solicitudService.ObtenerSolicitudesClienteAsync(userId);
        var firmas = await _solicitudService.ObtenerFirmasClienteAsync(userId);
        var notificaciones = await _solicitudService.ObtenerNotificacionesPendientesClienteAsync(userId, take);
        var entregasFirma = await _solicitudService.ObtenerEntregasFirmaPendientesClienteAsync(userId, take);
        var renovacion = await _firmaRenovacionService.ObtenerPorUsuarioAsync(userId, cancellationToken: cancellationToken);
        await using var db = await _dbFactory.CreateDbContextAsync(cancellationToken);
        var idTipoUsuario = await db.Usuarios.AsNoTracking()
            .Where(user => user.IdUsuario == userId)
            .Select(user => user.IdTipoUsuario)
            .FirstOrDefaultAsync(cancellationToken);
        var menus = idTipoUsuario is > 0
            ? (await _eSignMenuService.GetMenusByRol(idTipoUsuario.Value)).Select(menu => new
            {
                id = menu.IdMenu,
                nombre = menu.NombreMenu,
                ruta = menu.RutaMenu,
                icono = menu.IconoMenu,
                orden = menu.OrdenMenu
            })
            : Enumerable.Empty<object>();

        return Ok(new
        {
            solicitudes,
            firmas,
            notificaciones,
            entregasFirma,
            renovacion,
            menus
        });
    }

    [HttpGet("emisores")]
    public async Task<IActionResult> ObtenerEmisores(CancellationToken cancellationToken)
    {
        var idCuenta = await GetAccountIdAsync(cancellationToken);
        if (idCuenta is null) return Unauthorized();

        await using var db = await _dbFactory.CreateDbContextAsync(cancellationToken);
        var emisores = await db.Emisores.AsNoTracking()
            .Where(e => e.Estado && e.IdUsuario == idCuenta)
            .OrderByDescending(e => e.Codigo)
            .ToListAsync(cancellationToken);

        var respuesta = new List<object>();
        foreach (var emisor in emisores)
        {
            var ruta = _firmaPathResolver.ResolverRutaExistente(emisor.PathCertificado);
            var clave = _certificadoProtector.DesprotegerClave(emisor.ClaveCertificado);
            var tieneCertificado = ruta is not null;
            var tieneClave = !string.IsNullOrWhiteSpace(clave);
            var validacion = tieneCertificado && tieneClave
                ? await _certificadoValidator.ValidarConApiAsync(emisor, cancellationToken)
                : null;

            respuesta.Add(new
            {
                id = emisor.Codigo,
                razonSocial = emisor.RazonSocial,
                ruc = emisor.Ruc,
                email = emisor.Email,
                telefono = emisor.Telefono,
                esEmisorSistema = emisor.EsEmisorSistema,
                tieneCertificado,
                tieneClave,
                esValida = validacion?.IsValid ?? false,
                estadoVigencia = validacion?.EstadoVigencia,
                fechaExpiracion = validacion?.FechaExpiracion,
                diasRestantes = validacion?.DiasRestantes,
                mensaje = validacion?.Message
            });
        }

        return Ok(respuesta);
    }

    [HttpGet("emisores/{id:int}/firma/estado")]
    public async Task<IActionResult> ObtenerEstadoFirma(int id, CancellationToken cancellationToken)
    {
        var idCuenta = await GetAccountIdAsync(cancellationToken);
        if (idCuenta is null) return Unauthorized();

        await using var db = await _dbFactory.CreateDbContextAsync(cancellationToken);
        var emisor = await db.Emisores.AsNoTracking()
            .FirstOrDefaultAsync(e => e.Codigo == id && e.Estado && e.IdUsuario == idCuenta, cancellationToken);
        if (emisor is null) return NotFound(new { mensaje = "Emisor no encontrado." });

        var validacion = await _certificadoValidator.ValidarConApiAsync(emisor, cancellationToken);
        return Ok(new
        {
            esValida = validacion.IsValid,
            estadoVigencia = validacion.EstadoVigencia,
            mensaje = validacion.Message,
            nombreTitular = validacion.NombreTitular,
            identificacion = validacion.IdentificacionExtraida,
            fechaEmision = validacion.FechaEmision,
            fechaExpiracion = validacion.FechaExpiracion,
            diasRestantes = validacion.DiasRestantes,
            numeroSerie = validacion.NumeroSerie,
            huellaDigital = validacion.HuellaDigital
        });
    }

    [HttpGet("solicitudes")]
    public async Task<IActionResult> ObtenerSolicitudes(CancellationToken cancellationToken)
    {
        var userId = GetUserId();
        return userId <= 0 ? Unauthorized() : Ok(await _solicitudService.ObtenerSolicitudesClienteAsync(userId));
    }

    [HttpGet("firmas")]
    public async Task<IActionResult> ObtenerFirmas(CancellationToken cancellationToken)
    {
        var userId = GetUserId();
        return userId <= 0 ? Unauthorized() : Ok(await _solicitudService.ObtenerFirmasClienteAsync(userId));
    }

    [HttpGet("renovacion")]
    public async Task<IActionResult> ObtenerRenovacion(CancellationToken cancellationToken)
    {
        var userId = GetUserId();
        return userId <= 0
            ? Unauthorized()
            : Ok(await _firmaRenovacionService.ObtenerPorUsuarioAsync(userId, cancellationToken: cancellationToken));
    }

    [HttpGet("notificaciones")]
    public async Task<IActionResult> ObtenerNotificaciones([FromQuery] int take = 8)
    {
        var userId = GetUserId();
        if (userId <= 0) return Unauthorized();
        return Ok(await _solicitudService.ObtenerNotificacionesPendientesClienteAsync(userId, Math.Clamp(take, 1, 50)));
    }

    [HttpGet("entregas-firma")]
    public async Task<IActionResult> ObtenerEntregasFirma([FromQuery] int take = 8)
    {
        var userId = GetUserId();
        if (userId <= 0) return Unauthorized();
        return Ok(await _solicitudService.ObtenerEntregasFirmaPendientesClienteAsync(userId, Math.Clamp(take, 1, 50)));
    }

    [HttpPost("notificaciones/entregas/{observacionId:int}/vista")]
    public async Task<IActionResult> MarcarEntregaVista(int observacionId)
    {
        var userId = GetUserId();
        if (userId <= 0) return Unauthorized();
        if (observacionId <= 0) return BadRequest(new { mensaje = "La observación no es válida." });

        var actualizado = await _solicitudService.MarcarEntregaFirmaVistaAsync(observacionId, userId);
        return actualizado ? Ok(new { actualizado = true }) : NotFound(new { mensaje = "Entrega no encontrada." });
    }

    [HttpGet("solicitudes/{solId:int}/firma-p12")]
    public async Task<IActionResult> DescargarFirmaP12(int solId)
    {
        var userId = GetUserId();
        if (userId <= 0) return Unauthorized();
        var archivo = await _solicitudService.ObtenerArchivoFirmaP12ClienteAsync(solId, userId);
        return archivo is null
            ? NotFound(new { mensaje = "No existe una firma disponible para la solicitud." })
            : File(archivo.Contenido, "application/x-pkcs12", archivo.NombreArchivo);
    }

    [HttpPost("solicitudes/{solId:int}/sincronizar")]
    public async Task<IActionResult> SincronizarSolicitud(int solId, CancellationToken cancellationToken)
    {
        if (GetUserId() <= 0) return Unauthorized();
        var resultado = await _solicitudService.SincronizarSolicitudClienteUanatacaAsync(solId, GetUserId(), cancellationToken);
        return resultado.Success ? Ok(resultado) : BadRequest(resultado);
    }

    [HttpPost("solicitudes/sincronizar-pendientes")]
    public async Task<IActionResult> SincronizarPendientes(CancellationToken cancellationToken)
    {
        var userId = GetUserId();
        if (userId <= 0) return Unauthorized();
        var resultado = await _solicitudService.SincronizarSolicitudesUanatacaPendientesAsync(userId, cancellationToken);
        return Ok(resultado);
    }

    [HttpPost("solicitudes")]
    [RequestSizeLimit(70 * 1024 * 1024)]
    public async Task<IActionResult> CrearSolicitud([FromForm] MobileSolicitudFirmaRequest request, CancellationToken cancellationToken)
    {
        var userId = GetUserId();
        if (userId <= 0) return Unauthorized();

        var vigencia = NormalizarVigencia(request.Vigencia);
        var subtotal = ESignPricing.ObtenerSubtotal(vigencia);
        if (subtotal <= 0) return BadRequest(new { mensaje = "La vigencia seleccionada no es válida." });

        if (!TryParseDate(request.FechaNacimiento, out var fechaNacimiento))
            return BadRequest(new { mensaje = "La fecha de nacimiento no es válida." });

        var tipoPersona = EsRepresentanteLegal(request.TipoPersona) ? "JURIDICA" : "NATURAL";
        var tieneRuc = tipoPersona == "JURIDICA" || !string.IsNullOrWhiteSpace(request.Ruc);
        var solicitud = new UsuSolicitudFirma
        {
            SolIdUsuarioCliente = userId,
            SolTipoPersona = tipoPersona,
            SolTipoIdentificacion = NormalizarTipoIdentificacion(request.TipoDocumento),
            SolIdentificacion = SoloDigitosOTexto(request.Identificacion),
            SolCodigoDactilar = NormalizarTexto(request.CodigoDactilar),
            SolNombres = NormalizarTexto(request.Nombres),
            SolPrimerApellido = NormalizarTexto(request.PrimerApellido),
            SolSegundoApellido = NormalizarTexto(request.SegundoApellido),
            SolFechaNacimiento = fechaNacimiento,
            SolSexo = NormalizarSexo(request.Sexo),
            SolNacionalidad = string.IsNullOrWhiteSpace(request.Nacionalidad) ? "ECUATORIANA" : NormalizarTexto(request.Nacionalidad),
            SolTelefono1 = NormalizarTexto(request.Celular),
            SolTelefono2 = NormalizarTexto(request.TelefonoSecundario),
            SolCorreo1 = NormalizarTexto(request.Correo).ToLowerInvariant(),
            SolCorreo2 = NormalizarTexto(request.CorreoSecundario).ToLowerInvariant(),
            SolTieneRuc = tieneRuc,
            SolNroRuc = SoloDigitosOTexto(request.Ruc),
            SolProvincia = NormalizarTexto(request.Provincia),
            SolCanton = NormalizarTexto(request.Canton),
            SolDireccion = NormalizarTexto(request.Direccion),
            SolFormatoFirma = "ARCHIVO_P12",
            SolVigencia = vigencia,
            SolMontoPago = subtotal,
            SolCompanyName = NormalizarTexto(request.RazonSocialEmpresa),
            SolDepartment = NormalizarTexto(request.Departamento),
            SolPosition = NormalizarTexto(request.Cargo),
            SolReason = NormalizarTexto(request.MotivoFirma),
            SolIdentificationTypeManager = NormalizarTipoIdentificacion(request.RepresentanteTipoDocumento),
            SolIdentificationManager = SoloDigitosOTexto(request.RepresentanteIdentificacion),
            SolNamesManager = NormalizarTexto(request.RepresentanteNombres),
            SolLastNameManager = NormalizarTexto(request.RepresentanteApellidos),
            SolEsMayor65 = CalcularEdad(fechaNacimiento) >= 65
        };

        var archivos = new List<(string TempFileName, string Tipo)>();
        try
        {
            await AddTempFileAsync(archivos, request.CedulaFrontal, "CEDULA_FRONTAL", DocumentoExtensiones, MaxDocumentoBytes, cancellationToken);
            await AddTempFileAsync(archivos, request.CedulaPosterior, "CEDULA_POSTERIOR", DocumentoExtensiones, MaxDocumentoBytes, cancellationToken);
            await AddTempFileAsync(archivos, request.SelfieCedula, "SELFIE_CEDULA", ImagenExtensiones, MaxDocumentoBytes, cancellationToken);
            await AddTempFileAsync(archivos, request.VideoAceptacion, "VIDEO_ACEPTACION", VideoExtensiones, MaxVideoBytes, cancellationToken);
            await AddTempFileAsync(archivos, request.RucFile, "RUC_FILE", DocumentoExtensiones, MaxDocumentoBytes, cancellationToken);
            await AddTempFileAsync(archivos, request.Nombramiento, "NOMBRAMIENTO", DocumentoExtensiones, MaxDocumentoBytes, cancellationToken);
            await AddTempFileAsync(archivos, request.Constitucion, "CONSTITUCION", DocumentoExtensiones, MaxDocumentoBytes, cancellationToken);
            await AddTempFileAsync(archivos, request.CedulaRepresentante, "CEDULA_REPRESENTANTE", DocumentoExtensiones, MaxDocumentoBytes, cancellationToken);
            await AddTempFileAsync(archivos, request.Autorizacion, "AUTORIZACION", DocumentoExtensiones, MaxDocumentoBytes, cancellationToken);
            await AddTempFileAsync(archivos, request.AceptacionNombramiento, "ACEPTACION_NOMBRAMIENTO", DocumentoExtensiones, MaxDocumentoBytes, cancellationToken);
            await AddTempFileAsync(archivos, request.ArchivoAdicional, "ARCHIVO_ADICIONAL", DocumentoExtensiones, MaxDocumentoBytes, cancellationToken);

            var ok = await _solicitudService.CrearSolicitudFullAsync(solicitud, archivos);
            if (!ok)
                return BadRequest(new { mensaje = _solicitudService.UltimoErrorCrearSolicitud ?? "No se pudo crear la solicitud." });

            return Ok(CrearSolicitudResponse(solicitud));
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new { mensaje = ex.Message });
        }
    }

    [HttpPost("solicitudes/pago")]
    public async Task<IActionResult> CrearPagoSolicitud([FromBody] MobileSolicitudPagoRequest request)
    {
        var userId = GetUserId();
        if (userId <= 0) return Unauthorized();
        if (request.SolicitudId <= 0) return BadRequest(new { mensaje = "La solicitud no es válida." });

        await using var db = await _dbFactory.CreateDbContextAsync();
        var solicitud = await db.UsuSolicitudFirma
            .FirstOrDefaultAsync(item => item.SolId == request.SolicitudId && item.SolIdUsuarioCliente == userId && item.SolActivo);
        if (solicitud is null) return NotFound(new { mensaje = "Solicitud no encontrada." });
        if (solicitud.SolPagoExitoso == true) return BadRequest(new { mensaje = "La solicitud ya tiene un pago aprobado." });

        var notifyUrl = $"{Request.Scheme}://{Request.Host}/api/pagomedios/notificacion";
        var urlPago = await _pagoService.GenerarSolicitudPago(solicitud, notifyUrl);
        return string.IsNullOrWhiteSpace(urlPago)
            ? BadRequest(new { mensaje = "No se pudo conectar con la pasarela de pagos." })
            : Ok(new { solicitudId = solicitud.SolId, paymentUrl = urlPago, checkoutUrl = urlPago, status = "PENDIENTE_PAGO" });
    }

    [HttpPost("solicitudes/transferencia")]
    [RequestSizeLimit(8 * 1024 * 1024)]
    public async Task<IActionResult> RegistrarTransferenciaSolicitud([FromForm] MobileSolicitudTransferenciaRequest request, CancellationToken cancellationToken)
    {
        var userId = GetUserId();
        if (userId <= 0) return Unauthorized();
        if (request.SolicitudId <= 0) return BadRequest(new { mensaje = "La solicitud no es válida." });
        if (string.IsNullOrWhiteSpace(request.Banco) || string.IsNullOrWhiteSpace(request.TitularCuenta) ||
            string.IsNullOrWhiteSpace(request.CuentaOrigen) || string.IsNullOrWhiteSpace(request.NumeroComprobante))
            return BadRequest(new { mensaje = "Completa los datos de la transferencia." });
        if (request.Comprobante is null || !EsArchivoImagen(request.Comprobante, 5 * 1024 * 1024))
            return BadRequest(new { mensaje = "Adjunta un comprobante JPG o PNG de hasta 5 MB." });

        await using var db = await _dbFactory.CreateDbContextAsync(cancellationToken);
        var solicitud = await db.UsuSolicitudFirma
            .FirstOrDefaultAsync(item => item.SolId == request.SolicitudId && item.SolIdUsuarioCliente == userId && item.SolActivo, cancellationToken);
        if (solicitud is null) return NotFound(new { mensaje = "Solicitud no encontrada." });
        if (solicitud.SolPagoExitoso == true) return BadRequest(new { mensaje = "La solicitud ya tiene un pago aprobado." });

        var marcador = $"[SolicitudFirma:{solicitud.SolId}]";
        var pendienteExistente = await db.ReporteVentasBackOffice
            .AnyAsync(v => v.Estado == "pendiente" && v.Observacion != null && v.Observacion.Contains(marcador), cancellationToken);
        if (pendienteExistente) return BadRequest(new { mensaje = "Ya existe una transferencia pendiente para esta solicitud." });

        byte[] comprobanteBytes;
        await using (var stream = request.Comprobante.OpenReadStream())
        using (var memory = new MemoryStream())
        {
            await stream.CopyToAsync(memory, cancellationToken);
            comprobanteBytes = memory.ToArray();
        }

        var venta = new ReporteVentaBackOffice
        {
            Cliente = Truncar($"{solicitud.SolNombres} {solicitud.SolPrimerApellido} | {solicitud.SolCorreo1}", 150),
            Producto = "e-sign",
            PlanPaquete = Truncar($"Firma electrónica {solicitud.SolVigencia}", 100),
            Valor = decimal.Round((solicitud.SolMontoPago ?? 0m) * (1m + ESignPricing.IvaRate), 2, MidpointRounding.AwayFromZero),
            Fecha = DateTime.Now,
            Canal = "Transferencia Móvil",
            Vendedor = "Solicitud móvil",
            Estado = "pendiente",
            FormaPago = "Transferencia Bancaria",
            Observacion = Truncar($"{marcador} Banco: {request.Banco.Trim()}. Cuenta origen: {SoloDigitosOTexto(request.CuentaOrigen)}. N. comprobante: {NormalizarTexto(request.NumeroComprobante)}. Titular: {NormalizarTexto(request.TitularCuenta)}. Solicitud pendiente de aprobación por compra de firma electrónica.", 500),
            ComprobanteArchivo = comprobanteBytes
        };

        db.ReporteVentasBackOffice.Add(venta);
        await db.SaveChangesAsync(cancellationToken);
        solicitud.SolIdTransaccionPago = Truncar($"TRANSFERENCIA-PENDIENTE-MOVIL-{venta.IdReporte}", 100);
        solicitud.SolFechaActualizacion = DateTime.Now;
        await db.SaveChangesAsync(cancellationToken);

        return Ok(new { solicitudId = solicitud.SolId, status = "TRANSFERENCIA_REGISTRADA" });
    }

    [HttpGet("catalogos/productos")]
    public async Task<IActionResult> ObtenerProductos(CancellationToken cancellationToken) =>
        Ok(await _uanatacaApiService.ObtenerProductosAsync(cancellationToken));

    [HttpGet("catalogos/stakeholder-productos")]
    public async Task<IActionResult> ObtenerProductosStakeholder([FromQuery] string? stakeholderUuid, CancellationToken cancellationToken) =>
        Ok(await _uanatacaApiService.ObtenerProductosStakeholderAsync(stakeholderUuid, cancellationToken));

    [HttpGet("catalogos/saldo")]
    public async Task<IActionResult> ObtenerSaldo(CancellationToken cancellationToken) =>
        Ok(new { balance = await _uanatacaApiService.ObtenerSaldoAsync(cancellationToken) });

    [HttpGet("proveedor/solicitudes")]
    public async Task<IActionResult> BuscarSolicitudesProveedor(
        [FromQuery] string? q,
        [FromQuery] string? status,
        [FromQuery] string? uuid,
        CancellationToken cancellationToken) =>
        Ok(await _uanatacaApiService.BuscarSolicitudesAsync(q, status, uuid, cancellationToken));

    [HttpPost("documentos/firmar")]
    [RequestSizeLimit(20 * 1024 * 1024)]
    public async Task<IActionResult> FirmarDocumento(
        [FromForm] IFormFile? pdf,
        [FromForm] IFormFile? certificado,
        [FromForm] string? clave,
        [FromForm] int? idEmisor,
        [FromForm] string? razon,
        [FromForm] string? ubicacion,
        [FromForm] int pagina = 1,
        [FromForm] double xMm = 20,
        [FromForm] double yMm = 20,
        [FromForm] double anchoMm = 50,
        CancellationToken cancellationToken = default)
    {
        var userId = GetUserId();
        if (userId <= 0) return Unauthorized();
        if (pdf is null || !EsArchivo(pdf, ".pdf", 15 * 1024 * 1024))
            return BadRequest(new { mensaje = "Debes enviar un archivo PDF válido de hasta 15 MB." });
        if (pagina <= 0 || xMm < 0 || yMm < 0 || anchoMm <= 0)
            return BadRequest(new { mensaje = "La posición y el tamaño de la firma no son válidos." });

        byte[] certificadoBytes;
        string nombreCertificado;
        string claveFirma;

        var usaFirmaManual = certificado is not null && certificado.Length > 0 ||
                             !string.IsNullOrWhiteSpace(clave);
        if (usaFirmaManual)
        {
            if (certificado is null || string.IsNullOrWhiteSpace(clave))
                return BadRequest(new { mensaje = "Si envías una firma manual debes enviar el certificado .p12 y su clave." });
            if (!EsArchivo(certificado, ".p12", 5 * 1024 * 1024))
                return BadRequest(new { mensaje = "El certificado no tiene un formato o tamaño válido." });

            await using var certificadoStream = certificado.OpenReadStream();
            using var certificadoBuffer = new MemoryStream();
            await certificadoStream.CopyToAsync(certificadoBuffer, cancellationToken);
            certificadoBytes = certificadoBuffer.ToArray();
            nombreCertificado = Path.GetFileName(certificado.FileName);
            claveFirma = clave.Trim();
        }
        else
        {
            var firmaConfigurada = await CargarFirmaConfiguradaAsync(idEmisor, cancellationToken);
            if (firmaConfigurada is null)
                return BadRequest(new { mensaje = "No existe una firma electrónica configurada para tu cuenta. Carga el certificado .p12 desde e-Fact." });

            certificadoBytes = firmaConfigurada.Contenido;
            nombreCertificado = firmaConfigurada.NombreArchivo;
            claveFirma = firmaConfigurada.Clave;
        }

        await using var pdfStream = pdf.OpenReadStream();

        var resultado = await _firmaStampApiService.EstamparAsync(
            pdfStream,
            Path.GetFileName(pdf.FileName),
            pdf.Length,
            pdf.ContentType,
            new FirmaStampApiFile(certificadoBytes, nombreCertificado, "application/x-pkcs12"),
            claveFirma,
            razon,
            ubicacion,
            pagina,
            xMm,
            yMm,
            anchoMm,
            cancellationToken);

        return resultado.Success && resultado.Pdf is not null
            ? File(resultado.Pdf, resultado.ContentType ?? "application/pdf", $"{Path.GetFileNameWithoutExtension(pdf.FileName)}-firmado.pdf")
            : BadRequest(new { mensaje = resultado.Message, estado = resultado.HttpStatusCode });
    }

    private async Task<FirmaConfigurada?> CargarFirmaConfiguradaAsync(
        int? idEmisor,
        CancellationToken cancellationToken)
    {
        var idCuenta = await GetAccountIdAsync(cancellationToken);
        if (idCuenta is null)
            return null;

        await using var db = await _dbFactory.CreateDbContextAsync(cancellationToken);
        var consulta = db.Emisores.AsNoTracking()
            .Where(e => e.Estado && e.IdUsuario == idCuenta);

        if (idEmisor is > 0)
            consulta = consulta.Where(e => e.Codigo == idEmisor.Value);

        var emisores = await consulta
            .OrderByDescending(e => e.Codigo)
            .ToListAsync(cancellationToken);
        var emisor = emisores.FirstOrDefault(e =>
            _firmaPathResolver.ResolverRutaExistente(e.PathCertificado) is not null &&
            !string.IsNullOrWhiteSpace(_certificadoProtector.DesprotegerClave(e.ClaveCertificado)));
        if (emisor is null)
            return null;

        var ruta = _firmaPathResolver.ResolverRutaExistente(emisor.PathCertificado);
        var clave = _certificadoProtector.DesprotegerClave(emisor.ClaveCertificado)?.Trim();
        if (string.IsNullOrWhiteSpace(ruta) || string.IsNullOrWhiteSpace(clave))
            return null;

        var contenido = await System.IO.File.ReadAllBytesAsync(ruta, cancellationToken);
        if (contenido.Length == 0 || contenido.Length > 5 * 1024 * 1024)
            return null;

        return new FirmaConfigurada(contenido, Path.GetFileName(ruta), clave);
    }

    private sealed record FirmaConfigurada(byte[] Contenido, string NombreArchivo, string Clave);

    [HttpPost("documentos/validar-firma")]
    [RequestSizeLimit(10 * 1024 * 1024)]
    public async Task<IActionResult> ValidarFirmaPdf([FromForm] IFormFile? pdf, CancellationToken cancellationToken = default)
    {
        if (GetUserId() <= 0) return Unauthorized();
        if (pdf is null || !EsArchivo(pdf, ".pdf", 10 * 1024 * 1024))
            return BadRequest(new { mensaje = "Debes enviar un archivo PDF válido de hasta 10 MB." });

        await using var stream = pdf.OpenReadStream();
        var resultado = await _firmaStampApiService.ValidarFirmaPdfAsync(
            stream, Path.GetFileName(pdf.FileName), pdf.ContentType, cancellationToken);
        return resultado.Success ? Ok(resultado) : BadRequest(resultado);
    }

    [HttpGet("documentos/validar-qr")]
    public async Task<IActionResult> ValidarQr([FromQuery] string entrada, CancellationToken cancellationToken)
    {
        if (GetUserId() <= 0) return Unauthorized();
        if (string.IsNullOrWhiteSpace(entrada)) return BadRequest(new { mensaje = "La entrada QR es obligatoria." });
        var resultado = await _firmaStampApiService.ValidarQrAsync(entrada, cancellationToken);
        return resultado.Success ? Ok(resultado) : BadRequest(resultado);
    }

    private const long MaxDocumentoBytes = 10 * 1024 * 1024;
    private const long MaxVideoBytes = 50 * 1024 * 1024;
    private static readonly string[] DocumentoExtensiones = [".jpg", ".jpeg", ".png", ".pdf"];
    private static readonly string[] ImagenExtensiones = [".jpg", ".jpeg", ".png"];
    private static readonly string[] VideoExtensiones = [".mp4", ".mov", ".avi", ".webm"];

    private async Task AddTempFileAsync(
        List<(string TempFileName, string Tipo)> archivos,
        IFormFile? archivo,
        string tipo,
        IReadOnlyCollection<string> extensionesPermitidas,
        long maxBytes,
        CancellationToken cancellationToken)
    {
        if (archivo is null || archivo.Length == 0)
            return;

        var extension = Path.GetExtension(archivo.FileName);
        if (!extensionesPermitidas.Contains(extension, StringComparer.OrdinalIgnoreCase))
            throw new InvalidOperationException($"El archivo {tipo} tiene una extensión no permitida.");

        if (archivo.Length > maxBytes)
            throw new InvalidOperationException($"El archivo {tipo} supera el tamaño permitido.");

        var tempDir = Path.Combine(_hostEnvironment.WebRootPath, "uploads", "temp");
        Directory.CreateDirectory(tempDir);

        var tempFileName = $"{tipo}_{Guid.NewGuid():N}{extension.ToLowerInvariant()}";
        var path = Path.Combine(tempDir, tempFileName);
        await using var input = archivo.OpenReadStream();
        await using var output = System.IO.File.Create(path);
        await input.CopyToAsync(output, cancellationToken);
        archivos.Add((tempFileName, tipo));
    }

    private static object CrearSolicitudResponse(UsuSolicitudFirma solicitud)
    {
        var subtotal = solicitud.SolMontoPago ?? 0m;
        var iva = Math.Round(subtotal * 0.15m, 2, MidpointRounding.AwayFromZero);
        return new
        {
            solicitudId = solicitud.SolId,
            status = "CREADA",
            vigencia = solicitud.SolVigencia,
            subtotal,
            iva,
            total = subtotal + iva
        };
    }

    private static bool TryParseDate(string? value, out DateTime date)
    {
        var text = NormalizarTexto(value);
        string[] formats = ["yyyy-MM-dd", "dd/MM/yyyy", "MM/dd/yyyy"];
        return DateTime.TryParseExact(text, formats, CultureInfo.InvariantCulture, DateTimeStyles.None, out date)
            || DateTime.TryParse(text, CultureInfo.GetCultureInfo("es-EC"), DateTimeStyles.None, out date)
            || DateTime.TryParse(text, CultureInfo.InvariantCulture, DateTimeStyles.None, out date);
    }

    private static int CalcularEdad(DateTime fechaNacimiento)
    {
        var today = DateTime.Today;
        var edad = today.Year - fechaNacimiento.Year;
        if (fechaNacimiento.Date > today.AddYears(-edad)) edad--;
        return edad;
    }

    private static string NormalizarVigencia(string? value)
    {
        var text = NormalizarTexto(value).ToUpperInvariant()
            .Replace("AÑOS", "ANIOS", StringComparison.OrdinalIgnoreCase)
            .Replace("AÑO", "ANIO", StringComparison.OrdinalIgnoreCase);

        if (text.Contains("30")) return "30 DIAS";
        if (text.Contains("7")) return "7 DIAS";
        if (text.Contains("5")) return "5 ANIOS";
        if (text.Contains("4")) return "4 ANIOS";
        if (text.Contains("3")) return "3 ANIOS";
        if (text.Contains("2")) return "2 ANIOS";
        if (text.Contains("1")) return "1 ANIO";
        return text;
    }

    private static string NormalizarTipoIdentificacion(string? value)
    {
        var text = NormalizarTexto(value).ToUpperInvariant();
        return text.Contains("PAS") ? "PASAPORTE" : "CEDULA";
    }

    private static bool EsRepresentanteLegal(string? value)
    {
        var text = NormalizarTexto(value).ToUpperInvariant();
        return text.Contains("REPRESENTANTE") || text.Contains("JURIDICA") || text.Contains("JURÍDICA");
    }

    private static string NormalizarSexo(string? value)
    {
        var text = NormalizarTexto(value).ToUpperInvariant();
        if (text.StartsWith("F", StringComparison.OrdinalIgnoreCase)) return "F";
        if (text.StartsWith("M", StringComparison.OrdinalIgnoreCase)) return "M";
        return text;
    }

    private static string SoloDigitosOTexto(string? value)
    {
        var text = NormalizarTexto(value).ToUpperInvariant();
        return new string(text.Where(character => char.IsLetterOrDigit(character) || character == '-').ToArray());
    }

    private static string NormalizarTexto(string? value) =>
        string.Join(' ', (value ?? string.Empty).Trim().Split(' ', StringSplitOptions.RemoveEmptyEntries));

    private static bool EsArchivoImagen(IFormFile archivo, long maxBytes) =>
        archivo.Length > 0 && archivo.Length <= maxBytes &&
        ImagenExtensiones.Contains(Path.GetExtension(archivo.FileName), StringComparer.OrdinalIgnoreCase);

    private static string Truncar(string? value, int maxLength)
    {
        var text = NormalizarTexto(value);
        return text.Length <= maxLength ? text : text[..maxLength];
    }

    private static bool EsArchivo(IFormFile archivo, string extension, long maxBytes) =>
        archivo.Length > 0 && archivo.Length <= maxBytes &&
        string.Equals(Path.GetExtension(archivo.FileName), extension, StringComparison.OrdinalIgnoreCase);

    private int GetUserId()
    {
        var value = User.FindFirst("IdUsuario")?.Value ?? User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
        return int.TryParse(value, out var id) ? id : 0;
    }

    private async Task<int?> GetAccountIdAsync(CancellationToken cancellationToken)
    {
        var userId = GetUserId();
        if (userId <= 0) return null;

        await using var db = await _dbFactory.CreateDbContextAsync(cancellationToken);
        var user = await db.Usuarios.AsNoTracking()
            .Where(u => u.IdUsuario == userId)
            .Select(u => new { u.IdUsuario, u.idJefe, u.estadoAsociado })
            .FirstOrDefaultAsync(cancellationToken);

        return user is null ? null : user.estadoAsociado == true && user.idJefe > 0 ? user.idJefe : user.IdUsuario;
    }

    public sealed class MobileSolicitudPagoRequest
    {
        public int SolicitudId { get; set; }
    }

    public sealed class MobileSolicitudTransferenciaRequest
    {
        public int SolicitudId { get; set; }
        public string? Banco { get; set; }
        public string? TitularCuenta { get; set; }
        public string? CuentaOrigen { get; set; }
        public string? NumeroComprobante { get; set; }
        public IFormFile? Comprobante { get; set; }
    }

    public sealed class MobileSolicitudFirmaRequest
    {
        public string? Vigencia { get; set; }
        public string? TipoPersona { get; set; }
        public string? TipoDocumento { get; set; }
        public string? Identificacion { get; set; }
        public string? CodigoDactilar { get; set; }
        public string? Ruc { get; set; }
        public string? Nombres { get; set; }
        public string? PrimerApellido { get; set; }
        public string? SegundoApellido { get; set; }
        public string? FechaNacimiento { get; set; }
        public string? Sexo { get; set; }
        public string? Nacionalidad { get; set; }
        public string? Celular { get; set; }
        public string? Correo { get; set; }
        public string? TelefonoSecundario { get; set; }
        public string? CorreoSecundario { get; set; }
        public string? Provincia { get; set; }
        public string? Canton { get; set; }
        public string? Direccion { get; set; }
        public string? RazonSocialEmpresa { get; set; }
        public string? Departamento { get; set; }
        public string? Cargo { get; set; }
        public string? MotivoFirma { get; set; }
        public string? RepresentanteTipoDocumento { get; set; }
        public string? RepresentanteIdentificacion { get; set; }
        public string? RepresentanteNombres { get; set; }
        public string? RepresentanteApellidos { get; set; }
        public IFormFile? CedulaFrontal { get; set; }
        public IFormFile? CedulaPosterior { get; set; }
        public IFormFile? SelfieCedula { get; set; }
        public IFormFile? VideoAceptacion { get; set; }
        public IFormFile? RucFile { get; set; }
        public IFormFile? Nombramiento { get; set; }
        public IFormFile? Constitucion { get; set; }
        public IFormFile? CedulaRepresentante { get; set; }
        public IFormFile? Autorizacion { get; set; }
        public IFormFile? AceptacionNombramiento { get; set; }
        public IFormFile? ArchivoAdicional { get; set; }
    }
}
