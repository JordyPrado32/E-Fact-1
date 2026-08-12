using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Simetric.Data;
using Simetric.Models;

namespace Simetric.Controllers;

[ApiController]
[Route("api/roles-permisos")]
public class RolesPermisosController : ControllerBase
{
    private readonly AppDbContext _context;

    public RolesPermisosController(AppDbContext context)
    {
        _context = context;
    }

    [HttpGet("roles")]
    public async Task<IActionResult> GetRoles([FromQuery] string? search = null)
    {
        var term = Clean(search).ToLower();
        var roles = await _context.TipoUsuario
            .AsNoTracking()
            .Where(x => term == "" || x.NombreTipo.ToLower().Contains(term) || (x.Descripcion ?? "").ToLower().Contains(term))
            .OrderBy(x => x.NombreTipo)
            .Select(x => new { id = x.IdTipoUsuario, nombre = x.NombreTipo, descripcion = x.Descripcion, estado = x.Estado })
            .ToListAsync();
        return Ok(roles);
    }

    [HttpPost("roles")]
    public async Task<IActionResult> CreateRole([FromBody] RolDto model)
    {
        var nombre = Clean(model.Nombre);
        if (nombre == "") return BadRequest("El nombre del rol es obligatorio.");
        if (await _context.TipoUsuario.AnyAsync(x => x.NombreTipo.ToLower() == nombre.ToLower())) return BadRequest("Ya existe un rol con ese nombre.");

        var entity = new TipoUsuario { NombreTipo = nombre, Descripcion = Clean(model.Descripcion), Estado = model.Estado ?? true };
        _context.TipoUsuario.Add(entity);
        await _context.SaveChangesAsync();
        return Ok(new { id = entity.IdTipoUsuario, nombre = entity.NombreTipo, descripcion = entity.Descripcion, estado = entity.Estado });
    }

    [HttpPut("roles/{id:int}")]
    public async Task<IActionResult> UpdateRole(int id, [FromBody] RolDto model)
    {
        var entity = await _context.TipoUsuario.FindAsync(id);
        if (entity is null) return NotFound();
        var nombre = Clean(model.Nombre);
        if (nombre == "") return BadRequest("El nombre del rol es obligatorio.");
        if (await _context.TipoUsuario.AnyAsync(x => x.IdTipoUsuario != id && x.NombreTipo.ToLower() == nombre.ToLower())) return BadRequest("Ya existe un rol con ese nombre.");

        entity.NombreTipo = nombre;
        entity.Descripcion = Clean(model.Descripcion);
        entity.Estado = model.Estado ?? entity.Estado ?? true;
        await _context.SaveChangesAsync();
        return Ok(new { id = entity.IdTipoUsuario, nombre = entity.NombreTipo, descripcion = entity.Descripcion, estado = entity.Estado });
    }

    [HttpDelete("roles/{id:int}")]
    public async Task<IActionResult> DeleteRole(int id)
    {
        var entity = await _context.TipoUsuario.FindAsync(id);
        if (entity is null) return NotFound();
        entity.Estado = false;
        await _context.SaveChangesAsync();
        return Ok();
    }

    private static string Clean(string? value) => (value ?? string.Empty).Trim();
}

public sealed class RolDto
{
    public string? Nombre { get; set; }
    public string? Descripcion { get; set; }
    public bool? Estado { get; set; }
}
