using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Simetric.Data;

namespace Simetric.Controllers;

[ApiController]
[Route("api/sql-auditoria")]
public class SqlAuditoriaController : ControllerBase
{
    private readonly AppDbContext _context;

    public SqlAuditoriaController(AppDbContext context)
    {
        _context = context;
    }

    [HttpGet]
    public async Task<IActionResult> Get([FromQuery] string? search = null, [FromQuery] DateTime? desde = null, [FromQuery] DateTime? hasta = null)
    {
        var term = Clean(search).ToLower();
        var query = _context.Auditorias
            .AsNoTracking()
            .Include(x => x.Usuario)
            .AsQueryable();

        if (desde.HasValue) query = query.Where(x => x.Fecha >= desde.Value);
        if (hasta.HasValue) query = query.Where(x => x.Fecha < hasta.Value.AddDays(1));
        if (term != "")
        {
            query = query.Where(x =>
                (x.Accion ?? "").ToLower().Contains(term) ||
                (x.Detalles ?? "").ToLower().Contains(term) ||
                (x.ValoresPrevios ?? "").ToLower().Contains(term) ||
                (x.ValorNuevo ?? "").ToLower().Contains(term) ||
                (x.Usuario != null && ((x.Usuario.Nombres + " " + x.Usuario.Apellidos).ToLower().Contains(term) || x.Usuario.Email.ToLower().Contains(term))));
        }

        var data = await query
            .OrderByDescending(x => x.Fecha)
            .Take(300)
            .Select(x => new
            {
                id = x.IdAuditoria,
                fecha = x.Fecha,
                usuario = x.Usuario == null ? "Sistema / proceso" : (x.Usuario.Nombres + " " + x.Usuario.Apellidos).Trim(),
                correo = x.Usuario == null ? null : x.Usuario.Email,
                accion = x.Accion,
                valoresPrevios = x.ValoresPrevios,
                valorNuevo = x.ValorNuevo,
                detalles = x.Detalles
            })
            .ToListAsync();

        return Ok(data);
    }

    [HttpDelete("{id:int}")]
    public async Task<IActionResult> Delete(int id)
    {
        var entity = await _context.Auditorias.FindAsync(id);
        if (entity is null) return NotFound();
        _context.Auditorias.Remove(entity);
        await _context.SaveChangesAsync();
        return Ok();
    }

    private static string Clean(string? value) => (value ?? string.Empty).Trim();
}
