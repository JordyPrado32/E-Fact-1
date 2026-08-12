using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Simetric.Components.Helpers;
using Simetric.Data;
using Simetric.Models;
using Simetric.Services;

namespace Simetric.Controllers;

[ApiController]
[Route("api/usuarios-admin")]
public class UsuariosAdminController : ControllerBase
{
    private readonly IDbContextFactory<AppDbContext> _dbFactory;
    private readonly UserService _userService;

    public UsuariosAdminController(IDbContextFactory<AppDbContext> dbFactory, UserService userService)
    {
        _dbFactory = dbFactory;
        _userService = userService;
    }

    [HttpGet]
    public async Task<IActionResult> Get([FromQuery] string? search = null, [FromQuery] bool incluirInactivos = false)
    {
        await using var db = await _dbFactory.CreateDbContextAsync();
        var term = Clean(search).ToLower();

        var query = db.Usuarios
            .AsNoTracking()
            .Include(x => x.IdTipoUsuarioNavigation)
            .Include(x => x.IdTipoIdentificacionNavigation)
            .AsQueryable();

        if (!incluirInactivos) query = query.Where(x => x.Estado == true);
        if (term != "")
        {
            query = query.Where(x =>
                (x.Nombres + " " + x.Apellidos).ToLower().Contains(term) ||
                x.Email.ToLower().Contains(term) ||
                (x.Identificacion ?? "").ToLower().Contains(term) ||
                (x.IdTipoUsuarioNavigation != null && x.IdTipoUsuarioNavigation.NombreTipo.ToLower().Contains(term)));
        }

        var data = await query
            .OrderBy(x => x.Nombres)
            .Take(300)
            .Select(x => new
            {
                id = x.IdUsuario,
                nombres = x.Nombres,
                apellidos = x.Apellidos,
                nombreEmpresa = x.NombreEmpresa,
                email = x.Email,
                celular = x.Celular,
                identificacion = x.Identificacion,
                tipoCliente = x.TipoCliente,
                idTipoIdentificacion = x.IdTipoIdentificacion,
                tipoIdentificacion = x.IdTipoIdentificacionNavigation == null ? null : x.IdTipoIdentificacionNavigation.Descripcion,
                idTipoUsuario = x.IdTipoUsuario,
                rol = x.IdTipoUsuarioNavigation == null ? null : x.IdTipoUsuarioNavigation.NombreTipo,
                avatarUrl = x.AvatarUrl,
                estado = x.Estado,
                cuentaBloqueada = x.CuentaBloqueada,
                fechaCreacion = x.FechaCreacion,
                ultimoAcceso = x.UltimoAcceso
            })
            .ToListAsync();

        return Ok(data);
    }

    [HttpPost]
    public async Task<IActionResult> Create([FromBody] UsuarioAdminDto model)
    {
        var validation = Validate(model, true);
        if (validation is not null) return BadRequest(validation);

        var usuario = ToUsuario(model);
        usuario.PasswordHash = Simetric.Components.Helpers.SecurityHelper.HashPassword(Clean(model.Password));
        usuario.ClaveTemporal = model.ClaveTemporal ?? true;
        await _userService.SaveUsuarioAsync(usuario);
        return Ok(new { id = usuario.IdUsuario });
    }

    [HttpPut("{id:int}")]
    public async Task<IActionResult> Update(int id, [FromBody] UsuarioAdminDto model)
    {
        var validation = Validate(model, false);
        if (validation is not null) return BadRequest(validation);

        var usuario = ToUsuario(model);
        usuario.IdUsuario = id;
        if (!string.IsNullOrWhiteSpace(model.Password))
        {
            usuario.PasswordHash = Simetric.Components.Helpers.SecurityHelper.HashPassword(Clean(model.Password));
            usuario.ClaveTemporal = model.ClaveTemporal ?? true;
        }

        await _userService.SaveUsuarioAsync(usuario);
        return Ok(new { id });
    }

    [HttpPut("{id:int}/desbloquear")]
    public async Task<IActionResult> Unlock(int id)
    {
        await using var db = await _dbFactory.CreateDbContextAsync();
        var usuario = await db.Usuarios.FindAsync(id);
        if (usuario is null) return NotFound();
        usuario.CuentaBloqueada = false;
        usuario.IntentosFallidos = 0;
        usuario.FechaDesbloqueo = null;
        await db.SaveChangesAsync();
        return Ok();
    }

    [HttpDelete("{id:int}")]
    public async Task<IActionResult> Delete(int id)
    {
        var deleted = await _userService.SoftDeleteAsync(id);
        return deleted ? Ok() : NotFound();
    }

    private static Usuario ToUsuario(UsuarioAdminDto model) => new()
    {
        Nombres = Clean(model.Nombres),
        Apellidos = Clean(model.Apellidos),
        NombreEmpresa = Clean(model.NombreEmpresa),
        Email = Clean(model.Email),
        DireccionEmpresa = Clean(model.DireccionEmpresa),
        Celular = Clean(model.Celular),
        Identificacion = Clean(model.Identificacion),
        TipoCliente = model.TipoCliente,
        IdTipoIdentificacion = model.IdTipoIdentificacion,
        IdTipoUsuario = model.IdTipoUsuario,
        AvatarUrl = Clean(model.AvatarUrl),
        Estado = model.Estado ?? true,
        CuentaBloqueada = model.CuentaBloqueada ?? false,
        estadoAsociado = model.EstadoAsociado ?? true
    };

    private static string? Validate(UsuarioAdminDto model, bool requirePassword)
    {
        if (Clean(model.Nombres) == "") return "Los nombres son obligatorios.";
        if (Clean(model.Apellidos) == "") return "Los apellidos son obligatorios.";
        if (Clean(model.Email) == "") return "El correo es obligatorio.";
        if (model.IdTipoUsuario is null or <= 0) return "El rol es obligatorio.";
        if (requirePassword && Clean(model.Password) == "") return "La clave inicial es obligatoria.";
        return null;
    }

    private static string Clean(string? value) => (value ?? string.Empty).Trim();
}

public sealed class UsuarioAdminDto
{
    public string? Nombres { get; set; }
    public string? Apellidos { get; set; }
    public string? NombreEmpresa { get; set; }
    public string? Email { get; set; }
    public string? DireccionEmpresa { get; set; }
    public string? Celular { get; set; }
    public string? Identificacion { get; set; }
    public int? TipoCliente { get; set; }
    public int? IdTipoIdentificacion { get; set; }
    public int? IdTipoUsuario { get; set; }
    public string? AvatarUrl { get; set; }
    public string? Password { get; set; }
    public bool? ClaveTemporal { get; set; }
    public bool? Estado { get; set; }
    public bool? CuentaBloqueada { get; set; }
    public bool? EstadoAsociado { get; set; }
}
