using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Simetric.Data;
using Simetric.Services;

namespace Simetric.Controllers;

[ApiController]
[Route("api/ubicacion")]
public class UbicacionController : ControllerBase
{
    private readonly IDbContextFactory<AppDbContext> _dbFactory;

    public UbicacionController(IDbContextFactory<AppDbContext> dbFactory)
    {
        _dbFactory = dbFactory;
    }

    // GET: api/ubicacion/paises
    [HttpGet("paises")]
    public async Task<IActionResult> GetPaises()
    {
        await using var db = await _dbFactory.CreateDbContextAsync();
        await PaisCatalogoService.AsegurarCatalogoAsync(db);

        var data = await db.Paises
            .AsNoTracking()
            .OrderBy(x => x.Descripcion)
            .Select(x => new { idPais = x.IdPais, descripcion = x.Descripcion })
            .ToListAsync();

        return Ok(data);
    }

    // GET: api/ubicacion/provincias?paisId=1
    [HttpGet("provincias")]
    public async Task<IActionResult> GetProvincias([FromQuery] int paisId)
    {
        await using var db = await _dbFactory.CreateDbContextAsync();

        var data = await db.Provincias
            .AsNoTracking()
            .Where(x => x.IdPais == paisId)
            .OrderBy(x => x.Descripcion)
            .Select(x => new { idProvincia = x.IdProvincia, descripcion = x.Descripcion, idPais = x.IdPais })
            .ToListAsync();

        return Ok(data);
    }

    // GET: api/ubicacion/ciudades?provinciaId=1
    [HttpGet("ciudades")]
    public async Task<IActionResult> GetCiudades([FromQuery] int provinciaId)
    {
        await using var db = await _dbFactory.CreateDbContextAsync();

        var data = await db.Ciudades
            .AsNoTracking()
            .Where(x => x.IdProvincia == provinciaId)
            .OrderBy(x => x.Descripcion)
            .Select(x => new { idCiudad = x.IdCiudad, descripcion = x.Descripcion, idProvincia = x.IdProvincia })
            .ToListAsync();

        return Ok(data);
    }
}
