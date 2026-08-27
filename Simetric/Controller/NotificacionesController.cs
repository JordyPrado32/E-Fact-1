using System.Security.Claims;
using System.Text.Json;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Simetric.Data;
using Simetric.DTOs;
using Simetric.Models;
using Simetric.Services;

namespace Simetric.Controllers;

[Authorize]
[ApiController]
[Route("api/notificaciones")]
public sealed class NotificacionesController : ControllerBase
{
    private static readonly JsonSerializerOptions HistorialJsonOptions = new() { PropertyNameCaseInsensitive = true };
    private readonly DashboardService _dashboardService;
    private readonly IDbContextFactory<AppDbContext> _dbFactory;
    private readonly SolicitudService _solicitudService;

    public NotificacionesController(
        SolicitudService solicitudService,
        DashboardService dashboardService,
        IDbContextFactory<AppDbContext> dbFactory)
    {
        _solicitudService = solicitudService;
        _dashboardService = dashboardService;
        _dbFactory = dbFactory;
    }

    [HttpGet]
    [HttpGet("mobile")]
    public async Task<IActionResult> Get([FromQuery] int top = 20)
    {
        var usuarioId = ObtenerUsuarioId(User);
        if (usuarioId <= 0) return Unauthorized();

        var take = Math.Clamp(top, 1, 50);
        var usuario = await ObtenerUsuarioAsync(usuarioId);
        var items = new List<NotificacionMobileDto>();

        items.AddRange((await _solicitudService.ObtenerNotificacionesPendientesClienteAsync(usuarioId, 10)).Select(ToSolicitudNotification));
        items.AddRange((await _solicitudService.ObtenerEntregasFirmaPendientesClienteAsync(usuarioId, 10)).Select(ToFirmaNotification));
        items.AddRange((await _solicitudService.ObtenerRespuestasPendientesSoporteAsync(usuarioId, 10)).Select(ToSoporteNotification));
        items.AddRange((await ObtenerDocumentosAutorizadosAsync(usuarioId)).Select(ToDocumentoNotification));
        items.AddRange(ObtenerRecargasDocumentos(usuario?.HistorialComprasDocumentosJson).Select(ToRecargaNotification));

        if (BackOfficePermissionHelper.PuedeAprobarTransferencias(usuario?.IdTipoUsuario, usuario?.TipoCliente))
        {
            items.AddRange((await ObtenerSolicitudesDocumentosBackOfficeAsync()).Select(ToSolicitudDocumentosNotification));
        }

        return Ok(items
            .OrderByDescending(item => item.Fecha ?? DateTime.MinValue)
            .Take(take)
            .ToList());
    }

    private static int ObtenerUsuarioId(ClaimsPrincipal user)
    {
        var idClaim = user.FindFirst("IdUsuario")?.Value
            ?? user.FindFirst(ClaimTypes.NameIdentifier)?.Value;

        return int.TryParse(idClaim, out var usuarioId) ? usuarioId : 0;
    }

    private async Task<Usuario?> ObtenerUsuarioAsync(int usuarioId)
    {
        await using var context = await _dbFactory.CreateDbContextAsync();
        return await context.Usuarios
            .AsNoTracking()
            .FirstOrDefaultAsync(item => item.IdUsuario == usuarioId);
    }

    private async Task<List<DashboardAuthorizedDocumentDto>> ObtenerDocumentosAutorizadosAsync(int usuarioId)
    {
        try
        {
            return (await _dashboardService.GetRecentAuthorizedDocumentsAsync(usuarioId, 500))
                .Where(item => item.EsPendienteAutorizacion || item.FechaAutorizacion >= DateTime.Now.AddDays(-7))
                .Take(12)
                .ToList();
        }
        catch
        {
            return new List<DashboardAuthorizedDocumentDto>();
        }
    }

    private static List<CompraDocumentosHistorialItem> ObtenerRecargasDocumentos(string? historialJson)
    {
        if (string.IsNullOrWhiteSpace(historialJson)) return new List<CompraDocumentosHistorialItem>();

        try
        {
            return (JsonSerializer.Deserialize<List<CompraDocumentosHistorialItem>>(historialJson, HistorialJsonOptions)
                    ?? new List<CompraDocumentosHistorialItem>())
                .Where(item =>
                    item.Fecha >= DateTime.Now.AddDays(-7) &&
                    !string.IsNullOrWhiteSpace(item.Reference) &&
                    item.Reference.StartsWith("BO-", StringComparison.OrdinalIgnoreCase) &&
                    (EsRecargaAprobada(item) || EsRecargaRechazada(item)))
                .OrderByDescending(item => item.Fecha)
                .Take(4)
                .ToList();
        }
        catch
        {
            return new List<CompraDocumentosHistorialItem>();
        }
    }

    private async Task<List<ReporteVentaBackOffice>> ObtenerSolicitudesDocumentosBackOfficeAsync()
    {
        await using var context = await _dbFactory.CreateDbContextAsync();
        return await context.ReporteVentasBackOffice
            .AsNoTracking()
            .Where(item =>
                item.Estado == "pendiente" &&
                item.Producto == "e-fact" &&
                item.Canal != null &&
                item.Canal.Contains("Transferencia Web") &&
                item.Observacion != null &&
                item.Observacion.Contains("[CompraDocs:"))
            .OrderByDescending(item => item.Fecha)
            .Take(8)
            .ToListAsync();
    }

