using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Simetric.Data;
using Simetric.Models;

namespace Simetric.Controllers;

[ApiController]
[Route("api/identificaciones")]
public class IdentificacionesController : ControllerBase
{
    private readonly IDbContextFactory<AppDbContext> _dbFactory;

    public IdentificacionesController(IDbContextFactory<AppDbContext> dbFactory)
    {
        _dbFactory = dbFactory;
    }

    [HttpGet]
    public async Task<IActionResult> Get([FromQuery] string? search = null)
    {
        await using var db = await _dbFactory.CreateDbContextAsync();
        var term = (search ?? string.Empty).Trim().ToLower();

        var data = await db.Identificacion
            .AsNoTracking()
            .Where(x => term == "" || x.IdeCodigo.ToLower().Contains(term) || (x.IdeDescripcion ?? "").ToLower().Contains(term))
            .OrderBy(x => x.IdeCodigo)
            .Select(ToDto())
            .ToListAsync();

        return Ok(data);
    }

    [HttpPost]
    public async Task<IActionResult> Create([FromBody] IdentificacionDto model)
    {
        await using var db = await _dbFactory.CreateDbContextAsync();
        var codigo = Clean(model.Codigo);
        var descripcion = Clean(model.Descripcion);
        if (codigo == "" || descripcion == "") return BadRequest("Codigo y descripcion son obligatorios.");
        if (await db.Identificacion.AnyAsync(x => x.IdeCodigo == codigo)) return BadRequest("Ya existe una identificacion con ese codigo.");

        var entity = new Identificacion { IdeCodigo = codigo, IdeDescripcion = descripcion, Estado = model.Estado ?? true };
        db.Identificacion.Add(entity);
        await db.SaveChangesAsync();
        return Ok(ToDto(entity));
    }

    [HttpPut("{id:int}")]
    public async Task<IActionResult> Update(int id, [FromBody] IdentificacionDto model)
    {
        await using var db = await _dbFactory.CreateDbContextAsync();
        var entity = await db.Identificacion.FindAsync(id);
        if (entity is null) return NotFound();

        var codigo = Clean(model.Codigo);
        var descripcion = Clean(model.Descripcion);
        if (codigo == "" || descripcion == "") return BadRequest("Codigo y descripcion son obligatorios.");
        if (await db.Identificacion.AnyAsync(x => x.IdeSec != id && x.IdeCodigo == codigo)) return BadRequest("Ya existe una identificacion con ese codigo.");

        entity.IdeCodigo = codigo;
        entity.IdeDescripcion = descripcion;
        entity.Estado = model.Estado ?? entity.Estado ?? true;
        await db.SaveChangesAsync();
        return Ok(ToDto(entity));
    }

    [HttpDelete("{id:int}")]
    public async Task<IActionResult> Delete(int id)
    {
        await using var db = await _dbFactory.CreateDbContextAsync();
        var entity = await db.Identificacion.FindAsync(id);
        if (entity is null) return NotFound();

        entity.Estado = false;
        await db.SaveChangesAsync();
        return Ok();
    }

    private static System.Linq.Expressions.Expression<Func<Identificacion, object>> ToDto() => x => new
    {
        id = x.IdeSec,
        codigo = x.IdeCodigo,
        descripcion = x.IdeDescripcion,
        estado = x.Estado
    };

    private static object ToDto(Identificacion x) => new { id = x.IdeSec, codigo = x.IdeCodigo, descripcion = x.IdeDescripcion, estado = x.Estado };
    private static string Clean(string? value) => (value ?? string.Empty).Trim();
}

public sealed class IdentificacionDto
{
    public string? Codigo { get; set; }
    public string? Descripcion { get; set; }
    public bool? Estado { get; set; }
}
