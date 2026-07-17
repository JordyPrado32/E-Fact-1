using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Simetric.Services;

namespace Simetric.Controllers
{
    [Authorize]
    [ApiController]
    [Route("api/solicitudes")]
    public class SolicitudesController : ControllerBase
    {
        private readonly SolicitudService _solicitudService;

        public SolicitudesController(SolicitudService solicitudService)
        {
            _solicitudService = solicitudService;
        }

        [HttpGet("mis-solicitudes")]
        public async Task<IActionResult> ObtenerMisSolicitudes()
        {
            var usuarioId = ObtenerUsuarioId(User);
            if (usuarioId <= 0)
            {
                return Unauthorized();
            }

            var solicitudes = await _solicitudService.ObtenerSolicitudesClienteAsync(usuarioId);
            return Ok(solicitudes);
        }

        [HttpGet("mis-firmas")]
        public async Task<IActionResult> ObtenerMisFirmas()
        {
            var usuarioId = ObtenerUsuarioId(User);
            if (usuarioId <= 0)
            {
                return Unauthorized();
            }

            var firmas = await _solicitudService.ObtenerFirmasClienteAsync(usuarioId);
            return Ok(firmas);
        }

        [HttpGet("mis-entregas-firma")]
        public async Task<IActionResult> ObtenerMisEntregasFirmaPendientes([FromQuery] int take = 8)
        {
            var usuarioId = ObtenerUsuarioId(User);
            if (usuarioId <= 0)
            {
                return Unauthorized();
            }

            var entregas = await _solicitudService.ObtenerEntregasFirmaPendientesClienteAsync(usuarioId, take);
            return Ok(entregas);
        }

        [HttpPost("mis-entregas-firma/marcar-vista")]
        public async Task<IActionResult> MarcarEntregaFirmaVista([FromBody] MarcarEntregaFirmaVistaRequest request)
        {
            var usuarioId = ObtenerUsuarioId(User);
            if (usuarioId <= 0)
            {
                return Unauthorized();
            }

            if (request.ObservacionId <= 0)
            {
                return BadRequest(new { mensaje = "Debe indicar una observacion valida." });
            }

            var actualizado = await _solicitudService.MarcarEntregaFirmaVistaAsync(request.ObservacionId, usuarioId);
            if (!actualizado)
            {
                return NotFound(new { mensaje = "No se encontro la entrega de firma pendiente para marcarla como revisada." });
            }

            return Ok(new { mensaje = "Entrega de firma marcada como revisada." });
        }

        [HttpGet("{solId:int}/firma-p12")]
        public async Task<IActionResult> DescargarFirmaP12(int solId)
        {
            var usuarioId = ObtenerUsuarioId(User);
            if (usuarioId <= 0)
            {
                return Unauthorized();
            }

            var archivo = await _solicitudService.ObtenerArchivoFirmaP12ClienteAsync(solId, usuarioId);
            if (archivo is null)
            {
                return NotFound(new { mensaje = "No se encontro una firma .p12 disponible para esta solicitud." });
            }

            return File(archivo.Contenido, "application/x-pkcs12", archivo.NombreArchivo);
        }

        private static int ObtenerUsuarioId(ClaimsPrincipal user)
        {
            var idClaim = user.FindFirst("IdUsuario")?.Value
                ?? user.FindFirst(ClaimTypes.NameIdentifier)?.Value;

            return int.TryParse(idClaim, out var usuarioId) ? usuarioId : 0;
        }

        public sealed class MarcarEntregaFirmaVistaRequest
        {
            public int ObservacionId { get; set; }
        }
    }
}
