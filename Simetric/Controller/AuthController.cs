using System.Security.Claims;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Simetric.Components.Helpers;
using Simetric.Data;
using Simetric.Models;

namespace Simetric.Controllers
{
    [ApiController]
    [Route("api/auth")]
    public class AuthController : ControllerBase
    {
        private readonly IDbContextFactory<AppDbContext> _dbFactory;

        public AuthController(IDbContextFactory<AppDbContext> dbFactory)
        {
            _dbFactory = dbFactory;
        }

        [HttpGet("check")]
        public IActionResult Check()
        {
            Response.Headers.CacheControl = "no-store, no-cache, max-age=0";
            Response.Headers.Pragma = "no-cache";

            if (User.Identity?.IsAuthenticated == true)
            {
                var claimValue = User.FindFirst("IdUsuario")?.Value;
                var idUsuario = int.TryParse(claimValue, out var parsedId) ? parsedId : 0;
                return Ok(new { authenticated = idUsuario > 0, idUsuario });
            }

            return Ok(new { authenticated = false, idUsuario = 0 });
        }

        [HttpPost("login")]
        public async Task<IActionResult> Login([FromBody] LoginCookieRequest request)
        {
            try
            {
                if (request == null ||
                    string.IsNullOrWhiteSpace(request.Username) ||
                    string.IsNullOrWhiteSpace(request.Password))
                {
                    return BadRequest("Usuario y contraseña son obligatorios.");
                }

                using var context = await _dbFactory.CreateDbContextAsync();

                var cleanUsername = request.Username.Trim();
                var normalizedUsername = cleanUsername.ToLower();

                var userInDb = await context.Usuarios
                    .FirstOrDefaultAsync(u =>
                        (!string.IsNullOrWhiteSpace(u.Email) && u.Email.ToLower() == normalizedUsername) ||
                        (!string.IsNullOrWhiteSpace(u.Nombres) && u.Nombres.ToLower() == normalizedUsername));

                if (userInDb == null)
                {
                    return Unauthorized("Usuario no registrado.");
                }

                if (userInDb.CuentaBloqueada == true &&
                    userInDb.FechaDesbloqueo.HasValue &&
                    userInDb.FechaDesbloqueo.Value > DateTime.Now)
                {
                    var faltan = userInDb.FechaDesbloqueo.Value - DateTime.Now;
                    return Unauthorized($"Tu cuenta está restringida. Intenta en {Math.Ceiling(faltan.TotalMinutes)} min.");
                }

                if (userInDb.CuentaBloqueada == true &&
                    userInDb.FechaDesbloqueo.HasValue &&
                    userInDb.FechaDesbloqueo.Value <= DateTime.Now)
                {
                    userInDb.CuentaBloqueada = false;
                    userInDb.IntentosFallidos = 0;
                    await context.SaveChangesAsync();
                }

                var isPasswordValid = SecurityHelper.VerifyPassword(request.Password.Trim(), userInDb.PasswordHash);

                if (!isPasswordValid)
                {
                    userInDb.IntentosFallidos = (userInDb.IntentosFallidos ?? 0) + 1;

                    if (userInDb.IntentosFallidos >= 3)
                    {
                        userInDb.CuentaBloqueada = true;
                        userInDb.FechaDesbloqueo = DateTime.Now.AddMinutes(30);
                        await context.SaveChangesAsync();
                        return Unauthorized("Has superado los 3 intentos. Bloqueo de 30 minutos.");
                    }

                    await context.SaveChangesAsync();
                    return Unauthorized($"Contraseña incorrecta. Intento {userInDb.IntentosFallidos}/3.");
                }

                if (userInDb.Estado != true)
                {
                    if (userInDb.idJefe is > 0 && userInDb.estadoAsociado != true)
                    {
                        return Unauthorized("Tu solicitud de asociado esta pendiente de aprobacion.");
                    }

                    return Unauthorized("Tu cuenta está desactivada por el administrador.");
                }

                var esEmpleadoBackOffice = userInDb.IdTipoUsuario == 7 || userInDb.IdTipoUsuario == 2;

                var politicasAceptadas = await context.Auditorias
                    .AsNoTracking()
                    .AnyAsync(a => a.IdUsuario == userInDb.IdUsuario && a.Accion == "Aceptación de Políticas de Privacidad");

                if (!esEmpleadoBackOffice && !politicasAceptadas)
                {
                    return Ok(new
                    {
                        requierePoliticas = true,
                        idUsuario = userInDb.IdUsuario
                    });
                }

                if (userInDb.ClaveTemporal == true)
                {
                    return Ok(new
                    {
                        requiereCambioClave = true,
                        idUsuario = userInDb.IdUsuario
                    });
                }

                userInDb.IntentosFallidos = 0;
                userInDb.CuentaBloqueada = false;
                await context.SaveChangesAsync();

                var claims = new List<Claim>
                {
                    new(ClaimTypes.Name, userInDb.Nombres ?? "Usuario"),
                    new(ClaimTypes.NameIdentifier, userInDb.IdUsuario.ToString()),
                    new(ClaimTypes.Surname, userInDb.Apellidos ?? ""),
                    new(ClaimTypes.Email, userInDb.Email ?? ""),
                    new("IdUsuario", userInDb.IdUsuario.ToString()),
                    new("IdTipoUsuario", userInDb.IdTipoUsuario?.ToString() ?? "0"),
                    new("EstadoAsociado", (userInDb.estadoAsociado ?? false).ToString()),
                    new("TipoCliente", userInDb.TipoCliente?.ToString() ?? "0")
                };

                if (userInDb.idJefe is > 0)
                {
                    claims.Add(new Claim("IdJefe", userInDb.idJefe.Value.ToString()));
                }

                if (!string.IsNullOrWhiteSpace(userInDb.AvatarUrl))
                {
                    claims.Add(new Claim("AvatarUrl", userInDb.AvatarUrl));
                }

                if (userInDb.IdTipoUsuario != null)
                {
                    claims.Add(new Claim(ClaimTypes.Role, userInDb.IdTipoUsuario.ToString()!));
                }

                var identity = new ClaimsIdentity(
                    claims,
                    CookieAuthenticationDefaults.AuthenticationScheme);

                var principal = new ClaimsPrincipal(identity);

                var authProperties = new AuthenticationProperties
                {
                    IsPersistent = request.Recordarme,
                    AllowRefresh = true,
                    IssuedUtc = DateTimeOffset.UtcNow,
                    ExpiresUtc = DateTimeOffset.UtcNow.AddMinutes(30)
                };

                await HttpContext.SignInAsync(
                    CookieAuthenticationDefaults.AuthenticationScheme,
                    principal,
                    authProperties);

                StoreUserSession(userInDb, request.Recordarme);

                return Ok(new
                {
                    success = true,
                    idUsuario = userInDb.IdUsuario,
                    idTipoUsuario = userInDb.IdTipoUsuario,
                    nombres = userInDb.Nombres,
                    apellidos = userInDb.Apellidos,
                    email = userInDb.Email,
                    avatarUrl = userInDb.AvatarUrl,
                    tipoCliente = userInDb.TipoCliente,
                    idJefe = userInDb.idJefe,
                    estadoAsociado = userInDb.estadoAsociado
                });
            }
            catch (Exception)
            {
                return StatusCode(StatusCodes.Status500InternalServerError,
                    "Error al iniciar sesion. Intenta nuevamente en unos segundos.");
            }
        }

