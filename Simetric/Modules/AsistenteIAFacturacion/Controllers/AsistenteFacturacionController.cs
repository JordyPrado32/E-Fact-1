using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Simetric.Modules.AsistenteIAFacturacion.DTOs;
using Simetric.Modules.AsistenteIAFacturacion.Services;
using System.Security.Claims;

namespace Simetric.Modules.AsistenteIAFacturacion.Controllers;

[Authorize]
[ApiController]
[Route("api/asistente-facturacion")]
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
        var diagnostics = _openAIAsistenteService.GetDiagnostics();
        return Ok(diagnostics);
    }
}
