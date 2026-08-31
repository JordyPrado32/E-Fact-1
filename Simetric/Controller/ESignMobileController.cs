using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Simetric.Data;
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

    public ESignMobileController(
        IDbContextFactory<AppDbContext> dbFactory,
        SolicitudService solicitudService,
        FirmaRenovacionService firmaRenovacionService,
        EmisorCertificadoValidator certificadoValidator,
        EmisorCertificadoProtector certificadoProtector,
        FirmaPathResolver firmaPathResolver,
        UanatacaApiService uanatacaApiService,
        FirmaStampApiService firmaStampApiService,
        IESignMenuService eSignMenuService)
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
}