        [HttpPost("logout")]
        public async Task<IActionResult> Logout()
        {
            HttpContext.Session.Clear();
            await HttpContext.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
            return Ok(new { success = true });
        }

        private void StoreUserSession(Usuario userInDb, bool recordarme)
        {
            HttpContext.Session.SetInt32("Session.IdUsuario", userInDb.IdUsuario);
            HttpContext.Session.SetString("Session.Nombre", userInDb.Nombres ?? "Usuario");
            HttpContext.Session.SetString("Session.Apellido", userInDb.Apellidos ?? string.Empty);
            HttpContext.Session.SetString("Session.Email", userInDb.Email ?? string.Empty);
            HttpContext.Session.SetString("Session.IdTipoUsuario", userInDb.IdTipoUsuario?.ToString() ?? "0");
            HttpContext.Session.SetString("Session.IdJefe", userInDb.idJefe?.ToString() ?? string.Empty);
            HttpContext.Session.SetString("Session.EstadoAsociado", (userInDb.estadoAsociado ?? false).ToString());
            HttpContext.Session.SetString("Session.Recordarme", recordarme ? "true" : "false");
        }

    }

    public class LoginCookieRequest
    {
        public string Username { get; set; } = "";
        public string Password { get; set; } = "";
        public bool Recordarme { get; set; }
    }
}
