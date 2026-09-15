using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Simetric.Controllers;

/// <summary>
/// Historial de PDFs estampados desde la aplicación móvil.
/// Se mantiene separado del controlador de solicitudes y certificados.
/// </summary>
[Authorize]
[ApiController]
[Route("api/mobile/e-rubrica/documentos")]
public sealed class ESignMobileSignedDocumentsController : ControllerBase
{
    private readonly IWebHostEnvironment _hostEnvironment;

    public ESignMobileSignedDocumentsController(IWebHostEnvironment hostEnvironment)
    {
        _hostEnvironment = hostEnvironment;
    }

    [HttpGet("firmados")]
    public IActionResult ObtenerFirmados()
    {
        var userId = GetUserId();
        if (userId <= 0) return Unauthorized();

        var relativeDirectory = Path.Combine("uploads", "e-rubrica", "estampados", userId.ToString());
        var physicalDirectory = Path.Combine(_hostEnvironment.WebRootPath, relativeDirectory);
        if (!Directory.Exists(physicalDirectory)) return Ok(Array.Empty<object>());

        var documentos = Directory.EnumerateFiles(physicalDirectory, "*.pdf", SearchOption.TopDirectoryOnly)
            .Select(path => new FileInfo(path))
            .OrderByDescending(file => file.LastWriteTimeUtc)
            .Select(file => new
            {
                nombreDocumento = file.Name,
                fechaFirma = file.LastWriteTimeUtc,
                estado = "Válido",
                tamano = $"{Math.Max(1, Math.Ceiling(file.Length / 1024d))} KB",
                downloadUrl = "/" + Path.Combine(relativeDirectory, file.Name).Replace('\\', '/'),
                previewUrl = "/" + Path.Combine(relativeDirectory, file.Name).Replace('\\', '/')
            });

        return Ok(documentos);
    }

    private int GetUserId()
    {
        var value = User.FindFirstValue(ClaimTypes.NameIdentifier)
            ?? User.FindFirstValue("idUsuario")
            ?? User.FindFirstValue("sub");
        return int.TryParse(value, out var userId) ? userId : 0;
    }
}
