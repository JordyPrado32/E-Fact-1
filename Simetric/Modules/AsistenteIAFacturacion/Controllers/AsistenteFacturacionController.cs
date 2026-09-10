using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Simetric.Modules.AsistenteIAFacturacion.DTOs;
using Simetric.Modules.AsistenteIAFacturacion.Services;
using System.Security.Claims;

namespace Simetric.Modules.AsistenteIAFacturacion.Controllers;

[Authorize]
[ApiController]
[Route("api/asistente-facturacion")]
[EnableRateLimiting("asistente-facturacion")]
public sealed class AsistenteFacturacionController : ControllerBase
{
    private readonly IAsistenteFacturacionService _asistenteFacturacionService;
    private readonly IOpenAIAsistenteService _openAIAsistenteService;

    public AsistenteFacturacionController(
        IAsistenteFacturacionService asistenteFacturacionService,
        IOpenAIAsistenteService openAIAsistenteService)
    {
        _asistenteFacturacionService = asistenteFacturacionService;
        _openAIAsistenteService = openAIAsistenteService;
    }

    [HttpPost("chat")]
    public async Task<ActionResult<ChatFacturaResponse>> Chat([FromBody] ChatFacturaRequest request, CancellationToken cancellationToken)
    {
        if (request is null || string.IsNullOrWhiteSpace(request.Mensaje))
            return BadRequest("El mensaje es obligatorio.");

        if (request.Mensaje.Length > 800)
            return BadRequest("El mensaje no puede superar los 800 caracteres.");

        if (!string.IsNullOrWhiteSpace(request.SessionId) && request.SessionId.Length > 120)
            return BadRequest("La sesión indicada no es válida.");

        if (!string.IsNullOrWhiteSpace(request.RequestId) && request.RequestId.Length > 120)
            return BadRequest("El identificador de solicitud no es válido.");

        var userIdClaim = User.FindFirst("IdUsuario")?.Value
            ?? User.FindFirst(ClaimTypes.NameIdentifier)?.Value;

        if (!int.TryParse(userIdClaim, out var userId) || userId <= 0)
            return Unauthorized("No se pudo resolver el usuario actual.");

        var response = await _asistenteFacturacionService.ProcesarAsync(userId, request, cancellationToken);
        return Ok(response);
    }

    [HttpGet("diagnostico-openai")]
    public ActionResult<object> DiagnosticoOpenAi()
    {
        if (User.FindFirst("IdTipoUsuario")?.Value != "2")
            return Forbid();

        var diagnostics = _openAIAsistenteService.GetDiagnostics();
        return Ok(diagnostics);
    }
}
