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
        var webRootPath = string.IsNullOrWhiteSpace(_hostEnvironment.WebRootPath)
            ? Path.Combine(_hostEnvironment.ContentRootPath, "wwwroot")
            : _hostEnvironment.WebRootPath;
        var physicalDirectory = Path.Combine(webRootPath, relativeDirectory);
        if (!Directory.Exists(physicalDirectory)) return Ok(Array.Empty<object>());

        var documentos = Directory.EnumerateFiles(physicalDirectory, "*.pdf", SearchOption.TopDirectoryOnly)
            .Select(path => new FileInfo(path))
            .OrderByDescending(file => file.CreationTime)
            .Select(file => new
            {
                nombreDocumento = CrearNombreVisible(file),
                nombreArchivo = file.Name,
                fechaFirma = file.CreationTime,
                estado = "Válido",
                tamano = $"{Math.Max(1, Math.Ceiling(file.Length / 1024d))} KB",
                downloadUrl = "/" + Path.Combine(relativeDirectory, file.Name).Replace('\\', '/'),
                previewUrl = "/" + Path.Combine(relativeDirectory, file.Name).Replace('\\', '/')
            });

        return Ok(documentos);
    }

    private int GetUserId()
    {
        var value = User.FindFirstValue("IdUsuario")
            ?? User.FindFirstValue(ClaimTypes.NameIdentifier)
            ?? User.FindFirstValue("idUsuario")
            ?? User.FindFirstValue("sub");
        return int.TryParse(value, out var userId) ? userId : 0;
    }

    private static string CrearNombreVisible(FileInfo file)
    {
        if (TryGetNombreDocumentoOriginal(file.Name, out var nombreOriginal))
            return $"{nombreOriginal}_firmado.pdf";

        var nombre = Path.GetFileNameWithoutExtension(file.Name);
        var partes = nombre.Split('_', StringSplitOptions.RemoveEmptyEntries);
        if (partes.Length >= 3 &&
            partes[0].Length == 14 &&
            partes[^1].Equals("estampado", StringComparison.OrdinalIgnoreCase) &&
            partes[1].Length >= 16)
        {
            return $"Documento estampado - {file.CreationTime:dd/MM/yyyy HH:mm}";
        }

        var index = nombre.IndexOf("_estampado", StringComparison.OrdinalIgnoreCase);
        if (index > 0)
            nombre = nombre[..index];

        return string.IsNullOrWhiteSpace(nombre) ? "Documento firmado.pdf" : $"{nombre.Replace('_', ' ').Trim()}.pdf";
    }

    private static bool TryGetNombreDocumentoOriginal(string fileName, out string nombreOriginal)
    {
        nombreOriginal = string.Empty;
        var nombre = Path.GetFileNameWithoutExtension(fileName);
        var partes = nombre.Split('_', StringSplitOptions.RemoveEmptyEntries);
        if (partes.Length < 4 ||
            partes[0].Length != 14 ||
            partes[1].Length != 32 ||
            !partes[^1].Equals("firmado", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        nombreOriginal = string.Join("_", partes.Skip(2).Take(partes.Length - 3))
            .Replace('_', ' ')
            .Trim();
        return !string.IsNullOrWhiteSpace(nombreOriginal);
    }
}
