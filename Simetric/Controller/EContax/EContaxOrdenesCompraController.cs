using Microsoft.AspNetCore.Mvc;
using Simetric.DTOs.EContax;
using Simetric.Services.EContax;

namespace Simetric.Controllers.EContax;

[ApiController]
[Route("api/e-contax/compras/ordenes")]
public sealed class EContaxOrdenesCompraController : EContaxApiControllerBase
{
    protected override string RutaPermiso => EContaxRoutes.ComprasOrdenes;

    private readonly EContaxOrdenCompraService _service;

    public EContaxOrdenesCompraController(EContaxOrdenCompraService service)
    {
        _service = service;
    }

    [HttpGet]
    public async Task<ActionResult<List<EContaxOrdenCompraListaDto>>> Listar(
        [FromQuery] int userId,
        [FromQuery] string? search = null,
        [FromQuery] string? estado = null,
        [FromQuery] DateTime? fechaDesde = null,
        [FromQuery] DateTime? fechaHasta = null)
    {
        if (userId <= 0)
            return Unauthorized("Sesion no valida.");

        try
        {
            return Ok(await _service.ListarAsync(userId, search, estado, fechaDesde, fechaHasta));
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(ex.Message);
        }
    }

    [HttpGet("{id:int}")]
    public async Task<ActionResult<EContaxOrdenCompraDto>> Obtener(int id, [FromQuery] int userId)
    {
        if (userId <= 0)
            return Unauthorized("Sesion no valida.");

        try
        {
            var orden = await _service.ObtenerAsync(userId, id);
            return orden is null ? NotFound() : Ok(orden);
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(ex.Message);
        }
    }

    [HttpGet("proveedores")]
    public async Task<ActionResult<List<EContaxProveedorLookupDto>>> Proveedores([FromQuery] int userId)
    {
        if (userId <= 0)
            return Unauthorized("Sesion no valida.");

        try
        {
            return Ok(await _service.ListarProveedoresAsync(userId));
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(ex.Message);
        }
    }

    [HttpGet("productos")]
    public async Task<ActionResult> Productos([FromQuery] int userId, [FromQuery] string? search = null, [FromQuery] int? idProveedor = null)
    {
        if (userId <= 0)
            return Unauthorized("Sesion no valida.");

        try
        {
            return Ok(await _service.BuscarProductosAsync(userId, search, idProveedor));
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(ex.Message);
        }
    }

    [HttpPost]
    public async Task<ActionResult<EContaxOrdenCompraDto>> Crear(
        [FromQuery] int userId,
        [FromBody] EContaxOrdenCompraUpsertDto dto)
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
    public async Task<ActionResult<EContaxOrdenCompraDto>> Actualizar(
        int id,
        [FromQuery] int userId,
        [FromBody] EContaxOrdenCompraUpsertDto dto)
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

    [HttpPut("{id:int}/estado")]
    public async Task<ActionResult<EContaxOrdenCompraDto>> CambiarEstado(
        int id,
        [FromQuery] int userId,
        [FromBody] EContaxOrdenCompraEstadoDto dto)
    {
        if (userId <= 0)
            return Unauthorized("Sesion no valida.");

        try
        {
            return Ok(await _service.CambiarEstadoAsync(userId, id, dto.Estado, dto.Observacion));
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(ex.Message);
        }
    }
}
