using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Simetric.Data;
using Simetric.DTOs.EContax;
using Simetric.Services;
using Simetric.Services.EContax;

namespace Simetric.Controllers.EContax;

[ApiController]
[Route("api/e-contax/clientes")]
public sealed class EContaxClientesController : ControllerBase
{
    private readonly AppDbContext _context;
    private readonly EContaxCatalogService _catalogService;
    private readonly UbicacionEcuadorCatalogService _ubicacionEcuadorCatalogService;
    private readonly CedulaLookupService _cedulaLookupService;

    public EContaxClientesController(
        AppDbContext context,
        EContaxCatalogService catalogService,
        UbicacionEcuadorCatalogService ubicacionEcuadorCatalogService,
        CedulaLookupService cedulaLookupService)
    {
        _context = context;
        _catalogService = catalogService;
        _ubicacionEcuadorCatalogService = ubicacionEcuadorCatalogService;
        _cedulaLookupService = cedulaLookupService;
    }

    [HttpGet]
    public async Task<IActionResult> GetAll([FromQuery] int userId, [FromQuery] bool incluirInactivos = false)
    {
        if (userId <= 0)
            return Unauthorized("Sesion no valida.");

        try
        {
            return Ok(await _catalogService.GetClientesAsync(userId, incluirInactivos));
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(ex.Message);
        }
    }

    [HttpGet("{id:int}")]
    public async Task<IActionResult> GetById(int id, [FromQuery] int userId)
    {
        if (userId <= 0)
            return Unauthorized("Sesion no valida.");

        try
        {
            var cliente = await _catalogService.GetClienteAsync(userId, id);
            return cliente is null ? NotFound() : Ok(cliente);
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(ex.Message);
        }
    }

    [HttpGet("consulta-identificacion")]
    public async Task<IActionResult> ConsultarIdentificacion([FromQuery] string identificacion, CancellationToken cancellationToken)
    {
        var resultado = await _cedulaLookupService.ConsultarAsync(identificacion, cancellationToken);
        return resultado.Success ? Ok(resultado) : BadRequest(resultado);
    }

    [HttpPost]
    public async Task<IActionResult> Create([FromQuery] int userId, [FromBody] EContaxClienteUpsertDto dto)
    {
        if (userId <= 0)
            return Unauthorized("Sesion no valida.");

        try
        {
            await ResolverUbicacionEcuadorQuemada(dto);
            var id = await _catalogService.CrearClienteAsync(userId, dto);
            return Ok(new { codcliente = id });
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(ex.Message);
        }
    }

    [HttpPut("{id:int}")]
    public async Task<IActionResult> Update(int id, [FromQuery] int userId, [FromBody] EContaxClienteUpsertDto dto)
    {
        if (userId <= 0)
            return Unauthorized("Sesion no valida.");

        try
        {
            await ResolverUbicacionEcuadorQuemada(dto);
            await _catalogService.ActualizarClienteAsync(userId, id, dto);
            return Ok();
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(ex.Message);
        }
    }

    [HttpPut("{id:int}/desactivar")]
    public async Task<IActionResult> Desactivar(int id, [FromQuery] int userId)
    {
        if (userId <= 0)
            return Unauthorized("Sesion no valida.");

        try
        {
            await _catalogService.SetEstadoClienteAsync(userId, id, false);
            return Ok();
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(ex.Message);
        }
    }

    [HttpPut("{id:int}/activar")]
    public async Task<IActionResult> Activar(int id, [FromQuery] int userId)
    {
        if (userId <= 0)
            return Unauthorized("Sesion no valida.");

        try
        {
            await _catalogService.SetEstadoClienteAsync(userId, id, true);
            return Ok();
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(ex.Message);
        }
    }

    [HttpGet("lookups")]
    public async Task<IActionResult> Lookups()
    {
        await PaisCatalogoService.AsegurarCatalogoAsync(_context);

        var tipos = await _context.Tipoclientes
            .AsNoTracking()
            .OrderBy(t => t.TclCodigo)
            .Select(t => new
            {
                tclCodigo = t.TclCodigo,
                descripcion = t.TclDescripcion
            })
            .ToListAsync();

        var paises = await _context.Paises
            .AsNoTracking()
            .OrderBy(p => p.Descripcion)
            .Select(p => new
            {
                idPais = p.IdPais,
                descripcion = p.Descripcion
            })
            .ToListAsync();

        var identificaciones = await _context.Identificacion
            .AsNoTracking()
            .OrderBy(i => i.IdeSec)
            .Select(i => new
            {
                ideSec = i.IdeSec,
                ideCodigo = i.IdeCodigo,
                ideDescripcion = i.IdeDescripcion
            })
            .ToListAsync();

        return Ok(new { tipos, paises, identificaciones });
    }

