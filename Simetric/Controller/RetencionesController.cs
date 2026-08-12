using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Simetric.Data;
using Simetric.Models;

namespace Simetric.Controllers;

[ApiController]
[Route("api/retenciones-catalogo")]
public class RetencionesController : ControllerBase
{
    private readonly AppDbContext _context;

    public RetencionesController(AppDbContext context)
    {
        _context = context;
    }

    [HttpGet("iva")]
    public async Task<IActionResult> GetIva([FromQuery] string? search = null)
    {
        var term = Clean(search).ToLower();
        var data = await _context.RetencionIva
            .AsNoTracking()
            .Where(x => term == "" || x.Codigo.ToString().Contains(term) || (x.Descripcion ?? "").ToLower().Contains(term))
            .OrderBy(x => x.Codigo)
            .ToListAsync();
        return Ok(data);
    }

    [HttpPost("iva")]
    public async Task<IActionResult> CreateIva([FromBody] RetencionDecimalDto model)
    {
        if (model.Codigo is null) return BadRequest("Codigo requerido.");
        if (await _context.RetencionIva.AnyAsync(x => x.Codigo == model.Codigo)) return BadRequest("Ya existe una retencion IVA con ese codigo.");
        var entity = new RetencionIva { Codigo = model.Codigo.Value, Descripcion = Clean(model.Descripcion), Valor = model.Valor };
        _context.RetencionIva.Add(entity);
        await _context.SaveChangesAsync();
        return Ok(entity);
    }

    [HttpPut("iva/{codigo:int}")]
    public async Task<IActionResult> UpdateIva(int codigo, [FromBody] RetencionDecimalDto model)
    {
        var entity = await _context.RetencionIva.FindAsync(codigo);
        if (entity is null) return NotFound();
        entity.Descripcion = Clean(model.Descripcion);
        entity.Valor = model.Valor;
        await _context.SaveChangesAsync();
        return Ok(entity);
    }

    [HttpDelete("iva/{codigo:int}")]
    public async Task<IActionResult> DeleteIva(int codigo)
    {
        var entity = await _context.RetencionIva.FindAsync(codigo);
        if (entity is null) return NotFound();
        _context.RetencionIva.Remove(entity);
        await _context.SaveChangesAsync();
        return Ok();
    }

    [HttpGet("isd")]
    public async Task<IActionResult> GetIsd([FromQuery] string? search = null)
    {
        var term = Clean(search).ToLower();
        var data = await _context.RetencionIsd
            .AsNoTracking()
            .Where(x => term == "" || x.Codigo.ToString().Contains(term) || (x.Descripcion ?? "").ToLower().Contains(term))
            .OrderBy(x => x.Codigo)
            .ToListAsync();
        return Ok(data);
    }

    [HttpPost("isd")]
    public async Task<IActionResult> CreateIsd([FromBody] RetencionDecimalDto model)
    {
        if (model.Codigo is null) return BadRequest("Codigo requerido.");
        if (await _context.RetencionIsd.AnyAsync(x => x.Codigo == model.Codigo)) return BadRequest("Ya existe una retencion ISD con ese codigo.");
        var entity = new RetencionIsd { Codigo = model.Codigo.Value, Descripcion = Clean(model.Descripcion), Valor = model.Valor };
        _context.RetencionIsd.Add(entity);
        await _context.SaveChangesAsync();
        return Ok(entity);
    }

    [HttpPut("isd/{codigo:int}")]
    public async Task<IActionResult> UpdateIsd(int codigo, [FromBody] RetencionDecimalDto model)
    {
        var entity = await _context.RetencionIsd.FindAsync(codigo);
        if (entity is null) return NotFound();
        entity.Descripcion = Clean(model.Descripcion);
        entity.Valor = model.Valor;
        await _context.SaveChangesAsync();
        return Ok(entity);
    }

    [HttpDelete("isd/{codigo:int}")]
    public async Task<IActionResult> DeleteIsd(int codigo)
    {
        var entity = await _context.RetencionIsd.FindAsync(codigo);
        if (entity is null) return NotFound();
        _context.RetencionIsd.Remove(entity);
        await _context.SaveChangesAsync();
        return Ok();
    }

    [HttpGet("renta")]
    public async Task<IActionResult> GetRenta([FromQuery] string? search = null)
    {
        var term = Clean(search).ToLower();
        var data = await _context.RetencionRenta
            .AsNoTracking()
            .Where(x => term == "" || x.Codigo.ToLower().Contains(term) || (x.Descripcion ?? "").ToLower().Contains(term))
            .OrderBy(x => x.Codigo)
            .ToListAsync();
        return Ok(data);
    }

    [HttpPost("renta")]
    public async Task<IActionResult> CreateRenta([FromBody] RetencionRentaDto model)
    {
        var codigo = Clean(model.Codigo);
        if (codigo == "" || Clean(model.Descripcion) == "") return BadRequest("Codigo y descripcion son obligatorios.");
        if (await _context.RetencionRenta.AnyAsync(x => x.Codigo == codigo)) return BadRequest("Ya existe una retencion de renta con ese codigo.");
        var entity = new RetencionRenta
        {
            Codigo = codigo,
            Descripcion = Clean(model.Descripcion),
            Valor = model.Valor,
            ValorFinal = model.ValorFinal,
            CodigoFormulario103 = Clean(model.CodigoFormulario103),
            InformacionExtra = Clean(model.InformacionExtra),
            Estado = model.Estado ?? true
        };
        _context.RetencionRenta.Add(entity);
        await _context.SaveChangesAsync();
        return Ok(entity);
    }

    [HttpPut("renta/{codigo}")]
    public async Task<IActionResult> UpdateRenta(string codigo, [FromBody] RetencionRentaDto model)
    {
        var entity = await _context.RetencionRenta.FindAsync(codigo);
        if (entity is null) return NotFound();
        entity.Descripcion = Clean(model.Descripcion);
        entity.Valor = model.Valor;
        entity.ValorFinal = model.ValorFinal;
        entity.CodigoFormulario103 = Clean(model.CodigoFormulario103);
        entity.InformacionExtra = Clean(model.InformacionExtra);
        entity.Estado = model.Estado ?? entity.Estado ?? true;
        await _context.SaveChangesAsync();
        return Ok(entity);
    }

    [HttpDelete("renta/{codigo}")]
    public async Task<IActionResult> DeleteRenta(string codigo)
    {
        var entity = await _context.RetencionRenta.FindAsync(codigo);
        if (entity is null) return NotFound();
        entity.Estado = false;
        await _context.SaveChangesAsync();
        return Ok();
    }

    private static string Clean(string? value) => (value ?? string.Empty).Trim();
}

public sealed class RetencionDecimalDto
{
    public int? Codigo { get; set; }
    public string? Descripcion { get; set; }
    public decimal? Valor { get; set; }
}

public sealed class RetencionRentaDto
{
    public string? Codigo { get; set; }
    public string? Descripcion { get; set; }
    public decimal? Valor { get; set; }
    public decimal? ValorFinal { get; set; }
    public string? CodigoFormulario103 { get; set; }
    public string? InformacionExtra { get; set; }
    public bool? Estado { get; set; }
}
