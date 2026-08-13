using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Simetric.Components.Helpers;
using Simetric.Data;
using Simetric.ViewModels;

namespace Simetric.Controllers;

[ApiController]
[Route("api/perfil")]
public class PerfilController : ControllerBase
{
    private readonly AppDbContext _context;
    private readonly IWebHostEnvironment _environment;

    public PerfilController(AppDbContext context, IWebHostEnvironment environment)
    {
        _context = context;
        _environment = environment;
    }

    [HttpGet]
    public async Task<IActionResult> Get([FromQuery] int? idUsuario)
    {
        if (idUsuario is null or <= 0)
            return BadRequest("Id de usuario requerido.");

        var usuario = await _context.Usuarios.AsNoTracking().FirstOrDefaultAsync(u => u.IdUsuario == idUsuario.Value);
        if (usuario is null)
            return NotFound("Usuario no encontrado.");

        var tiposCliente = await _context.Tipoclientes
            .AsNoTracking()
            .OrderBy(t => t.TclCodigo)
            .Select(t => new { tclCodigo = t.TclCodigo, descripcion = t.TclCodigo == 1 ? "Persona Natural" : t.TclCodigo == 2 ? "Persona Jurídica" : t.TclDescripcion })
            .ToListAsync();

        var tiposIdentificacion = await _context.TipoIdentificacion
            .AsNoTracking()
            .Where(t => t.Estado != false)
            .OrderBy(t => t.IdTipoIdentificacion)
            .Select(t => new { idTipoIdentificacion = t.IdTipoIdentificacion, nombreTipo = t.NombreTipo, descripcion = t.Descripcion })
            .ToListAsync();

        return Ok(new
        {
            perfil = new
            {
                idUsuario = usuario.IdUsuario,
                nombres = usuario.Nombres,
                apellidos = usuario.Apellidos,
                nombreEmpresa = usuario.NombreEmpresa,
                email = usuario.Email,
                avatarUrl = usuario.AvatarUrl,
                identificacion = usuario.Identificacion,
                tipoCliente = usuario.TipoCliente,
                idTipoIdentificacion = usuario.IdTipoIdentificacion,
                direccionEmpresa = usuario.DireccionEmpresa,
                celular = usuario.Celular
            },
            tiposCliente,
            tiposIdentificacion
        });
    }

    [HttpPut("{id:int}")]
    public async Task<IActionResult> Update(int id, [FromQuery] int? idUsuario, [FromBody] PerfilUpdateDto model)
    {
        if (idUsuario is null or <= 0 || id != idUsuario.Value)
            return BadRequest("Id de usuario inválido.");

        var usuario = await _context.Usuarios.FirstOrDefaultAsync(u => u.IdUsuario == idUsuario.Value);
        if (usuario is null)
            return NotFound("Usuario no encontrado.");

        var perfil = new PerfilViewModel
        {
            Nombres = model.Nombres ?? string.Empty,
            Apellidos = model.Apellidos ?? string.Empty,
            NombreEmpresa = model.NombreEmpresa,
            Email = usuario.Email,
            AvatarUrl = model.AvatarUrl,
            Identificacion = model.Identificacion,
            TipoCliente = model.TipoCliente,
            IdTipoIdentificacion = model.IdTipoIdentificacion,
            DireccionEmpresa = model.DireccionEmpresa,
            Celular = model.Celular,
            NuevaPassword = model.NuevaPassword,
            ConfirmarPassword = model.ConfirmarPassword
        };

        var validationContext = new System.ComponentModel.DataAnnotations.ValidationContext(perfil);
        var results = new List<System.ComponentModel.DataAnnotations.ValidationResult>();
        if (!System.ComponentModel.DataAnnotations.Validator.TryValidateObject(perfil, validationContext, results, true))
            return BadRequest(string.Join(" ", results.Select(r => r.ErrorMessage).Where(m => !string.IsNullOrWhiteSpace(m))));

        usuario.Nombres = perfil.TipoCliente == 2 ? perfil.NombreEmpresa! : NormalizarTexto(perfil.Nombres);
        usuario.Apellidos = perfil.TipoCliente == 2 ? string.Empty : NormalizarTexto(perfil.Apellidos);
        usuario.NombreEmpresa = perfil.TipoCliente == 2 ? perfil.NombreEmpresa?.Trim() : null;
        usuario.DireccionEmpresa = perfil.DireccionEmpresa?.Trim();
        usuario.Celular = perfil.Celular?.Trim();
        usuario.Identificacion = perfil.Identificacion?.Trim();
        usuario.IdTipoIdentificacion = perfil.IdTipoIdentificacion;
        usuario.TipoCliente = perfil.TipoCliente;
        usuario.AvatarUrl = perfil.AvatarUrl;

        if (!string.IsNullOrWhiteSpace(perfil.NuevaPassword))
        {
            usuario.PasswordHash = SecurityHelper.HashPassword(perfil.NuevaPassword);
            usuario.ClaveTemporal = false;
        }

        await _context.SaveChangesAsync();
        return Ok();
    }