    [HttpGet("provincias")]
    public async Task<IActionResult> Provincias([FromQuery] int paisId)
    {
        if (paisId <= 0)
            return BadRequest();

        var provincias = await _context.Provincias
            .AsNoTracking()
            .Where(x => x.IdPais == paisId)
            .OrderBy(x => x.Descripcion)
            .Select(x => new
            {
                idProvincia = x.IdProvincia,
                descripcion = x.Descripcion
            })
            .ToListAsync();

        return Ok(provincias);
    }

    [HttpGet("ciudades")]
    public async Task<IActionResult> Ciudades([FromQuery] int provinciaId)
    {
        if (provinciaId <= 0)
            return BadRequest();

        var ciudades = await _context.Ciudades
            .AsNoTracking()
            .Where(x => x.IdProvincia == provinciaId)
            .OrderBy(x => x.Descripcion)
            .Select(x => new
            {
                idCiudad = x.IdCiudad,
                descripcion = x.Descripcion
            })
            .ToListAsync();

        return Ok(ciudades);
    }

    [HttpGet("ubicacion-ecuador")]
    public async Task<IActionResult> UbicacionEcuador()
    {
        var idPaisEcuador = await _context.Paises
            .AsNoTracking()
            .Where(p => p.Descripcion != null && p.Descripcion.Trim().ToUpper() == "ECUADOR")
            .Select(p => p.IdPais)
            .FirstOrDefaultAsync();

        if (idPaisEcuador <= 0)
            return NotFound("No se encontro el catalogo de Ecuador.");

        await _ubicacionEcuadorCatalogService.EnsureCatalogoAsync();

        var provincias = await _context.Provincias
            .AsNoTracking()
            .Where(x => x.IdPais == idPaisEcuador)
            .OrderBy(x => x.Descripcion)
            .Select(x => new
            {
                idProvincia = x.IdProvincia,
                descripcion = x.Descripcion,
                idPais = x.IdPais
            })
            .ToListAsync();

        var provinciaIds = provincias.Select(x => x.idProvincia).ToList();

        var ciudades = await _context.Ciudades
            .AsNoTracking()
            .Where(x => x.IdProvincia.HasValue && provinciaIds.Contains(x.IdProvincia.Value))
            .OrderBy(x => x.Descripcion)
            .Select(x => new
            {
                idCiudad = x.IdCiudad,
                descripcion = x.Descripcion,
                idProvincia = x.IdProvincia
            })
            .ToListAsync();

        return Ok(new
        {
            idPais = idPaisEcuador,
            provincias,
            ciudades
        });
    }

    private async Task ResolverUbicacionEcuadorQuemada(EContaxClienteUpsertDto dto)
    {
        if (dto.Provincia >= 0 && dto.Ciudad >= 0)
            return;

        await _ubicacionEcuadorCatalogService.EnsureCatalogoAsync();

        if (UbicacionEcuadorCatalogService.TryGetProvinciaCatalogo(dto.Provincia, out var provinciaCatalogo, out var provinciaIndex))
        {
            var provinciasPais = await _context.Provincias
                .Where(x => x.IdPais == dto.Pais)
                .ToListAsync();
            var provinciaReal = provinciasPais.FirstOrDefault(x =>
                UbicacionEcuadorCatalogService.NormalizarClaveUbicacion(x.Descripcion) ==
                UbicacionEcuadorCatalogService.NormalizarClaveUbicacion(provinciaCatalogo.Nombre));

            if (provinciaReal is not null)
                dto.Provincia = provinciaReal.IdProvincia;
        }

        if (UbicacionEcuadorCatalogService.TryGetCiudadCatalogo(dto.Ciudad, out var ciudadCatalogo, out var ciudadProvinciaIndex, out _) &&
            ciudadProvinciaIndex == provinciaIndex &&
            dto.Provincia.HasValue &&
            dto.Provincia > 0)
        {
            var ciudadesProvincia = await _context.Ciudades
                .Where(x => x.IdProvincia == dto.Provincia.Value)
                .ToListAsync();
            var ciudadReal = ciudadesProvincia.FirstOrDefault(x =>
                UbicacionEcuadorCatalogService.NormalizarClaveUbicacion(x.Descripcion) ==
                UbicacionEcuadorCatalogService.NormalizarClaveUbicacion(ciudadCatalogo));

            if (ciudadReal is not null)
                dto.Ciudad = ciudadReal.IdCiudad;
        }
    }
}
