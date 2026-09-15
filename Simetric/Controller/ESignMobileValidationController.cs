using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Simetric.Services.ESign;

namespace Simetric.Controllers;

/// <summary>Validación de firmas PDF y códigos QR para la aplicación móvil.</summary>
[Authorize]
[ApiController]
[Route("api/mobile/e-rubrica/documentos")]
public sealed class ESignMobileValidationController : ControllerBase
{
    private readonly FirmaStampApiService _firmaStampApiService;

    public ESignMobileValidationController(FirmaStampApiService firmaStampApiService)
    {
        _firmaStampApiService = firmaStampApiService;
    }

    [HttpPost("validar-firma")]
    [RequestSizeLimit(10 * 1024 * 1024)]
    public async Task<IActionResult> ValidarFirmaPdf([FromForm] IFormFile? pdf, CancellationToken cancellationToken = default)
    {
        if (GetUserId() <= 0) return Unauthorized();
        if (pdf is null || pdf.Length <= 0 || pdf.Length > 10 * 1024 * 1024 ||
            !string.Equals(Path.GetExtension(pdf.FileName), ".pdf", StringComparison.OrdinalIgnoreCase))
        {
            return BadRequest(new { mensaje = "Debes enviar un archivo PDF válido de hasta 10 MB." });
        }

        await using var stream = pdf.OpenReadStream();
        var resultado = await _firmaStampApiService.ValidarFirmaPdfAsync(
            stream, Path.GetFileName(pdf.FileName), pdf.ContentType, cancellationToken);
        return resultado.Success ? Ok(resultado) : BadRequest(resultado);
    }

    [HttpGet("validar-qr")]
    public async Task<IActionResult> ValidarQr([FromQuery] string entrada, CancellationToken cancellationToken)
    {
        if (GetUserId() <= 0) return Unauthorized();
        if (string.IsNullOrWhiteSpace(entrada)) return BadRequest(new { mensaje = "La entrada QR es obligatoria." });

        var resultado = await _firmaStampApiService.ValidarQrAsync(entrada, cancellationToken);
        return resultado.Success ? Ok(resultado) : BadRequest(resultado);
    }

    private int GetUserId()
    {
        var value = User.FindFirstValue("IdUsuario")
            ?? User.FindFirstValue(ClaimTypes.NameIdentifier)
            ?? User.FindFirstValue("idUsuario")
            ?? User.FindFirstValue("sub");
        return int.TryParse(value, out var userId) ? userId : 0;
    }
}
