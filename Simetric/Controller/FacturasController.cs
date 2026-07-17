using Microsoft.AspNetCore.Mvc;
using Simetric.DTOs;
using Simetric.Models;
using Simetric.Services;
using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using System.Linq;

namespace Simetric.Controllers
{

    [ApiController]
    [Route("api/[controller]")]

    public class FacturasController : ControllerBase
    {
        private readonly FacturacionService _service;

        public FacturasController(FacturacionService service)
        {
            _service = service;
        }

        /// <summary>
        /// DTO de entrada para la creación de factura. 
        /// Se asegura de usar el modelo 'Detallefactura' para compatibilidad con el Service.
        /// </summary>
        public class FacturaCreateDto
        {
            public Factura Factura { get; set; } = null!;
            public Cliente Cliente { get; set; } = null!;
            public List<Detallefactura> Detalles { get; set; } = new();
        }

        /// <summary>
        /// Guarda una factura completa incluyendo cliente y detalles de forma atómica.
        /// </summary>
        [HttpPost("guardar-completa")]
        public async Task<IActionResult> GuardarCompleta([FromBody] FacturaCreateDto dto)
        {
            // 1. Validaciones de integridad del objeto
            if (dto == null)
                return BadRequest(new { mensaje = "El cuerpo de la solicitud no puede estar vacío." });

            if (!ModelState.IsValid)
                return BadRequest(ModelState);

            if (dto.Factura == null || dto.Cliente == null)
                return BadRequest(new { mensaje = "Datos de factura o cliente incompletos." });

            if (dto.Detalles == null || !dto.Detalles.Any())
                return BadRequest(new { mensaje = "La factura debe tener al menos un detalle de producto/servicio." });

            try
            {
                // 2. Recalcular valores de servidor por seguridad antes de enviar al Service
                // Esto evita que datos manipulados en el cliente lleguen a la base de datos
                foreach (var detalle in dto.Detalles)
                {
                    // Aseguramos que el total de cada línea sea correcto: (Cant * Precio) - Descuento
                    decimal subtotalLinea = (detalle.Cantproducto * (detalle.Precioproducto)) - (detalle.Descuento ?? 0);
                    var tarifa = TaxRateHelper.NormalizePercentInt(detalle.Tarifa);
                    detalle.Tarifa = tarifa;
                    detalle.Valortproducto = Math.Round(subtotalLinea, 2, MidpointRounding.AwayFromZero);
                    detalle.Valoriva = Math.Round(detalle.Valortproducto * (tarifa / 100m), 2, MidpointRounding.AwayFromZero);
                    detalle.Valortotal = Math.Round(detalle.Valortproducto + detalle.Valoriva, 2, MidpointRounding.AwayFromZero);
                }

                // 3. Ejecución del servicio transaccional
                // Pasamos los modelos directamente al service que ya conoce estas entidades
                int idUsuario = int.Parse(User.FindFirst("IdUsuario")?.Value ?? "0");
                // o si usas NameIdentifier:
                // int idUsuario = int.Parse(User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value ?? "0");

                var ok = await _service.GuardarFacturaCompletaAsync(idUsuario, dto.Factura, dto.Cliente, dto.Detalles);


                if (!ok)
                {
                    return StatusCode(500, new { mensaje = "El servicio no pudo completar la operación. Verifique los logs." });
                }

                // 4. Respuesta exitosa
                return Ok(new
                {
                    mensaje = "Factura procesada y guardada correctamente.",
                    codfactura = dto.Factura.Codfactura,
                    numeroComprobante = dto.Factura.Numfactura,
                    clienteId = dto.Cliente.Codcliente
                });
            }
            catch (Exception ex)
            {
                // Es vital capturar la inner exception para errores de base de datos
                var errorInterno = ex.InnerException != null ? ex.InnerException.Message : ex.Message;
                return StatusCode(500, new
                {
                    mensaje = "Error inesperado al guardar la factura.",
                    detalle = errorInterno
                });
            }
        }

        [HttpGet("por-numero")]
        public async Task<IActionResult> GetPorNumero([FromQuery] string numFactura, [FromQuery] int idUsuario, [FromQuery] string? serie = null)
        {
            if (idUsuario <= 0) return Unauthorized();

            var dto = await _service.GetFacturaPorNumeroUsuarioAsync(numFactura, idUsuario, serie);
            if (dto == null) return NotFound(new { mensaje = "No se encontró la factura con ese número." });

            return Ok(dto);
        }

        [HttpGet("nc/next-secuencial")]
        public async Task<IActionResult> GetNextNc([FromQuery] int idUsuario, [FromQuery] string? serie = null)
        {
            if (idUsuario <= 0) return Unauthorized();

            var serieNc = string.IsNullOrWhiteSpace(serie)
                ? await _service.GetSerieNotaCreditoRawAsync(idUsuario)
                : serie.Replace("-", "").Trim();
            if (string.IsNullOrWhiteSpace(serieNc))
                return BadRequest(new { error = "Caja sin SerieNotasCred." });

            var next = await _service.GetNextSecuencialNotaCreditoAsync(idUsuario, serieNc);

            return Ok(new { serieNc, proximo = next });
        }

        [HttpGet("nd/next-secuencial")]
        public async Task<IActionResult> GetNextNd([FromQuery] int idUsuario, [FromQuery] string? serie = null)
        {
            if (idUsuario <= 0) return Unauthorized();

            var serieNd = string.IsNullOrWhiteSpace(serie)
                ? await _service.GetSerieNotaDebitoRawAsync(idUsuario)
                : serie.Replace("-", "").Trim();
            if (string.IsNullOrWhiteSpace(serieNd))
                return BadRequest(new { error = "Caja sin SerieDebitos." });

            var next = await _service.GetNextSecuencialNotaDebitoAsync(idUsuario, serieNd);

            return Ok(new { serieNd, proximo = next });
        }
        
        /// <summary>
         /// Obtiene el siguiente número secuencial disponible.
         /// </summary>
        [HttpGet("siguiente-secuencial")]
        public async Task<IActionResult> GetSiguienteSecuencial([FromQuery] int idUsuario)
        {
            var siguiente = await _service.GetNextFacturaNumeroAsync(idUsuario);
            return Ok(new { proximo = siguiente });
        }
        [HttpGet]
        public async Task<IActionResult> Listar([FromQuery] int top = 100)
        {
            var lista = await _service.ListarFacturasAsync(top);
            return Ok(lista);
        }

        [HttpGet("api/impuestos/iva")]
        public async Task<IActionResult> GetIva()
            => Ok(await _service.GetPorcentajesIvaCatalogoAsync());


        [HttpGet("{codfactura:int}")]
        public async Task<IActionResult> Ver(int codfactura)
        {
            var dto = await _service.GetFacturaCompletaAsync(codfactura);
            if (dto == null) return NotFound();
            return Ok(dto);
        }
    }
}



