using System.Security.Claims;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Simetric.Data;
using Simetric.Services;

namespace Simetric.Controllers;

/// <summary>Plan disponible de E-Rúbrica para el usuario autenticado.</summary>
[Authorize]
[ApiController]
[Route("api/mobile/e-rubrica")]
public sealed class ESignMobilePlanController : ControllerBase
{
    private readonly IDbContextFactory<AppDbContext> _dbFactory;
    private readonly SolicitudService _solicitudService;

    public ESignMobilePlanController(IDbContextFactory<AppDbContext> dbFactory, SolicitudService solicitudService)
    {
        _dbFactory = dbFactory;
        _solicitudService = solicitudService;
    }

    [HttpGet("plan")]
    public async Task<IActionResult> ObtenerPlan(CancellationToken cancellationToken)
    {
        var accountId = await GetAccountIdAsync(cancellationToken);
        if (accountId is null) return Unauthorized();

        var firma = (await _solicitudService.ObtenerSolicitudesClienteAsync(accountId.Value))
            .Where(item => item.SolPagoExitoso)
            .OrderByDescending(item => item.SolFechaPago ?? item.SolFechaSolicitud)
            .FirstOrDefault();
        if (firma is null)
            return Ok(new
            {
                tieneFirmaPagada = false,
                diasRestantes = 0,
                estado = "Compra una firma",
                fechaVencimiento = (DateTime?)null
            });

        var fechaInicio = firma.SolFechaAprobacion ?? firma.SolFechaPago ?? firma.SolFechaSolicitud;
        var match = Regex.Match(firma.SolVigencia ?? string.Empty, @"\d+");
        var cantidad = match.Success && int.TryParse(match.Value, out var value) ? value : 1;
        var fechaVencimiento = Regex.IsMatch(firma.SolVigencia ?? string.Empty, @"D[IÍ]AS?", RegexOptions.IgnoreCase)
            ? fechaInicio.AddDays(Math.Max(cantidad, 1))
            : fechaInicio.AddYears(Math.Clamp(cantidad, 1, 5));
        var diasRestantes = Math.Max(0, (fechaVencimiento.Date - DateTime.Today).Days);
        return Ok(new
        {
            tieneFirmaPagada = true,
            solicitudId = firma.SolId,
            vigencia = firma.SolVigencia,
            fechaInicio,
            fechaVencimiento,
            diasRestantes,
            estado = diasRestantes > 0 ? "Activo" : "Vencido"
        });
    }

    private async Task<int?> GetAccountIdAsync(CancellationToken cancellationToken)
    {
        var claim = User.FindFirstValue("IdUsuario") ?? User.FindFirstValue(ClaimTypes.NameIdentifier);
        if (!int.TryParse(claim, out var userId) || userId <= 0) return null;
        await using var db = await _dbFactory.CreateDbContextAsync(cancellationToken);
        var user = await db.Usuarios.AsNoTracking().Where(u => u.IdUsuario == userId)
            .Select(u => new { u.IdUsuario, u.idJefe, u.estadoAsociado }).FirstOrDefaultAsync(cancellationToken);
        return user is null ? null : user.estadoAsociado == true && user.idJefe > 0 ? user.idJefe : user.IdUsuario;
    }
}
