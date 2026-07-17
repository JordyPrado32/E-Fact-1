using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Simetric.Data;

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
    public async Task<IActionResult> Get()
    {
        await using var db = await _dbFactory.CreateDbContextAsync();

        var data = await db.Identificacion
            .AsNoTracking()
            .OrderBy(x => x.IdeSec)
            .Select(x => new
            {
                id = x.IdeSec,
                descripcion = x.IdeDescripcion
            })
            .ToListAsync();

        return Ok(data);
    }
}
