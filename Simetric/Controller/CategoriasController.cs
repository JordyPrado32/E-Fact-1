using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Simetric.Data;
using Simetric.Models;

namespace Simetric.Controllers;

[ApiController]
[Route("api/categorias")]
public class CategoriasController : ControllerBase
{
    private readonly AppDbContext _db;

    public CategoriasController(AppDbContext db) => _db = db;

    public sealed class CategoriaUpsertDto
    {
        public string? Descripcion { get; set; }
        public bool? Estado { get; set; }
    }

    private async Task<int?> GetOwnerIdAsync(int userId)
    {
        if (userId <= 0) return null;

        var user = await _db.Usuarios
            .Where(u => u.IdUsuario == userId)
            .Select(u => new { u.IdUsuario, u.idJefe })
            .FirstOrDefaultAsync();

        return user is null ? null : user.idJefe ?? user.IdUsuario;
    }

    [HttpGet]
    public async Task<ActionResult> GetAll([FromQuery] int userId, [FromQuery] bool incluirInactivos = false)
    {
        var ownerId = await GetOwnerIdAsync(userId);
        if (ownerId is null) return Unauthorized("Sesión no válida.");

        var query = _db.Productotipos.AsNoTracking().Where(x => x.Idusuario == ownerId.Value);
        if (!incluirInactivos)
            query = query.Where(x => x.Estado == true);

        var data = await query
            .OrderBy(x => x.Descripcion)
            .Select(x => new { IdCategoria = x.Idtipoproducto, x.Descripcion, x.Estado })
            .ToListAsync();

        return Ok(data);
    }

    [HttpPost]
    public async Task<ActionResult> Create([FromQuery] int userId, [FromBody] CategoriaUpsertDto model)
    {
        var ownerId = await GetOwnerIdAsync(userId);
        if (ownerId is null) return Unauthorized("Sesión no válida.");

        var descripcion = (model.Descripcion ?? string.Empty).Trim();
        if (string.IsNullOrWhiteSpace(descripcion)) return BadRequest("La descripcion de la categoria es obligatoria.");

        var existente = await _db.Productotipos
            .FirstOrDefaultAsync(x => x.Idusuario == ownerId.Value && x.Descripcion != null && x.Descripcion.Trim().ToUpper() == descripcion.ToUpper());

        if (existente is not null)
        {
            existente.Descripcion = descripcion;
            existente.Estado = model.Estado ?? true;
            await _db.SaveChangesAsync();
            return Ok(new { IdCategoria = existente.Idtipoproducto });
        }

        var entity = new Productotipo { Descripcion = descripcion, Estado = model.Estado ?? true, Idusuario = ownerId.Value };
        _db.Productotipos.Add(entity);
        await _db.SaveChangesAsync();
        return Ok(new { IdCategoria = entity.Idtipoproducto });
    }

    [HttpPut("{idCategoria:int}")]
    public async Task<ActionResult> Update(int idCategoria, [FromQuery] int userId, [FromBody] CategoriaUpsertDto model)
    {
        var ownerId = await GetOwnerIdAsync(userId);
        if (ownerId is null) return Unauthorized("Sesión no válida.");

        var descripcion = (model.Descripcion ?? string.Empty).Trim();
        if (string.IsNullOrWhiteSpace(descripcion)) return BadRequest("La descripcion de la categoria es obligatoria.");

        var entity = await _db.Productotipos.FirstOrDefaultAsync(x => x.Idtipoproducto == idCategoria && x.Idusuario == ownerId.Value);
        if (entity is null) return NotFound("La categoria no existe o no tiene permisos.");

        var duplicada = await _db.Productotipos.AnyAsync(x => x.Idusuario == ownerId.Value && x.Idtipoproducto != idCategoria && x.Descripcion != null && x.Descripcion.Trim().ToUpper() == descripcion.ToUpper());
        if (duplicada) return BadRequest("Ya existe una categoria con esa descripcion.");

        entity.Descripcion = descripcion;
        entity.Estado = model.Estado ?? entity.Estado;
        await _db.SaveChangesAsync();
        return NoContent();
    }

    [HttpPut("{idCategoria:int}/desactivar")]
    public async Task<ActionResult> Desactivar(int idCategoria, [FromQuery] int userId)
    {
        var ownerId = await GetOwnerIdAsync(userId);
        if (ownerId is null) return Unauthorized("Sesión no válida.");

        var entity = await _db.Productotipos.FirstOrDefaultAsync(x => x.Idtipoproducto == idCategoria && x.Idusuario == ownerId.Value);
        if (entity is null) return NotFound("La categoria no existe o no tiene permisos.");

        entity.Estado = false;
        await _db.SaveChangesAsync();
        return NoContent();
    }
}

[ApiController]
[Route("api/subcategorias")]
public class SubcategoriasController : ControllerBase
{
    private readonly AppDbContext _db;

    public SubcategoriasController(AppDbContext db) => _db = db;

