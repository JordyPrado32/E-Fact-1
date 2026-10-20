using Microsoft.AspNetCore.Mvc;
using Simetric.DTOs.EContax;
using Simetric.Services.EContax;

namespace Simetric.Controllers.EContax;

[ApiController]
[Route("api/e-contax/compras/recepciones")]
public sealed class EContaxRecepcionesCompraController : EContaxApiControllerBase
{
    protected override string RutaPermiso => EContaxRoutes.ComprasRecepciones;

    private readonly EContaxRecepcionCompraService _service;

    public EContaxRecepcionesCompraController(EContaxRecepcionCompraService service)
    {
        _service = service;
    }

    [HttpGet]
    public async Task<ActionResult<List<EContaxRecepcionCompraListaDto>>> Listar(
        [FromQuery] int userId,
        [FromQuery] string? search = null,
        [FromQuery] string? estado = null)
    {
        if (userId <= 0)
            return Unauthorized("Sesion no valida.");

        try
        {
            return Ok(await _service.ListarAsync(userId, search, estado));
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(ex.Message);
        }
    }

    [HttpGet("ordenes-disponibles")]
    public async Task<ActionResult<List<EContaxRecepcionOrdenDto>>> OrdenesDisponibles([FromQuery] int userId)
    {
        if (userId <= 0)
            return Unauthorized("Sesion no valida.");

        try
        {
            return Ok(await _service.ListarOrdenesDisponiblesAsync(userId));
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(ex.Message);
        }
    }

    [HttpGet("ordenes-disponibles/{idOrdenCompra:int}")]
    public async Task<ActionResult<EContaxRecepcionOrdenDto>> OrdenDisponible(int idOrdenCompra, [FromQuery] int userId)
    {
        if (userId <= 0)
            return Unauthorized("Sesion no valida.");

        try
        {
            var orden = await _service.ObtenerOrdenDisponibleAsync(userId, idOrdenCompra);
            return orden is null ? NotFound() : Ok(orden);
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(ex.Message);
        }
    }

    [HttpGet("{id:int}")]
    public async Task<ActionResult<EContaxRecepcionCompraDto>> Obtener(int id, [FromQuery] int userId)
    {
        if (userId <= 0)
            return Unauthorized("Sesion no valida.");

        try
        {
            var recepcion = await _service.ObtenerAsync(userId, id);
            return recepcion is null ? NotFound() : Ok(recepcion);
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(ex.Message);
        }
    }

    [HttpPost]
    public async Task<ActionResult<EContaxRecepcionCompraDto>> Crear(
        [FromQuery] int userId,
        [FromBody] EContaxRecepcionCompraUpsertDto dto)
    {
        if (userId <= 0)
            return Unauthorized("Sesion no valida.");

        try
        {
            return Ok(await _service.CrearAsync(userId, dto));
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(ex.Message);
        }
    }

    [HttpPut("{id:int}")]
    public async Task<ActionResult<EContaxRecepcionCompraDto>> Actualizar(
        int id,
        [FromQuery] int userId,
        [FromBody] EContaxRecepcionCompraUpsertDto dto)
    {
        if (userId <= 0)
            return Unauthorized("Sesion no valida.");

        try
        {
            return Ok(await _service.ActualizarAsync(userId, id, dto));
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(ex.Message);
        }
    }

    [HttpPut("{id:int}/confirmar")]
    public async Task<ActionResult<EContaxRecepcionCompraDto>> Confirmar(int id, [FromQuery] int userId)
    {
        if (userId <= 0)
            return Unauthorized("Sesion no valida.");

        try
        {
            return Ok(await _service.ConfirmarAsync(userId, id));
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(ex.Message);
        }
    }

    [HttpPut("{id:int}/anular")]
    public async Task<ActionResult<EContaxRecepcionCompraDto>> Anular(
        int id,
        [FromQuery] int userId,
        [FromBody] EContaxOrdenCompraEstadoDto dto)
    {
        if (userId <= 0)
            return Unauthorized("Sesion no valida.");

        try
        {
            return Ok(await _service.AnularAsync(userId, id, dto.Observacion));
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(ex.Message);
        }
    }
}
