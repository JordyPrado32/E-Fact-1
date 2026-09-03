using Microsoft.AspNetCore.Mvc;
using Simetric.DTOs;
using Simetric.Models;
using Simetric.Services;
using System;
using System.Collections.Generic;
using System.Security.Claims;
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
            public int? IdUsuario { get; set; }
            public Factura Factura { get; set; } = null!;
            public Cliente Cliente { get; set; } = null!;
            public List<Detallefactura> Detalles { get; set; } = new();
            public List<FacturaCorreoDestinoDto> CorreosFactura { get; set; } = new();
        }

        public class ConfigurarSecuenciaFacturaDto
        {
            public int? IdUsuario { get; set; }
            public bool YaFacturoAntes { get; set; }
            public string? SecuenciaAnterior { get; set; }
            public int? CodEmisor { get; set; }
            public string? Serie { get; set; }
        }

        public class SeriePreferidaDto
        {
            public int? IdUsuario { get; set; }
            public string? Serie { get; set; }
        }

        public class EnviarFacturaCorreoDto
        {
            public int? IdUsuario { get; set; }
            public bool ForzarReenvio { get; set; }
            public List<string> CorreosCopia { get; set; } = new();
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
                    decimal subtotalLinea = Math.Max(
                        0m,
                        (detalle.Cantproducto * detalle.Precioproducto) - Math.Max(0m, detalle.Descuento ?? 0m));
                    var tarifa = TaxRateHelper.NormalizePercentInt(detalle.Tarifa);
                    detalle.Tarifa = tarifa;
                    detalle.Valortproducto = Math.Round(subtotalLinea, 2, MidpointRounding.AwayFromZero);
                    detalle.Valoriva = Math.Round(detalle.Valortproducto * (tarifa / 100m), 2, MidpointRounding.AwayFromZero);
                    detalle.Valortotal = Math.Round(detalle.Valortproducto + detalle.Valoriva, 2, MidpointRounding.AwayFromZero);
                }

                // 3. Ejecución del servicio transaccional
                // Pasamos los modelos directamente al service que ya conoce estas entidades
                int idUsuario = ResolverIdUsuario(dto.IdUsuario);
                if (idUsuario <= 0)
                    return Unauthorized(new { mensaje = "No se pudo identificar al usuario." });

                var ok = await _service.GuardarFacturaCompletaAsync(
                    idUsuario,
                    dto.Factura,
                    dto.Cliente,
                    dto.Detalles,
                    dto.CorreosFactura);


                if (!ok)
                {
                    return BadRequest(new
                    {
                        mensaje = _service.UltimoErrorGuardarFactura ?? "El servicio no pudo completar la operación."
                    });
                }

                // 4. Respuesta exitosa
                // La respuesta del guardado incluye el resultado real del envío
                // firmado al SRI; la app no debe asumir que guardar = autorizar.
                var respuestaSri = await _service.ReintentarEnvioSriFacturaAsync(dto.Factura.Codfactura);

                return Ok(new
                {
                    mensaje = "Factura procesada y guardada correctamente.",
                    codfactura = dto.Factura.Codfactura,
                    numeroComprobante = dto.Factura.Numfactura,
                    clienteId = dto.Cliente.Codcliente,
                    sri = respuestaSri
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
            idUsuario = ResolverIdUsuario(idUsuario);
            if (idUsuario <= 0) return Unauthorized();

            var dto = await _service.GetFacturaPorNumeroUsuarioAsync(numFactura, idUsuario, serie);
            if (dto == null) return NotFound(new { mensaje = "No se encontró la factura con ese número." });

            return Ok(dto);
        }

        [HttpGet("nc/next-secuencial")]
        public async Task<IActionResult> GetNextNc([FromQuery] int idUsuario, [FromQuery] string? serie = null)
        {
            idUsuario = ResolverIdUsuario(idUsuario);
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
            idUsuario = ResolverIdUsuario(idUsuario);
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
        public async Task<IActionResult> GetSiguienteSecuencial(
            [FromQuery] int idUsuario,
            [FromQuery] int? codEmisor = null,
            [FromQuery] string? serie = null)
        {
            idUsuario = ResolverIdUsuario(idUsuario);
            if (idUsuario <= 0) return Unauthorized();

            var siguiente = await _service.GetNextFacturaNumeroAsync(idUsuario, codEmisor, serie);
            return Ok(new { proximo = siguiente });
        }

        [HttpGet("preparacion")]
        public async Task<IActionResult> GetPreparacion([FromQuery] int idUsuario)
        {
            idUsuario = ResolverIdUsuario(idUsuario);
            if (idUsuario <= 0) return Unauthorized();

            var emisoresTask = _service.GetEmisoresActivosAsync(idUsuario);
            var ivaTask = _service.GetPorcentajesIvaCatalogoAsync();
            var tiposClienteTask = _service.GetTiposClienteAsync();
            var paisesTask = _service.GetPaisesAsync();
            var cajaTask = _service.GetCajaUsuarioAsync(idUsuario);
            var seriesTask = _service.GetSeriesFacturaHabilitadasAsync(idUsuario);
            var formasPagoTask = _service.ObtenerFormasPagoAsync();

            await Task.WhenAll(emisoresTask, ivaTask, tiposClienteTask, paisesTask, cajaTask, seriesTask, formasPagoTask);

            return Ok(new
            {
                emisores = await emisoresTask,
                porcentajesIva = await ivaTask,
                tiposCliente = await tiposClienteTask,
                paises = await paisesTask,
                caja = await cajaTask,
                series = await seriesTask,
                formasPago = await formasPagoTask
            });
        }

        [HttpGet("emisores")]
        public async Task<IActionResult> GetEmisores([FromQuery] int idUsuario)
        {
            idUsuario = ResolverIdUsuario(idUsuario);
            return idUsuario <= 0 ? Unauthorized() : Ok(await _service.GetEmisoresActivosAsync(idUsuario));
        }

        [HttpGet("tipos-cliente")]
        public async Task<IActionResult> GetTiposCliente()
            => Ok(await _service.GetTiposClienteAsync());

        [HttpGet("paises")]
        public async Task<IActionResult> GetPaises()
            => Ok(await _service.GetPaisesAsync());

        [HttpGet("provincias")]
        public async Task<IActionResult> GetProvincias([FromQuery] int idPais)
            => idPais <= 0 ? BadRequest() : Ok(await _service.GetProvinciasByPaisAsync(idPais));

        [HttpGet("ciudades")]
        public async Task<IActionResult> GetCiudades([FromQuery] int idProvincia)
            => idProvincia <= 0 ? BadRequest() : Ok(await _service.GetCiudadesByProvinciaAsync(idProvincia));

        [HttpGet("formas-pago")]
        public async Task<IActionResult> GetFormasPago()
            => Ok(await _service.ObtenerFormasPagoAsync());

        [HttpGet("clientes/buscar")]
        public async Task<IActionResult> BuscarClientes([FromQuery] int idUsuario, [FromQuery] string? filtro = null)
        {
            idUsuario = ResolverIdUsuario(idUsuario);
            return idUsuario <= 0
                ? Unauthorized()
                : Ok(await _service.BuscarClientesFiltroAsync(idUsuario, filtro ?? string.Empty));
        }

        [HttpGet("clientes/por-identificacion")]
        public async Task<IActionResult> GetClientePorIdentificacion(
            [FromQuery] int idUsuario,
            [FromQuery] string identificacion)
        {
            idUsuario = ResolverIdUsuario(idUsuario);
            if (idUsuario <= 0) return Unauthorized();

            var cliente = await _service.GetClienteByIdentificacionAsync(idUsuario, identificacion);
            return cliente is null ? NotFound() : Ok(cliente);
        }

        [HttpGet("clientes/{codCliente:int}/correos")]
        public async Task<IActionResult> GetCorreosCliente(int codCliente, [FromQuery] int idUsuario)
        {
            idUsuario = ResolverIdUsuario(idUsuario);
            return idUsuario <= 0
                ? Unauthorized()
                : Ok(await _service.GetCorreosAdicionalesClienteAsync(idUsuario, codCliente));
        }

        [HttpGet("clientes/facturas")]
        public async Task<IActionResult> GetFacturasCliente(
            [FromQuery] int idUsuario,
            [FromQuery] string identificacion,
            [FromQuery] int top = 200)
        {
            idUsuario = ResolverIdUsuario(idUsuario);
            return idUsuario <= 0
                ? Unauthorized()
                : Ok(await _service.ListarFacturasClienteUsuarioAsync(idUsuario, identificacion, top));
        }

        [HttpGet("productos/buscar")]
        public async Task<IActionResult> BuscarProductos([FromQuery] int idUsuario, [FromQuery] string? filtro = null)
        {
            idUsuario = ResolverIdUsuario(idUsuario);
            return idUsuario <= 0
                ? Unauthorized()
                : Ok(await _service.BuscarProductosFiltroAsync(idUsuario, filtro ?? string.Empty));
        }

        [HttpGet("productos/detalle")]
        public async Task<IActionResult> GetProductoDetalle(
            [FromQuery] int idUsuario,
            [FromQuery] string codigo)
        {
            idUsuario = ResolverIdUsuario(idUsuario);
            if (idUsuario <= 0) return Unauthorized();

            var producto = await _service.BuscarProductoParaDetalleAsync(idUsuario, codigo);
            return producto is null ? NotFound() : Ok(producto);
        }

        [HttpGet("buscar")]
        public async Task<IActionResult> BuscarFacturas([FromQuery] int idUsuario, [FromQuery] string texto)
        {
            idUsuario = ResolverIdUsuario(idUsuario);
            return idUsuario <= 0
                ? Unauthorized()
                : Ok(await _service.BuscarFacturasAutocompleteAsync(texto, idUsuario));
        }

        [HttpGet("series")]
        public async Task<IActionResult> GetSeries([FromQuery] int idUsuario)
        {
            idUsuario = ResolverIdUsuario(idUsuario);
            return idUsuario <= 0
                ? Unauthorized()
                : Ok(await _service.GetSeriesFacturaHabilitadasAsync(idUsuario));
        }

        [HttpPut("series/preferida")]
        public async Task<IActionResult> GuardarSeriePreferida([FromBody] SeriePreferidaDto dto)
        {
            var idUsuario = ResolverIdUsuario(dto.IdUsuario);
            if (idUsuario <= 0) return Unauthorized();

            await _service.SavePreferredSeriesForAllDocumentsAsync(idUsuario, dto.Serie);
            return NoContent();
        }

        [HttpGet("secuencia/estado-inicial")]
        public async Task<IActionResult> GetEstadoSecuenciaInicial(
            [FromQuery] int idUsuario,
            [FromQuery] int? codEmisor = null,
            [FromQuery] string? serie = null)
        {
            idUsuario = ResolverIdUsuario(idUsuario);
            if (idUsuario <= 0) return Unauthorized();

            var debeConfigurar = await _service.DebePreguntarSecuenciaInicialAsync(idUsuario, codEmisor, serie);
            var pendiente = await _service.ObtenerFacturaPendienteSecuenciaAsync(idUsuario, codEmisor, serie);
            return Ok(new { debeConfigurar, pendiente });
        }

        [HttpPost("secuencia/configurar-inicial")]
        public async Task<IActionResult> ConfigurarSecuenciaInicial([FromBody] ConfigurarSecuenciaFacturaDto dto)
        {
            var idUsuario = ResolverIdUsuario(dto.IdUsuario);
            if (idUsuario <= 0) return Unauthorized();

            await _service.ConfigurarSecuenciaInicialFacturaAsync(
                idUsuario,
                dto.YaFacturoAntes,
                dto.SecuenciaAnterior,
                dto.CodEmisor,
                dto.Serie);

            return NoContent();
        }
        [HttpGet]
        public async Task<IActionResult> Listar([FromQuery] int? idUsuario = null, [FromQuery] int top = 100)
        {
            var usuario = ResolverIdUsuario(idUsuario);
            var lista = usuario > 0
                ? await _service.ListarFacturasUsuarioAsync(usuario, top)
                : await _service.ListarFacturasAsync(top);
            return Ok(lista);
        }

        [HttpGet("impuestos/iva")]
        [HttpGet("api/impuestos/iva")]
        public async Task<IActionResult> GetIva()
            => Ok(await _service.GetPorcentajesIvaCatalogoAsync());


        [HttpGet("{codfactura:int}")]
        public async Task<IActionResult> Ver(int codfactura, [FromQuery] int? idUsuario = null)
        {
            var usuario = ResolverIdUsuario(idUsuario);
            var dto = usuario > 0
                ? await _service.GetFacturaCompletaUsuarioAsync(codfactura, usuario)
                : await _service.GetFacturaCompletaAsync(codfactura);
            if (dto == null) return NotFound();
            return Ok(dto);
        }

        [HttpGet("{codfactura:int}/xml")]
        public async Task<IActionResult> GetXml(
            int codfactura,
            [FromQuery] int idUsuario,
            [FromQuery] bool forzarRegeneracion = false)
        {
            idUsuario = ResolverIdUsuario(idUsuario);
            if (idUsuario <= 0) return Unauthorized();

            var url = await _service.AsegurarXmlFacturaUsuarioAsync(codfactura, idUsuario, forzarRegeneracion);
            return url is null ? NotFound() : Ok(new { url });
        }

        [HttpGet("{codfactura:int}/pdf")]
        public async Task<IActionResult> GetPdf(
            int codfactura,
            [FromQuery] int idUsuario,
            [FromQuery] FormatoImpresionDocumento formato = FormatoImpresionDocumento.A4)
        {
            idUsuario = ResolverIdUsuario(idUsuario);
            if (idUsuario <= 0) return Unauthorized();

            var url = await _service.AsegurarPdfFacturaUsuarioAsync(codfactura, idUsuario, formato);
            return url is null ? NotFound() : Ok(new { url });
        }

        [HttpPost("{codfactura:int}/reintentar-sri")]
        public async Task<IActionResult> ReintentarSri(int codfactura, [FromQuery] int idUsuario)
        {
            idUsuario = ResolverIdUsuario(idUsuario);
            if (idUsuario <= 0) return Unauthorized();
            if (!await FacturaPerteneceAlUsuarioAsync(codfactura, idUsuario)) return NotFound();

            return Ok(await _service.ReintentarEnvioSriFacturaAsync(codfactura));
        }

        [HttpPost("{codfactura:int}/enviar-correo")]
        public async Task<IActionResult> EnviarCorreo(int codfactura, [FromBody] EnviarFacturaCorreoDto dto)
        {
            var idUsuario = ResolverIdUsuario(dto.IdUsuario);
            if (idUsuario <= 0) return Unauthorized();
            if (!await FacturaPerteneceAlUsuarioAsync(codfactura, idUsuario)) return NotFound();

            var resultado = await _service.IntentarEnviarFacturaPorCorreoAsync(
                codfactura,
                forzarReenvio: dto.ForzarReenvio,
                correosCopia: dto.CorreosCopia);

            return resultado.Error ? BadRequest(resultado) : Ok(resultado);
        }

        [HttpPost("{codfactura:int}/no-cobrada")]
        public async Task<IActionResult> MarcarNoCobrada(int codfactura, [FromQuery] int idUsuario)
        {
            idUsuario = ResolverIdUsuario(idUsuario);
            if (idUsuario <= 0) return Unauthorized();

            var resultado = await _service.MarcarFacturaNoCobradaAsync(codfactura, idUsuario, HttpContext.RequestAborted);
            return resultado.Success
                ? Ok(new { mensaje = resultado.Message })
                : BadRequest(new { mensaje = resultado.Message });
        }

        [HttpDelete("{codfactura:int}")]
        public async Task<IActionResult> Anular(int codfactura, [FromQuery] int idUsuario)
        {
            idUsuario = ResolverIdUsuario(idUsuario);
            if (idUsuario <= 0) return Unauthorized();
            if (!await FacturaPerteneceAlUsuarioAsync(codfactura, idUsuario)) return NotFound();

            return await _service.AnularFacturaDirectoAsync(codfactura)
                ? NoContent()
                : BadRequest(new { mensaje = "No se pudo anular la factura." });
        }

        private async Task<bool> FacturaPerteneceAlUsuarioAsync(int codfactura, int idUsuario)
            => await _service.GetFacturaCompletaUsuarioAsync(codfactura, idUsuario) is not null;

        private int ResolverIdUsuario(int? idUsuarioSolicitud)
        {
            var claim = User.FindFirst("IdUsuario")?.Value
                ?? User.FindFirst(ClaimTypes.NameIdentifier)?.Value;

            return int.TryParse(claim, out var idUsuarioClaim) && idUsuarioClaim > 0
                ? idUsuarioClaim
                : idUsuarioSolicitud.GetValueOrDefault();
        }
    }
}