    public sealed class SubcategoriaUpsertDto
    {
        public string? Descripcion { get; set; }
        public int? IdCategoria { get; set; }
        public bool? Estado { get; set; }
    }

    private async Task<int?> GetOwnerIdAsync(int userId)
    {
        if (userId <= 0) return null;

        var user = await _db.Usuarios
            .Where(u => u.IdUsuario == userId)
            .Select(u => new { u.IdUsuario, u.idJefe })
            .FirstOrDefaultAsync();

        return user is null ? null : user.idJefe ?? user.IdUsuario;
    }

    [HttpGet]
    public async Task<ActionResult> GetAll([FromQuery] int userId, [FromQuery] bool incluirInactivos = false, [FromQuery] int? categoriaId = null)
    {
        var ownerId = await GetOwnerIdAsync(userId);
        if (ownerId is null) return Unauthorized("Sesión no válida.");

        var query = _db.Productosubtipos.AsNoTracking().Where(x => x.Idusuario == ownerId.Value);
        if (!incluirInactivos)
            query = query.Where(x => x.Estado == "A");
        if (categoriaId.HasValue)
            query = query.Where(x => x.Idtipoproducto == categoriaId.Value);

        var data = await query
            .OrderBy(x => x.Descripcion)
            .Select(x => new
            {
                IdSubcategoria = x.Idsubtipo,
                IdCategoria = x.Idtipoproducto,
                CategoriaDescripcion = x.IdtipoproductoNavigation!.Descripcion,
                x.Descripcion,
                Estado = x.Estado == "A"
            })
            .ToListAsync();

        return Ok(data);
    }

    [HttpPost]
    public async Task<ActionResult> Create([FromQuery] int userId, [FromBody] SubcategoriaUpsertDto model) =>
        await Save(null, userId, model);

    [HttpPut("{idSubcategoria:int}")]
    public async Task<ActionResult> Update(int idSubcategoria, [FromQuery] int userId, [FromBody] SubcategoriaUpsertDto model) =>
        await Save(idSubcategoria, userId, model);

    [HttpPut("{idSubcategoria:int}/desactivar")]
    public async Task<ActionResult> Desactivar(int idSubcategoria, [FromQuery] int userId)
    {
        var ownerId = await GetOwnerIdAsync(userId);
        if (ownerId is null) return Unauthorized("Sesión no válida.");

        var entity = await _db.Productosubtipos.FirstOrDefaultAsync(x => x.Idsubtipo == idSubcategoria && x.Idusuario == ownerId.Value);
        if (entity is null) return NotFound("La subcategoria no existe o no tiene permisos.");

        entity.Estado = "I";
        await _db.SaveChangesAsync();
        return NoContent();
    }

    private async Task<ActionResult> Save(int? idSubcategoria, int userId, SubcategoriaUpsertDto model)
    {
        var ownerId = await GetOwnerIdAsync(userId);
        if (ownerId is null) return Unauthorized("Sesión no válida.");

        var descripcion = (model.Descripcion ?? string.Empty).Trim();
        if (string.IsNullOrWhiteSpace(descripcion)) return BadRequest("La descripcion de la subcategoria es obligatoria.");
        if (!model.IdCategoria.HasValue || model.IdCategoria <= 0) return BadRequest("Debe seleccionar una categoria.");

        var categoriaValida = await _db.Productotipos.AnyAsync(x => x.Idtipoproducto == model.IdCategoria.Value && x.Idusuario == ownerId.Value && x.Estado == true);
        if (!categoriaValida) return BadRequest("La categoria seleccionada no pertenece a su grupo o está inactiva.");

        var duplicada = await _db.Productosubtipos.AnyAsync(x => x.Idusuario == ownerId.Value && x.Idsubtipo != (idSubcategoria ?? 0) && x.Idtipoproducto == model.IdCategoria.Value && x.Descripcion != null && x.Descripcion.Trim().ToUpper() == descripcion.ToUpper());
        if (duplicada) return BadRequest("Ya existe una subcategoria con esa descripcion para la categoria seleccionada.");

        if (!idSubcategoria.HasValue)
        {
            var entity = new Productosubtipo { Descripcion = descripcion, Idtipoproducto = model.IdCategoria.Value, Estado = model.Estado == false ? "I" : "A", Idusuario = ownerId.Value };
            _db.Productosubtipos.Add(entity);
            await _db.SaveChangesAsync();
            return Ok(new { IdSubcategoria = entity.Idsubtipo });
        }

        var actual = await _db.Productosubtipos.FirstOrDefaultAsync(x => x.Idsubtipo == idSubcategoria.Value && x.Idusuario == ownerId.Value);
        if (actual is null) return NotFound("La subcategoria no existe o no tiene permisos.");

        actual.Descripcion = descripcion;
        actual.Idtipoproducto = model.IdCategoria.Value;
        actual.Estado = model.Estado == false ? "I" : "A";
        await _db.SaveChangesAsync();
        return NoContent();
    }
}
