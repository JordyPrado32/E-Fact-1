using System.Security.Claims;
using System.Text;
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

        [HttpGet("check")]
        public IActionResult Check()
        {
            if (User.Identity?.IsAuthenticated == true)
            {
                var idUsuario = User.FindFirst("IdUsuario")?.Value ?? "0";
                return Ok(new { authenticated = true, idUsuario = int.Parse(idUsuario) });
            }

            return Ok(new { authenticated = false, idUsuario = 0 });
        }

        public AuthController(IDbContextFactory<AppDbContext> dbFactory)
        {
            _dbFactory = dbFactory;
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
                    return Unauthorized("Usuario no registrado.");

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

                bool isPasswordValid = SecurityHelper.VerifyPassword(request.Password.Trim(), userInDb.PasswordHash);

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

            if (!esEmpleadoBackOffice && userInDb.ClaveTemporal == true)
            {
                return Ok(new
                {
                        requiereCambioClave = true,
                        idUsuario = userInDb.IdUsuario
                    });
                }

                if (esEmpleadoBackOffice && userInDb.ClaveTemporal == true)
                {
                    userInDb.ClaveTemporal = false;
                    userInDb.TokenRecuperacion = null;
                    userInDb.FechaExpiracionToken = null;
                }

                if (userInDb.MfaHabilitado == true)
                {
                    HttpContext.Session.SetInt32("PendingMfa.IdUsuario", userInDb.IdUsuario);
                    HttpContext.Session.SetString("PendingMfa.Recordarme", request.Recordarme ? "true" : "false");

                    return Ok(new
                    {
                        requiereMfa = true,
                        idUsuario = userInDb.IdUsuario
                    });
                }

                userInDb.IntentosFallidos = 0;
                userInDb.CuentaBloqueada = false;
                await context.SaveChangesAsync();

                var claims = new List<Claim>
                {
                    new Claim(ClaimTypes.Name, userInDb.Nombres ?? "Usuario"),
                    new Claim(ClaimTypes.NameIdentifier, userInDb.IdUsuario.ToString()),
                    new Claim(ClaimTypes.Surname, userInDb.Apellidos ?? ""),
                    new Claim(ClaimTypes.Email, userInDb.Email ?? ""),
                    new Claim("IdUsuario", userInDb.IdUsuario.ToString()),
                    new Claim("IdTipoUsuario", userInDb.IdTipoUsuario?.ToString() ?? "0"),
                    new Claim("EstadoAsociado", (userInDb.estadoAsociado ?? false).ToString()),
                    new Claim("TipoCliente", userInDb.TipoCliente?.ToString() ?? "0")
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
            catch (Exception ex)
            {
                var diagnostic = await BuildRuntimeDatabaseDiagnosticAsync();
                return StatusCode(StatusCodes.Status500InternalServerError,
                    $"Error al iniciar sesion. {diagnostic}Detalle: {ex.Message}");
            }
        }

        [HttpPost("mfa-login")]
        public async Task<IActionResult> MfaLogin([FromBody] MfaLoginRequest request)
        {
            if (request == null || request.IdUsuario <= 0)
                return BadRequest("Solicitud inválida.");

            var pendingUserId = HttpContext.Session.GetInt32("PendingMfa.IdUsuario");
            if (pendingUserId != request.IdUsuario)
                return Unauthorized("La validacion MFA expiro. Vuelve a iniciar sesion.");

            var recordarmePendiente = string.Equals(
                HttpContext.Session.GetString("PendingMfa.Recordarme"),
                "true",
                StringComparison.OrdinalIgnoreCase);

            using var context = await _dbFactory.CreateDbContextAsync();
            var userInDb = await context.Usuarios.FindAsync(request.IdUsuario);

            if (userInDb == null)
                return Unauthorized("Usuario no encontrado.");

            if (userInDb.Estado != true)
            {
                if (userInDb.idJefe is > 0 && userInDb.estadoAsociado != true)
                {
                    return Unauthorized("Tu solicitud de asociado esta pendiente de aprobacion.");
                }

                return Unauthorized("Tu cuenta está desactivada por el administrador.");
            }

            var claims = new List<Claim>
            {
                new Claim(ClaimTypes.Name, userInDb.Nombres ?? "Usuario"),
                new Claim(ClaimTypes.NameIdentifier, userInDb.IdUsuario.ToString()),
                new Claim(ClaimTypes.Surname, userInDb.Apellidos ?? ""),
                new Claim(ClaimTypes.Email, userInDb.Email ?? ""),
                new Claim("IdUsuario", userInDb.IdUsuario.ToString()),
                new Claim("IdTipoUsuario", userInDb.IdTipoUsuario?.ToString() ?? "0"),
                new Claim("EstadoAsociado", (userInDb.estadoAsociado ?? false).ToString())
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

            var principal = new ClaimsPrincipal(
                new ClaimsIdentity(claims, CookieAuthenticationDefaults.AuthenticationScheme));

            var authProperties = new AuthenticationProperties
            {
                IsPersistent = recordarmePendiente,
                AllowRefresh = true,
                IssuedUtc = DateTimeOffset.UtcNow,
                ExpiresUtc = DateTimeOffset.UtcNow.AddMinutes(30)
            };

            await HttpContext.SignInAsync(
                CookieAuthenticationDefaults.AuthenticationScheme,
                principal,
                authProperties);

            HttpContext.Session.Remove("PendingMfa.IdUsuario");
            HttpContext.Session.Remove("PendingMfa.Recordarme");
            StoreUserSession(userInDb, recordarmePendiente);

            return Ok(new { success = true });
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

        private async Task<string> BuildRuntimeDatabaseDiagnosticAsync()
        {
            try
            {
                await using var context = await _dbFactory.CreateDbContextAsync();
                var connection = context.Database.GetDbConnection();
                var usuariosObjectId = "NULL";
                var saldoColumnLength = "NULL";

                if (connection.State != System.Data.ConnectionState.Open)
                {
                    await connection.OpenAsync();
                }

                await using var command = connection.CreateCommand();
                command.CommandText = """
                    SELECT
                        OBJECT_ID(N'Usuarios') AS UsuariosObjectId,
                        COL_LENGTH(N'Usuarios', N'SaldoDocumentos') AS SaldoDocumentosLength;
                    """;

                await using var reader = await command.ExecuteReaderAsync();
                if (await reader.ReadAsync())
                {
                    usuariosObjectId = reader.IsDBNull(0) ? "NULL" : reader.GetValue(0).ToString() ?? "NULL";
                    saldoColumnLength = reader.IsDBNull(1) ? "NULL" : reader.GetValue(1).ToString() ?? "NULL";
                }

                return new StringBuilder()
                    .Append("Diagnostico runtime -> ")
                    .Append("DataSource: ").Append(connection.DataSource)
                    .Append(", Database: ").Append(connection.Database)
                    .Append(", Usuarios OBJECT_ID: ").Append(usuariosObjectId)
                    .Append(", SaldoDocumentos COL_LENGTH: ").Append(saldoColumnLength)
                    .Append(", BaseDir: ").Append(AppContext.BaseDirectory)
                    .Append(". ")
                    .ToString();
            }
            catch (Exception diagnosticEx)
            {
                return $"Diagnostico runtime no disponible ({diagnosticEx.Message}). ";
            }
        }
    }

    public class LoginCookieRequest
    {
        public string Username { get; set; } = "";
        public string Password { get; set; } = "";
        public bool Recordarme { get; set; }
    }

    public class MfaLoginRequest
    {
        public int IdUsuario { get; set; }
        public bool Recordarme { get; set; }
    }
}
