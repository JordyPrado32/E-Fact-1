using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Simetric.Models;
using Simetric.Services;

namespace Simetric.Controllers;

[Authorize]
[ApiController]
[Route("api/centro-normativo")]
[Route("api/configuracion/centro-normativo")]
public class CentroNormativoController : ControllerBase
{
    private readonly NormativaLegalService _normativaService;

    public CentroNormativoController(NormativaLegalService normativaService)
    {
        _normativaService = normativaService;
    }

    [HttpGet]
    public async Task<IActionResult> GetNormativas([FromQuery] string? search = null)
    {
        var normativas = await _normativaService.ObtenerPublicadasAsync();

        if (!string.IsNullOrWhiteSpace(search))
        {
            var term = search.Trim();
            normativas = normativas
                .Where(x =>
                    Contains(x.Codigo, term) ||
                    Contains(x.Titulo, term) ||
                    Contains(x.Categoria, term) ||
                    Contains(x.Resumen, term) ||
                    Contains(x.Contenido, term))
                .ToList();
        }

        return Ok(new
        {
            total = normativas.Count,
            categorias = normativas
                .Select(x => x.Categoria)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .OrderBy(x => x)
                .ToList(),
            ultimaVerificacion = ObtenerUltimaVerificacion(normativas),
            items = normativas.Select(ToDto)
        });
    }

    [HttpGet("{id:int}")]
    public async Task<IActionResult> GetNormativa(int id)
    {
        var normativa = (await _normativaService.ObtenerPublicadasAsync())
            .FirstOrDefault(x => x.Id == id);

        return normativa is null ? NotFound() : Ok(ToDto(normativa));
    }

    private static object ToDto(NormativaLegal normativa) => new
    {
        normativa.Id,
        normativa.Codigo,
        normativa.Titulo,
        normativa.Categoria,
        normativa.Resumen,
        normativa.Contenido,
        normativa.UrlOficial,
        normativa.EstadoNorma,
        normativa.FechaPublicacion,
        normativa.FechaVigencia,
        normativa.FechaUltimaVerificacion
    };

    private static string ObtenerUltimaVerificacion(IEnumerable<NormativaLegal> normativas)
    {
        var fecha = normativas
            .Where(x => x.FechaUltimaVerificacion.HasValue)
            .Select(x => x.FechaUltimaVerificacion!.Value)
            .DefaultIfEmpty()
            .Max();

        return fecha == default
            ? "Por verificar"
            : fecha.ToString("MMM yyyy", new System.Globalization.CultureInfo("es-EC"));
    }

    private static bool Contains(string? source, string term) =>
        !string.IsNullOrWhiteSpace(source) &&
        source.Contains(term, StringComparison.OrdinalIgnoreCase);
}
