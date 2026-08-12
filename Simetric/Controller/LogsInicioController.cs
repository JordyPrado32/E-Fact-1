using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Simetric.Data;

namespace Simetric.Controllers;

[ApiController]
[Route("api/logs-inicio")]
public class LogsInicioController : ControllerBase
{
    private readonly AppDbContext _context;

    public LogsInicioController(AppDbContext context)
    {
        _context = context;
    }

    [HttpGet]
    public async Task<IActionResult> Get([FromQuery] string? search = null, [FromQuery] DateTime? desde = null, [FromQuery] DateTime? hasta = null)
    {
        var term = Clean(search).ToLower();
        var query = _context.LogIniciosSesiones
            .AsNoTracking()
            .Include(x => x.IdUsuarioNavigation)
            .AsQueryable();

        if (desde.HasValue) query = query.Where(x => x.FechaAcceso >= desde.Value);
        if (hasta.HasValue) query = query.Where(x => x.FechaAcceso < hasta.Value.AddDays(1));
        if (term != "")
        {
            query = query.Where(x =>
                (x.IdUsuarioNavigation != null && ((x.IdUsuarioNavigation.Nombres + " " + x.IdUsuarioNavigation.Apellidos).ToLower().Contains(term) || x.IdUsuarioNavigation.Email.ToLower().Contains(term))) ||
                (x.DireccionIp ?? "").ToLower().Contains(term) ||
                (x.DetalleError ?? "").ToLower().Contains(term));
        }

        var data = await query
            .OrderByDescending(x => x.FechaAcceso)
            .Take(300)
            .Select(x => new
            {
                id = x.IdLog,
                usuario = x.IdUsuarioNavigation == null ? "Sistema / proceso" : (x.IdUsuarioNavigation.Nombres + " " + x.IdUsuarioNavigation.Apellidos).Trim(),
                correo = x.IdUsuarioNavigation == null ? null : x.IdUsuarioNavigation.Email,
                fechaAcceso = x.FechaAcceso,
                direccionIp = x.DireccionIp,
                navegador = x.Navegador,
                exitoso = x.Exitoso,
                detalleError = x.DetalleError
            })
            .ToListAsync();

        return Ok(data);
    }

    [HttpDelete("{id:int}")]
    public async Task<IActionResult> Delete(int id)
    {
        var entity = await _context.LogIniciosSesiones.FindAsync(id);
        if (entity is null) return NotFound();
        _context.LogIniciosSesiones.Remove(entity);
        await _context.SaveChangesAsync();
        return Ok();
    }

    [HttpDelete]
    public async Task<IActionResult> DeleteRange([FromQuery] DateTime? desde = null, [FromQuery] DateTime? hasta = null)
    {
        var query = _context.LogIniciosSesiones.AsQueryable();
        if (desde.HasValue) query = query.Where(x => x.FechaAcceso >= desde.Value);
        if (hasta.HasValue) query = query.Where(x => x.FechaAcceso < hasta.Value.AddDays(1));

        var logs = await query.Take(5000).ToListAsync();
        _context.LogIniciosSesiones.RemoveRange(logs);
        await _context.SaveChangesAsync();
        return Ok(new { eliminados = logs.Count });
    }

    private static string Clean(string? value) => (value ?? string.Empty).Trim();
}
