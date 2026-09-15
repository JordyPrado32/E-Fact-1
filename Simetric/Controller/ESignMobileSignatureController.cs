using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Simetric.Data;
using Simetric.Services;

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
    private readonly EmisorCertificadoProtector _certificadoProtector;
    private readonly EmisorCertificadoValidator _certificadoValidator;

    public ESignMobileSignatureController(
        IDbContextFactory<AppDbContext> dbFactory,
        IWebHostEnvironment hostEnvironment,
        EmisorCertificadoProtector certificadoProtector,
        EmisorCertificadoValidator certificadoValidator)
    {
        _dbFactory = dbFactory;
        _hostEnvironment = hostEnvironment;
        _certificadoProtector = certificadoProtector;
        _certificadoValidator = certificadoValidator;
    }

    [HttpPost("{id:int}/firma/configurar")]
    [RequestSizeLimit(MaxCertificadoSize + 64 * 1024)]
    public async Task<IActionResult> ConfigurarFirma(int id, [FromForm] IFormFile? certificado, [FromForm] string? clave, CancellationToken cancellationToken)
    {
        if (certificado is null || certificado.Length <= 0 || certificado.Length > MaxCertificadoSize ||
            !string.Equals(Path.GetExtension(certificado.FileName), ".p12", StringComparison.OrdinalIgnoreCase))
            return BadRequest(new { mensaje = "Debes enviar un certificado .p12 válido de hasta 5 MB." });
        if (string.IsNullOrWhiteSpace(clave) || clave.Trim().Length > 50)
            return BadRequest(new { mensaje = "La clave del certificado es obligatoria y no puede exceder 50 caracteres." });

        var accountId = await GetAccountIdAsync(cancellationToken);
        if (accountId is null) return Unauthorized();

        await using var db = await _dbFactory.CreateDbContextAsync(cancellationToken);
        var emisor = await db.Emisores.FirstOrDefaultAsync(e => e.Codigo == id && e.Estado && e.IdUsuario == accountId.Value, cancellationToken);
        if (emisor is null) return NotFound(new { mensaje = "Emisor no encontrado." });

        var relativePath = $"certs/path/{Guid.NewGuid():N}.p12";
        var physicalPath = Path.Combine(_hostEnvironment.ContentRootPath, "App_Data", relativePath.Replace('/', Path.DirectorySeparatorChar));
        Directory.CreateDirectory(Path.GetDirectoryName(physicalPath)!);
        await using (var output = System.IO.File.Create(physicalPath))
            await certificado.CopyToAsync(output, cancellationToken);

        emisor.PathCertificado = relativePath;
        emisor.ClaveCertificado = _certificadoProtector.ProtegerClave(clave.Trim());
        var validacion = await _certificadoValidator.ValidarConApiAsync(emisor, cancellationToken);
        if (!validacion.IsValid)
        {
            System.IO.File.Delete(physicalPath);
            return BadRequest(new { mensaje = validacion.Message });
        }

        await db.SaveChangesAsync(cancellationToken);
        return Ok(new
        {
            mensaje = "Firma configurada correctamente.",
            esValida = true,
            validacion.FechaExpiracion,
            validacion.DiasRestantes,
            validacion.NombreTitular
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
