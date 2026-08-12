using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Simetric.Data;
using Simetric.Models;

namespace Simetric.Controllers;

[ApiController]
[Route("api/impuestos")]
public class ImpuestosController : ControllerBase
{
    private readonly AppDbContext _context;

    public ImpuestosController(AppDbContext context)
    {
        _context = context;
    }

    [HttpGet("codigos")]
    public async Task<IActionResult> GetCodigos([FromQuery] string? search = null)
    {
        var term = Clean(search).ToLower();
        var data = await _context.Codigoimpuestos
            .AsNoTracking()
            .Where(x => term == "" || x.Codigo.ToLower().Contains(term) || (x.Descripcion ?? "").ToLower().Contains(term))
            .OrderBy(x => x.Codigo)
            .Select(x => new { codigo = x.Codigo, descripcion = x.Descripcion, estado = x.Estado })
            .ToListAsync();
        return Ok(data);
    }

    [HttpPost("codigos")]
    public async Task<IActionResult> CreateCodigo([FromBody] CodigoImpuestoDto model)
    {
        var codigo = Clean(model.Codigo);
        var descripcion = Clean(model.Descripcion);
        if (codigo == "" || descripcion == "") return BadRequest("Codigo y descripcion son obligatorios.");
        if (await _context.Codigoimpuestos.AnyAsync(x => x.Codigo == codigo)) return BadRequest("Ya existe un codigo de impuesto con ese codigo.");

        var entity = new Codigosimpuesto { Codigo = codigo, Descripcion = descripcion, Estado = model.Estado ?? "A" };
        _context.Codigoimpuestos.Add(entity);
        await _context.SaveChangesAsync();
        return Ok(entity);
    }

    [HttpPut("codigos/{codigo}")]
    public async Task<IActionResult> UpdateCodigo(string codigo, [FromBody] CodigoImpuestoDto model)
    {
        var entity = await _context.Codigoimpuestos.FindAsync(codigo);
        if (entity is null) return NotFound();

        var descripcion = Clean(model.Descripcion);
        if (descripcion == "") return BadRequest("La descripcion es obligatoria.");
        entity.Descripcion = descripcion;
        entity.Estado = model.Estado ?? entity.Estado ?? "A";
        await _context.SaveChangesAsync();
        return Ok(entity);
    }

    [HttpDelete("codigos/{codigo}")]
    public async Task<IActionResult> DeleteCodigo(string codigo)
    {
        var entity = await _context.Codigoimpuestos.FindAsync(codigo);
        if (entity is null) return NotFound();
        entity.Estado = "I";
        await _context.SaveChangesAsync();
        return Ok();
    }

    [HttpGet("iva")]
    public async Task<IActionResult> GetIva([FromQuery] string? search = null)
    {
        var term = Clean(search).ToLower();
        var data = await _context.Porcentajeivas
            .AsNoTracking()
            .Where(x => term == "" || x.Codigo.ToLower().Contains(term) || (x.Descripcion ?? "").ToLower().Contains(term) || (x.Valor ?? "").ToLower().Contains(term))
            .OrderBy(x => x.Codigo)
            .Select(x => new { codigo = x.Codigo, descripcion = x.Descripcion, valor = x.Valor, valorCalculo = x.ValorCalculo, estado = x.Estado })
            .ToListAsync();
        return Ok(data);
    }

    [HttpPost("iva")]
    public async Task<IActionResult> CreateIva([FromBody] PorcentajeIvaDto model)
    {
        var codigo = Clean(model.Codigo);
        var descripcion = Clean(model.Descripcion);
        var valor = Clean(model.Valor);
        if (codigo == "" || descripcion == "" || valor == "") return BadRequest("Codigo, descripcion y valor son obligatorios.");
        if (await _context.Porcentajeivas.AnyAsync(x => x.Codigo == codigo)) return BadRequest("Ya existe una tarifa IVA con ese codigo.");

        var entity = new Porcentajeiva { Codigo = codigo, Descripcion = descripcion, Valor = valor, ValorCalculo = model.ValorCalculo, Estado = model.Estado ?? "A" };
        _context.Porcentajeivas.Add(entity);
        await _context.SaveChangesAsync();
        return Ok(entity);
    }

    [HttpPut("iva/{codigo}")]
    public async Task<IActionResult> UpdateIva(string codigo, [FromBody] PorcentajeIvaDto model)
    {
        var entity = await _context.Porcentajeivas.FindAsync(codigo);
        if (entity is null) return NotFound();

        entity.Descripcion = Clean(model.Descripcion);
        entity.Valor = Clean(model.Valor);
        entity.ValorCalculo = model.ValorCalculo;
        entity.Estado = model.Estado ?? entity.Estado ?? "A";
        await _context.SaveChangesAsync();
        return Ok(entity);
    }

    [HttpDelete("iva/{codigo}")]
    public async Task<IActionResult> DeleteIva(string codigo)
    {
        var entity = await _context.Porcentajeivas.FindAsync(codigo);
        if (entity is null) return NotFound();
        entity.Estado = "I";
        await _context.SaveChangesAsync();
        return Ok();
    }

    private static string Clean(string? value) => (value ?? string.Empty).Trim();
}

public sealed class CodigoImpuestoDto
{
    public string? Codigo { get; set; }
    public string? Descripcion { get; set; }
    public string? Estado { get; set; }
}

public sealed class PorcentajeIvaDto
{
    public string? Codigo { get; set; }
    public string? Descripcion { get; set; }
    public string? Valor { get; set; }
    public decimal? ValorCalculo { get; set; }
    public string? Estado { get; set; }
}
