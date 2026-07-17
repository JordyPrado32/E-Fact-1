using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Simetric.Data;
using Simetric.DTOs.EContax;
using Simetric.Services.EContax;

namespace Simetric.Controllers.EContax;

[ApiController]
[Route("api/e-contax/productos")]
public sealed class EContaxProductosController : ControllerBase
{
    private readonly AppDbContext _db;
    private readonly EContaxCatalogService _catalogService;
    private readonly EContaxTenantService _tenantService;

    public EContaxProductosController(
        AppDbContext db,
        EContaxCatalogService catalogService,
        EContaxTenantService tenantService)
    {
        _db = db;
        _catalogService = catalogService;
        _tenantService = tenantService;
    }

    [HttpGet("lookups")]
    public async Task<ActionResult> Lookups([FromQuery] int userId)
    {
        if (userId <= 0)
            return Unauthorized("Sesion no valida.");

        try
        {
            var userContext = await _tenantService.GetContextAsync(userId);
            var lookupsContext = await _catalogService.GetProductoLookupsContextAsync(userId);

            var tipos = await _db.Productotipos
                .AsNoTracking()
                .Where(x => x.Idusuario == userContext.IdUsuarioTitular && x.Estado == true)
                .OrderBy(x => x.Descripcion)
                .Select(x => new { x.Idtipoproducto, x.Descripcion })
                .ToListAsync();

            var subtipos = await _db.Productosubtipos
                .AsNoTracking()
                .Where(x => x.Idusuario == userContext.IdUsuarioTitular && x.Estado == "A")
                .OrderBy(x => x.Descripcion)
                .Select(x => new { x.Idsubtipo, x.Descripcion, x.Idtipoproducto })
                .ToListAsync();

            var impuestos = await _db.Codigoimpuestos
                .AsNoTracking()
                .OrderBy(x => x.Codigo)
                .Select(x => new { x.Codigo, x.Descripcion })
                .ToListAsync();

            var ivas = await _db.Porcentajeivas
                .AsNoTracking()
                .Where(x => x.Estado == "A" || x.Estado == "1")
                .OrderBy(x => x.Codigo)
                .Select(x => new { x.Codigo, x.Descripcion, Valor = x.Valor == null ? null : x.Valor.ToString() })
                .ToListAsync();

            return Ok(new
            {
                tipos,
                subtipos,
                impuestos,
                ivas,
                sucursales = lookupsContext.Sucursales,
                contexto = lookupsContext.Contexto
            });
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(ex.Message);
        }
    }

    [HttpGet]
    public async Task<ActionResult<List<EContaxProductoDto>>> GetAll([FromQuery] int userId, [FromQuery] int? sucursalId = null)
    {
        if (userId <= 0)
            return Unauthorized("Sesion no valida.");

        try
        {
            return Ok(await _catalogService.GetProductosAsync(userId, sucursalId));
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(ex.Message);
        }
    }

    [HttpGet("{codigo:int}")]
    public async Task<ActionResult> GetByCodigo(int codigo, [FromQuery] int userId)
    {
        if (userId <= 0)
            return Unauthorized("Sesion no valida.");

        try
        {
            var item = await _catalogService.GetProductoAsync(userId, codigo);
            return item is null ? NotFound() : Ok(item);
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(ex.Message);
        }
    }

    [HttpPost]
    public async Task<ActionResult> Create([FromQuery] int userId, [FromBody] EContaxProductoUpsertDto model)
    {
        if (userId <= 0)
            return Unauthorized("Sesion no valida.");

        try
        {
            var id = await _catalogService.CrearProductoAsync(userId, model);
            return Ok(new { codigo = id });
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(ex.Message);
        }
    }

    [HttpPut("{codigo:int}")]
    public async Task<ActionResult> Update(int codigo, [FromQuery] int userId, [FromBody] EContaxProductoUpsertDto model)
    {
        if (userId <= 0)
            return Unauthorized("Sesion no valida.");

        try
        {
            await _catalogService.ActualizarProductoAsync(userId, codigo, model);
            return NoContent();
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(ex.Message);
        }
    }

    [HttpDelete("{codigo:int}")]
    public async Task<ActionResult> Delete(int codigo, [FromQuery] int userId)
    {
        if (userId <= 0)
            return Unauthorized("Sesion no valida.");

        try
        {
            await _catalogService.DesactivarProductoAsync(userId, codigo);
            return NoContent();
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(ex.Message);
        }
    }
}