    [HttpPost("{id:int}/avatar")]
    [RequestSizeLimit(2 * 1024 * 1024)]
    public async Task<IActionResult> UploadAvatar(int id, [FromQuery] int? idUsuario, IFormFile? file)
    {
        if (idUsuario is null or <= 0 || id != idUsuario.Value)
            return BadRequest("Id de usuario inválido.");

        if (file is null || file.Length == 0)
            return BadRequest("Archivo requerido.");

        if (file.Length > 2 * 1024 * 1024)
            return BadRequest("La imagen no puede superar 2 MB.");

        var extension = Path.GetExtension(file.FileName).ToLowerInvariant();
        var allowedExtensions = new[] { ".jpg", ".jpeg", ".png" };
        if (!allowedExtensions.Contains(extension))
            return BadRequest("Formato no permitido. Usa JPG, JPEG o PNG.");

        var usuario = await _context.Usuarios.FirstOrDefaultAsync(u => u.IdUsuario == idUsuario.Value);
        if (usuario is null)
            return NotFound("Usuario no encontrado.");

        var avatarsPath = Path.Combine(_environment.WebRootPath, "images", "Avatars", "uploads");
        Directory.CreateDirectory(avatarsPath);

        if (!string.IsNullOrWhiteSpace(usuario.AvatarUrl) && usuario.AvatarUrl.Contains("images/Avatars/uploads/", StringComparison.OrdinalIgnoreCase))
        {
            var oldPath = Path.Combine(_environment.WebRootPath, usuario.AvatarUrl.TrimStart('/').Replace('/', Path.DirectorySeparatorChar));
            if (System.IO.File.Exists(oldPath))
                System.IO.File.Delete(oldPath);
        }

        var fileName = $"avatar_user_{idUsuario.Value}_{Guid.NewGuid()}{extension}";
        var fullPath = Path.Combine(avatarsPath, fileName);

        await using (var stream = System.IO.File.Create(fullPath))
        {
            await file.CopyToAsync(stream);
        }

        var avatarUrl = $"images/Avatars/uploads/{fileName}";
        usuario.AvatarUrl = avatarUrl;
        await _context.SaveChangesAsync();

        return Ok(new { avatarUrl });
    }

    private static string NormalizarTexto(string? texto) =>
        string.Join(" ", (texto ?? string.Empty).Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries));

    public sealed class PerfilUpdateDto
    {
        public string? Nombres { get; set; }
        public string? Apellidos { get; set; }
        public string? NombreEmpresa { get; set; }
        public string? AvatarUrl { get; set; }
        public string? Identificacion { get; set; }
        public int? TipoCliente { get; set; }
        public int? IdTipoIdentificacion { get; set; }
        public string? DireccionEmpresa { get; set; }
        public string? Celular { get; set; }
        public string? NuevaPassword { get; set; }
        public string? ConfirmarPassword { get; set; }
    }
}
