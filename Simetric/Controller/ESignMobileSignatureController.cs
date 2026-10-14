using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Simetric.Data;
using Simetric.Services;
using Simetric.Services.ESign;

namespace Simetric.Controllers;

/// <summary>Configuración segura del certificado de firma desde la aplicación móvil.</summary>
[Authorize]
[ApiController]
[Route("api/mobile/e-rubrica/emisores")]
public sealed class ESignMobileSignatureController : ControllerBase
{
    private const long MaxCertificadoSize = 5 * 1024 * 1024;
    private readonly IDbContextFactory<AppDbContext> _dbFactory;
    private readonly IWebHostEnvironment _hostEnvironment;
    private readonly EmisorCertificadoValidator _certificadoValidator;
    private readonly FirmaConfiguracionService _firmaConfiguracionService;

    public ESignMobileSignatureController(
        IDbContextFactory<AppDbContext> dbFactory,
        IWebHostEnvironment hostEnvironment,
        EmisorCertificadoValidator certificadoValidator,
        FirmaConfiguracionService firmaConfiguracionService)
    {
        _dbFactory = dbFactory;
        _hostEnvironment = hostEnvironment;
        _certificadoValidator = certificadoValidator;
        _firmaConfiguracionService = firmaConfiguracionService;
    }

    [HttpPost("firma/configurar")]
    [RequestSizeLimit(MaxCertificadoSize + 64 * 1024)]
    public async Task<IActionResult> ConfigurarFirmaCuenta(
        [FromForm] IFormFile? certificado,
        [FromForm] string? clave,
        CancellationToken cancellationToken)
    {
        if (certificado is null || certificado.Length <= 0 || certificado.Length > MaxCertificadoSize ||
            !string.Equals(Path.GetExtension(certificado.FileName), ".p12", StringComparison.OrdinalIgnoreCase))
            return BadRequest(new { mensaje = "Debes enviar un certificado .p12 válido de hasta 5 MB." });
        if (string.IsNullOrWhiteSpace(clave) || clave.Trim().Length > 50)
            return BadRequest(new { mensaje = "La clave del certificado es obligatoria y no puede exceder 50 caracteres." });

        var accountId = await GetAccountIdAsync(cancellationToken);
        if (accountId is null) return Unauthorized();

        var relativePath = $"certs/path/{Guid.NewGuid():N}.p12";
        var physicalPath = Path.Combine(_hostEnvironment.ContentRootPath, "App_Data", relativePath.Replace('/', Path.DirectorySeparatorChar));
        Directory.CreateDirectory(Path.GetDirectoryName(physicalPath)!);
        var persistida = false;

        try
        {
            await using (var output = System.IO.File.Create(physicalPath))
                await certificado.CopyToAsync(output, cancellationToken);

            var validacion = await _certificadoValidator.ConsultarArchivoConApiAsync(relativePath, clave.Trim(), cancellationToken);
            if (!validacion.Success || validacion.Info is null ||
                !validacion.Info.EsValida || !validacion.Info.TieneClavePrivada)
            {
                return BadRequest(new
                {
                    mensaje = validacion.Info is not null && !validacion.Info.TieneClavePrivada
                        ? "El archivo no contiene la clave privada requerida para firmar."
                        : string.IsNullOrWhiteSpace(validacion.Message)
                        ? "El archivo o la clave no son válidos, o la firma no está vigente."
                        : validacion.Message
                });
            }

            await _firmaConfiguracionService.GuardarAsync(accountId.Value, relativePath, clave.Trim());
            persistida = true;
            var detalle = validacion.Info;
            return Ok(new
            {
                mensaje = "Firma configurada correctamente.",
                esValida = true,
                detalle.FechaExpiracion,
                detalle.DiasRestantes,
                detalle.NombreTitular
            });
        }
        finally
        {
            if (System.IO.File.Exists(physicalPath) && !persistida)
                System.IO.File.Delete(physicalPath);
        }
    }

