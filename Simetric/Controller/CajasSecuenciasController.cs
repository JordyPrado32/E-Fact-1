using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Simetric.Data;

namespace Simetric.Controllers;

[ApiController]
[Route("api/cajas-secuencias")]
public class CajasSecuenciasController : ControllerBase
{
    private readonly AppDbContext _context;

    public CajasSecuenciasController(AppDbContext context)
    {
        _context = context;
    }

    [HttpGet]
    public async Task<IActionResult> Get([FromQuery] string? search = null)
    {
        var term = (search ?? string.Empty).Trim().ToLower();
        var data = await _context.Caja
            .AsNoTracking()
            .Where(c => term == "" ||
                ("Caja " + (c.NumCaja ?? 0)).ToLower().Contains(term) ||
                (c.SerieFactura ?? "").ToLower().Contains(term) ||
                (c.IdUsuario == null ? "" : ("Usuario " + c.IdUsuario)).ToLower().Contains(term))
            .OrderByDescending(c => c.Sec)
            .Take(300)
            .Select(c => new
            {
                id = c.Sec,
                numCaja = c.NumCaja,
                idUsuario = c.IdUsuario,
                serieFactura = c.SerieFactura,
                serieGuia = c.SerieGuia,
                serieNotasCred = c.SerieNotasCred,
                ultimoSecuencialFactura = c.UltimoSecuencialFactura,
                estado = c.Estado
            })
            .ToListAsync();

        return Ok(data);
    }
}
