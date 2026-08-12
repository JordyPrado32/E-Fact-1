using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Simetric.Data;
using Simetric.Models;

namespace Simetric.Controllers;

[ApiController]
[Route("api/formas-pago")]
public class FormasPagoController : ControllerBase
{
    private readonly AppDbContext _context;

    public FormasPagoController(AppDbContext context)
    {
        _context = context;
    }

    [HttpGet]
    public async Task<IActionResult> Get([FromQuery] string? search = null)
    {
        var term = Clean(search).ToLower();
        var data = await _context.FormasPago
            .AsNoTracking()
            .Where(x => term == "" || x.Codigo.ToLower().Contains(term) || (x.Descripcion ?? "").ToLower().Contains(term) || (x.DescripcionSri ?? "").ToLower().Contains(term))
            .OrderBy(x => x.Codigo)
            .Select(x => new { id = x.Id, codigo = x.Codigo, descripcion = x.Descripcion, descripcionSri = x.DescripcionSri, tipoVenta = x.TipoVenta, tipoCompra = x.TipoCompra, estado = x.Estado })
            .ToListAsync();
        return Ok(data);
    }

    [HttpPost]
    public async Task<IActionResult> Create([FromBody] FormaPagoDto model)
    {
        var codigo = Clean(model.Codigo);
        if (codigo == "" || Clean(model.Descripcion) == "") return BadRequest("Codigo y descripcion son obligatorios.");
        if (await _context.FormasPago.AnyAsync(x => x.Codigo == codigo)) return BadRequest("Ya existe una forma de pago con ese codigo.");

        var entity = new FormasPago
        {
            Codigo = codigo,
            Descripcion = Clean(model.Descripcion),
            DescripcionSri = Clean(model.DescripcionSri),
            TipoVenta = model.TipoVenta,
            TipoCompra = model.TipoCompra,
            Estado = model.Estado ?? true
        };
        _context.FormasPago.Add(entity);
        await _context.SaveChangesAsync();
        return Ok(entity);
    }

    [HttpPut("{id:int}")]
    public async Task<IActionResult> Update(int id, [FromBody] FormaPagoDto model)
    {
        var entity = await _context.FormasPago.FindAsync(id);
        if (entity is null) return NotFound();
        var codigo = Clean(model.Codigo);
        if (codigo == "" || Clean(model.Descripcion) == "") return BadRequest("Codigo y descripcion son obligatorios.");
        if (await _context.FormasPago.AnyAsync(x => x.Id != id && x.Codigo == codigo)) return BadRequest("Ya existe una forma de pago con ese codigo.");

        entity.Codigo = codigo;
        entity.Descripcion = Clean(model.Descripcion);
        entity.DescripcionSri = Clean(model.DescripcionSri);
        entity.TipoVenta = model.TipoVenta;
        entity.TipoCompra = model.TipoCompra;
        entity.Estado = model.Estado ?? entity.Estado ?? true;
        await _context.SaveChangesAsync();
        return Ok(entity);
    }

    [HttpDelete("{id:int}")]
    public async Task<IActionResult> Delete(int id)
    {
        var entity = await _context.FormasPago.FindAsync(id);
        if (entity is null) return NotFound();
        entity.Estado = false;
        await _context.SaveChangesAsync();
        return Ok();
    }

    [HttpGet("tipos-documento")]
    public async Task<IActionResult> GetTiposDocumento([FromQuery] string? search = null)
    {
        var term = Clean(search).ToLower();
        var data = await _context.TipoDocumento
            .AsNoTracking()
            .Where(x => term == "" || (x.Codigo ?? "").ToLower().Contains(term) || (x.Descripcion ?? "").ToLower().Contains(term))
            .OrderBy(x => x.Codigo)
            .Select(x => new { id = x.Sec, codigo = x.Codigo, descripcion = x.Descripcion })
            .ToListAsync();
        return Ok(data);
    }

    [HttpPost("tipos-documento")]
    public async Task<IActionResult> CreateTipoDocumento([FromBody] TipoDocumentoDto model)
    {
        var codigo = Clean(model.Codigo);
        var descripcion = Clean(model.Descripcion);
        if (codigo == "" || descripcion == "") return BadRequest("Codigo y descripcion son obligatorios.");
        if (await _context.TipoDocumento.AnyAsync(x => x.Codigo == codigo)) return BadRequest("Ya existe un tipo de documento con ese codigo.");

        var entity = new TipoDocumento { Codigo = codigo, Descripcion = descripcion };
        _context.TipoDocumento.Add(entity);
        await _context.SaveChangesAsync();
        return Ok(entity);
    }

    [HttpPut("tipos-documento/{id:int}")]
    public async Task<IActionResult> UpdateTipoDocumento(int id, [FromBody] TipoDocumentoDto model)
    {
        var entity = await _context.TipoDocumento.FindAsync(id);
        if (entity is null) return NotFound();
        var codigo = Clean(model.Codigo);
        var descripcion = Clean(model.Descripcion);
        if (codigo == "" || descripcion == "") return BadRequest("Codigo y descripcion son obligatorios.");
        if (await _context.TipoDocumento.AnyAsync(x => x.Sec != id && x.Codigo == codigo)) return BadRequest("Ya existe un tipo de documento con ese codigo.");

        entity.Codigo = codigo;
        entity.Descripcion = descripcion;
        await _context.SaveChangesAsync();
        return Ok(entity);
    }

    [HttpDelete("tipos-documento/{id:int}")]
    public async Task<IActionResult> DeleteTipoDocumento(int id)
    {
        var entity = await _context.TipoDocumento.FindAsync(id);
        if (entity is null) return NotFound();
        _context.TipoDocumento.Remove(entity);
        await _context.SaveChangesAsync();
        return Ok();
    }

    private static string Clean(string? value) => (value ?? string.Empty).Trim();
}

public sealed class FormaPagoDto
{
    public string? Codigo { get; set; }
    public string? Descripcion { get; set; }
    public string? DescripcionSri { get; set; }
    public bool? TipoVenta { get; set; }
    public bool? TipoCompra { get; set; }
    public bool? Estado { get; set; }
}

public sealed class TipoDocumentoDto
{
    public string? Codigo { get; set; }
    public string? Descripcion { get; set; }
}