    [HttpPost("{id:int}/firma/configurar")]
    [RequestSizeLimit(MaxCertificadoSize + 64 * 1024)]
    public Task<IActionResult> ConfigurarFirma(
        int id,
        [FromForm] IFormFile? certificado,
        [FromForm] string? clave,
        CancellationToken cancellationToken) =>
        ConfigurarFirmaCuenta(certificado, clave, cancellationToken);

    [HttpPost("firma/validar-temporal")]
    [RequestSizeLimit(MaxCertificadoSize + 64 * 1024)]
    public async Task<IActionResult> ValidarFirmaTemporal(
        [FromForm] IFormFile? archivo,
        [FromForm] string? clave,
        CancellationToken cancellationToken)
    {
        if (archivo is null || archivo.Length <= 0 || archivo.Length > MaxCertificadoSize ||
            !string.Equals(Path.GetExtension(archivo.FileName), ".p12", StringComparison.OrdinalIgnoreCase))
            return BadRequest(new { mensaje = "Debes enviar un certificado .p12 válido de hasta 5 MB." });
        if (string.IsNullOrWhiteSpace(clave) || clave.Trim().Length > 50)
            return BadRequest(new { mensaje = "La clave del certificado es obligatoria y no puede exceder 50 caracteres." });

        var accountId = await GetAccountIdAsync(cancellationToken);
        if (accountId is null) return Unauthorized();

        var relativePath = $"certs/temp/{Guid.NewGuid():N}.p12";
        var physicalPath = Path.Combine(_hostEnvironment.ContentRootPath, "App_Data", relativePath.Replace('/', Path.DirectorySeparatorChar));
        Directory.CreateDirectory(Path.GetDirectoryName(physicalPath)!);

        FirmaInfoApiResult apiResult;
        try
        {
            await using (var output = System.IO.File.Create(physicalPath))
            {
                await archivo.CopyToAsync(output, cancellationToken);
                await output.FlushAsync(cancellationToken);
            }
            apiResult = await _certificadoValidator.ConsultarArchivoConApiAsync(relativePath, clave.Trim(), cancellationToken);
        }
        finally
        {
            if (System.IO.File.Exists(physicalPath)) System.IO.File.Delete(physicalPath);
        }

        if (!apiResult.Success || apiResult.Info is null)
            return BadRequest(new { mensaje = string.IsNullOrWhiteSpace(apiResult.Message) ? "La API no pudo validar la firma y la clave." : apiResult.Message });

        var detalle = apiResult.Info;
        var identificacion = string.IsNullOrWhiteSpace(detalle.Ruc) ? detalle.Cedula : detalle.Ruc;
        var esValida = detalle.EsValida;
        return Ok(new
        {
            esValida,
            coincideCuenta = true,
            mensaje = !detalle.EsValida
                ? "El archivo o la clave no son válidos, o la firma no está vigente."
                : "La firma y la clave son correctas. El archivo no fue guardado.",
            detalle.EstadoVigencia,
            detalle.NombreTitular,
            identificacion,
            emisor = detalle.EmisorCertificado,
            detalle.NumeroSerie,
            detalle.HuellaDigital,
            detalle.FechaEmision,
            detalle.FechaExpiracion,
            diasRestantes = detalle.FechaExpiracion is null ? (int?)null : (detalle.FechaExpiracion.Value.Date - DateTimeOffset.Now.Date).Days
        });
    }

    private int GetUserId()
    {
        var value = User.FindFirstValue("IdUsuario") ?? User.FindFirstValue(ClaimTypes.NameIdentifier);
        return int.TryParse(value, out var id) ? id : 0;
    }

    private async Task<int?> GetAccountIdAsync(CancellationToken cancellationToken)
    {
        var userId = GetUserId();
        if (userId <= 0) return null;
        await using var db = await _dbFactory.CreateDbContextAsync(cancellationToken);
        var user = await db.Usuarios.AsNoTracking().Where(u => u.IdUsuario == userId)
            .Select(u => new { u.IdUsuario, u.idJefe, u.estadoAsociado }).FirstOrDefaultAsync(cancellationToken);
        return user is null ? null : user.estadoAsociado == true && user.idJefe > 0 ? user.idJefe : user.IdUsuario;
    }

}
