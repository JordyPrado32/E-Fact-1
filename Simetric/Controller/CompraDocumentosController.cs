using System.Text.Json;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Simetric.Data;
using Simetric.DTOs;

namespace Simetric.Controllers;

[ApiController]
[Route("api/documentos")]
public class CompraDocumentosController : UsuarioApiControllerBase
{
    private static readonly JsonSerializerOptions HistorialJsonOptions = new(JsonSerializerDefaults.Web);
    private readonly IDbContextFactory<AppDbContext> _dbFactory;

    public CompraDocumentosController(IDbContextFactory<AppDbContext> dbFactory)
    {
        _dbFactory = dbFactory;
    }

    [HttpGet("compra")]
    public async Task<IActionResult> GetCompra([FromQuery] int idUsuario)
    {
        idUsuario = ResolverIdUsuario(idUsuario);
        if (idUsuario <= 0) return Unauthorized();

        await using var context = await _dbFactory.CreateDbContextAsync();
        var usuario = await context.Usuarios
            .AsNoTracking()
            .Where(x => x.IdUsuario == idUsuario)
            .Select(x => new
            {
                x.IdUsuario,
                x.SaldoDocumentos,
                x.FechaUltimaRecargaDocumentos,
                x.HistorialComprasDocumentosJson
            })
            .FirstOrDefaultAsync();

        return usuario is null
            ? NotFound()
            : Ok(new
            {
                usuario.IdUsuario,
                saldoDocumentos = usuario.SaldoDocumentos,
                usuario.FechaUltimaRecargaDocumentos,
                historial = LeerHistorial(usuario.HistorialComprasDocumentosJson)
            });
    }

    [HttpPost("compra")]
    public async Task<IActionResult> CrearCompra([FromQuery] int idUsuario, [FromBody] CompraDocumentosMobileDto model)
    {
        idUsuario = ResolverIdUsuario(idUsuario);
        if (idUsuario <= 0) return Unauthorized();
        if (!model.EsIlimitado && model.Documentos <= 0) return BadRequest("Debe indicar la cantidad de documentos.");
        if (model.MontoTotal <= 0) return BadRequest("Debe indicar el monto total.");

        await using var context = await _dbFactory.CreateDbContextAsync();
        var usuario = await context.Usuarios.FirstOrDefaultAsync(x => x.IdUsuario == idUsuario);
        if (usuario is null) return NotFound();

        var historial = LeerHistorial(usuario.HistorialComprasDocumentosJson);
        var compra = new CompraDocumentosHistorialItem
        {
            Documentos = model.EsIlimitado ? 0 : model.Documentos,
            MontoTotal = model.MontoTotal,
            Estado = "Pendiente",
            Descripcion = string.IsNullOrWhiteSpace(model.Descripcion)
                ? model.EsIlimitado ? "Plan ilimitado E-FACT" : $"Recarga de {model.Documentos} documentos E-FACT"
                : model.Descripcion.Trim(),
            EmailDestino = string.IsNullOrWhiteSpace(model.EmailDestino) ? usuario.Email : model.EmailDestino.Trim(),
            EsIlimitado = model.EsIlimitado,
            EsPermanente = model.EsPermanente
        };

        historial.Insert(0, compra);
        usuario.HistorialComprasDocumentosJson = JsonSerializer.Serialize(historial, HistorialJsonOptions);
        await context.SaveChangesAsync();

        return Ok(compra);
    }

    [HttpGet("recargas")]
    public async Task<IActionResult> GetRecargas([FromQuery] int idUsuario)
    {
        idUsuario = ResolverIdUsuario(idUsuario);
        if (idUsuario <= 0) return Unauthorized();

        await using var context = await _dbFactory.CreateDbContextAsync();
        var historialJson = await context.Usuarios
            .AsNoTracking()
            .Where(x => x.IdUsuario == idUsuario)
            .Select(x => x.HistorialComprasDocumentosJson)
            .FirstOrDefaultAsync();

        return Ok(LeerHistorial(historialJson));
    }

    [HttpGet("paquetes")]
    public IActionResult GetPaquetes() => Ok(new[]
    {
        new { id = "docs-10", descripcion = "10 documentos", documentos = 10, esIlimitado = false },
        new { id = "docs-50", descripcion = "50 documentos", documentos = 50, esIlimitado = false },
        new { id = "docs-100", descripcion = "100 documentos", documentos = 100, esIlimitado = false },
        new { id = "ilimitado", descripcion = "Ilimitados durante 1 ano", documentos = 0, esIlimitado = true }
    });

    private static List<CompraDocumentosHistorialItem> LeerHistorial(string? historialJson)
    {
        if (string.IsNullOrWhiteSpace(historialJson)) return new List<CompraDocumentosHistorialItem>();

        try
        {
            return JsonSerializer.Deserialize<List<CompraDocumentosHistorialItem>>(historialJson, HistorialJsonOptions)
                ?? new List<CompraDocumentosHistorialItem>();
        }
        catch (JsonException)
        {
            return new List<CompraDocumentosHistorialItem>();
        }
    }
}

public sealed class CompraDocumentosMobileDto
{
    public int Documentos { get; set; }
    public decimal MontoTotal { get; set; }
    public string? Descripcion { get; set; }
    public string? EmailDestino { get; set; }
    public bool EsIlimitado { get; set; }
    public bool EsPermanente { get; set; }
}