    private static NotificacionMobileDto ToSolicitudNotification(SolicitudNotificacionDto item) =>
        new()
        {
            Id = $"solicitud:{item.ObsId}",
            Titulo = string.Equals(item.ObsTipo, "CODIGO", StringComparison.OrdinalIgnoreCase)
                ? "Codigo solicitado"
                : $"Corregir {GetCampoLabel(item.ObsCampoObservado)}",
            Mensaje = item.ObsDetalle,
            Fecha = item.ObsFechaObservacion,
            Leido = false,
            Tipo = item.ObsTipo,
            Ruta = "/solicitud/pagos"
        };

    private static NotificacionMobileDto ToFirmaNotification(SolicitudNotificacionDto item) =>
        new()
        {
            Id = $"firma:{item.ObsId}",
            Titulo = "Firma lista",
            Mensaje = item.ObsDetalle,
            Fecha = item.ObsFechaObservacion,
            Leido = false,
            Tipo = "FIRMA",
            Ruta = $"/solicitud/pagos?firma={item.SolId}"
        };

    private static NotificacionMobileDto ToSoporteNotification(SolicitudNotificacionDto item) =>
        new()
        {
            Id = $"soporte:{item.ObsId}",
            Titulo = string.Equals(item.ObsTipo, "CODIGO", StringComparison.OrdinalIgnoreCase)
                ? "Codigo recibido"
                : $"Correccion recibida: {GetCampoLabel(item.ObsCampoObservado)}",
            Mensaje = string.IsNullOrWhiteSpace(item.ObsRespuestaUsuario) ? item.ObsDetalle : item.ObsRespuestaUsuario,
            Fecha = item.ObsFechaObservacion,
            Leido = false,
            Tipo = "SOPORTE",
            Ruta = $"/soporte/solicitud/{item.SolId}"
        };

    private static NotificacionMobileDto ToDocumentoNotification(DashboardAuthorizedDocumentDto item) =>
        new()
        {
            Id = $"documento:{item.Clave}",
            Titulo = item.Titulo,
            Mensaje = item.EsPendienteAutorizacion
                ? "Corrige lo necesario y reenvia el comprobante al SRI."
                : item.NumeroDocumento,
            Fecha = item.FechaAutorizacion,
            Leido = false,
            Tipo = item.EsPendienteAutorizacion ? "PENDIENTE_SRI" : "AUTORIZACION_SRI",
            Ruta = item.Ruta
        };

    private static NotificacionMobileDto ToRecargaNotification(CompraDocumentosHistorialItem item) =>
        new()
        {
            Id = $"recarga-documentos:{item.Id}:{item.Estado}",
            Titulo = EsRecargaAprobada(item) ? "Recarga aprobada" : "Recarga rechazada",
            Mensaje = item.Descripcion,
            Fecha = item.Fecha,
            Leido = false,
            Tipo = "RECARGA_DOCUMENTOS",
            Ruta = "/compra-documentos"
        };

    private static NotificacionMobileDto ToSolicitudDocumentosNotification(ReporteVentaBackOffice item) =>
        new()
        {
            Id = $"bo-recarga-documentos:{item.IdReporte}:{item.Estado}",
            Titulo = "Solicitud de documentos",
            Mensaje = GetSolicitudDocumentosDetalle(item),
            Fecha = item.Fecha,
            Leido = false,
            Tipo = "BACKOFFICE_DOCUMENTOS",
            Ruta = "/backoffice/cobros"
        };

    private static string GetCampoLabel(string? campo) =>
        string.IsNullOrWhiteSpace(campo)
            ? "datos"
            : campo.Replace("SOL_", string.Empty, StringComparison.OrdinalIgnoreCase).Replace("_", " ").ToLowerInvariant();

    private static bool EsRecargaAprobada(CompraDocumentosHistorialItem recarga) =>
        recarga.Estado?.Contains("aprob", StringComparison.OrdinalIgnoreCase) == true;

    private static bool EsRecargaRechazada(CompraDocumentosHistorialItem recarga) =>
        recarga.Estado?.Contains("rech", StringComparison.OrdinalIgnoreCase) == true;

    private static string GetSolicitudDocumentosDetalle(ReporteVentaBackOffice solicitud)
    {
        var cliente = string.IsNullOrWhiteSpace(solicitud.Cliente)
            ? "Usuario"
            : solicitud.Cliente.Split('|')[0].Trim();

        return $"{cliente} solicita validar una transferencia por $ {solicitud.Valor:N2}.";
    }

    public sealed class NotificacionMobileDto
    {
        public string Id { get; init; } = string.Empty;
        public string Titulo { get; init; } = string.Empty;
        public string Mensaje { get; init; } = string.Empty;
        public DateTime? Fecha { get; init; }
        public bool Leido { get; init; }
        public string? Tipo { get; init; }
        public string? Ruta { get; init; }
    }
}
