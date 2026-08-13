using System.Security.Claims;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Simetric.Components.Helpers;
using Simetric.Data;
using Simetric.Models;
using Simetric.Services;
using HelperSecurity = Simetric.Components.Helpers.SecurityHelper;

namespace Simetric.Controllers
{
    [ApiController]
    [Route("api/auth")]
    public class AuthController : ControllerBase
    {
        private readonly IDbContextFactory<AppDbContext> _dbFactory;
        private readonly ClienteService _clienteService;
        private readonly IEmailService _emailService;

        public AuthController(
            IDbContextFactory<AppDbContext> dbFactory,
            ClienteService clienteService,
            IEmailService emailService)
        {
            _dbFactory = dbFactory;
            _clienteService = clienteService;
            _emailService = emailService;
        }

        [HttpGet("check")]
        public async Task<IActionResult> Check()
        {
            Response.Headers.CacheControl = "no-store, no-cache, max-age=0";
            Response.Headers.Pragma = "no-cache";

            if (User.Identity?.IsAuthenticated == true)
            {
                var claimValue = User.FindFirst("IdUsuario")?.Value;
                var idUsuario = int.TryParse(claimValue, out var parsedId) ? parsedId : 0;
                if (idUsuario <= 0)
                {
                    return Ok(new { authenticated = false, idUsuario = 0 });
                }

                await using var context = await _dbFactory.CreateDbContextAsync();
                var userInDb = await context.Usuarios.AsNoTracking().FirstOrDefaultAsync(u => u.IdUsuario == idUsuario);

                if (userInDb is null || userInDb.Estado != true)
                {
                    return Ok(new { authenticated = false, idUsuario = 0 });
                }

                return Ok(new
                {
                    authenticated = true,
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

                var isPasswordValid = HelperSecurity.VerifyPassword(request.Password.Trim(), userInDb.PasswordHash);

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
                    ExpiresUtc = request.Recordarme
                        ? DateTimeOffset.UtcNow.AddDays(30)
                        : DateTimeOffset.UtcNow.AddMinutes(30)
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

        [HttpPost("register")]
        public async Task<IActionResult> Register([FromBody] RegisterApiRequest request)
        {
            try
            {
                if (request is null ||
                    string.IsNullOrWhiteSpace(request.Email) ||
                    string.IsNullOrWhiteSpace(request.Password) ||
                    string.IsNullOrWhiteSpace(request.Identificacion))
                {
                    return BadRequest("Los datos de registro son obligatorios.");
                }

                var email = request.Email.Trim().ToLowerInvariant();
                var nombres = request.TipoCliente == 2 ? request.RazonSocial?.Trim() : request.Nombres?.Trim();

                if (string.IsNullOrWhiteSpace(nombres))
                {
                    return BadRequest("El nombre o razon social es obligatorio.");
                }

                int usuarioId = 0;

                await using var strategyDb = await _dbFactory.CreateDbContextAsync();
                var executionStrategy = strategyDb.Database.CreateExecutionStrategy();

                await executionStrategy.ExecuteAsync(async () =>
                {
                    await using var db = await _dbFactory.CreateDbContextAsync();
                    await using var transaction = await db.Database.BeginTransactionAsync();

                    var existente = await db.Usuarios.AsNoTracking().FirstOrDefaultAsync(u => u.Email == email);
                    if (existente is not null)
                    {
                        throw new InvalidOperationException("El correo ya esta registrado. Intenta iniciar sesion o usa otro email.");
                    }

                    var finalAvatarUrl = string.IsNullOrWhiteSpace(request.AvatarUrl)
                        ? "images/Avatars/Avatar-Boy.jpg"
                        : request.AvatarUrl.Trim();

                    var usuario = new Usuario
                    {
                        Nombres = request.TipoCliente == 2 ? request.RazonSocial.Trim() : request.Nombres.Trim(),
                        Apellidos = request.TipoCliente == 2 ? string.Empty : (request.Apellidos ?? string.Empty).Trim(),
                        NombreEmpresa = request.TipoCliente == 2 ? request.RazonSocial.Trim() : string.Empty,
                        Email = email,
                        DireccionEmpresa = request.Direccion?.Trim(),
                        AvatarUrl = finalAvatarUrl,
                        Celular = request.Celular?.Trim(),
                        Identificacion = request.Identificacion.Trim(),
                        IdTipoIdentificacion = request.TipoDocumento?.Trim().ToUpperInvariant() switch
                        {
                            "RUC" => 2,
                            "PASAPORTE" => 3,
                            _ => 1
                        },
                        PasswordHash = HelperSecurity.HashPassword(request.Password),
                        IdTipoUsuario = 1,
                        Estado = true,
                        FechaCreacion = DateTime.Now,
                        SaldoDocumentos = 5,
                        TipoCliente = request.TipoCliente <= 0 ? 1 : request.TipoCliente
                    };

                    db.Usuarios.Add(usuario);
                    await db.SaveChangesAsync();
                    await _clienteService.EnsureConsumidorFinalAsync(db, usuario.IdUsuario);
                    await transaction.CommitAsync();

                    usuarioId = usuario.IdUsuario;
                });

                try
                {
                    await _emailService.EnviarCuentaCreadaAsync(email, nombres);
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"La cuenta se creo, pero no se pudo enviar el correo de confirmacion: {ex.Message}");
                }

                return Ok(new
                {
                    success = true,
                    idUsuario = usuarioId,
                    message = "Cuenta creada correctamente. Ya puedes iniciar sesion."
                });
            }
            catch (InvalidOperationException ex)
            {
                return BadRequest(ex.Message);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error al registrar usuario desde API: {ex.Message}");
                return StatusCode(StatusCodes.Status500InternalServerError,
                    "Error al registrar la cuenta. Intenta nuevamente en unos segundos.");
            }
        }

        [HttpPost("recover-password")]
        public async Task<IActionResult> RecoverPassword([FromBody] RecoverPasswordApiRequest request)
        {
            const string successMessage = "Si el correo existe, enviaremos un codigo de acceso para recuperar tu clave.";

            try
            {
                if (request is null || string.IsNullOrWhiteSpace(request.Email))
                {
                    return BadRequest("El correo es obligatorio.");
                }

                var email = request.Email.Trim().ToLowerInvariant();
                await using var db = await _dbFactory.CreateDbContextAsync();
                var usuario = await db.Usuarios.FirstOrDefaultAsync(u => u.Email.ToLower() == email);

                if (usuario is null)
                {
                    return Ok(new { success = true, message = successMessage });
                }

                const int minutosExpira = RecoveryCodeHelper.MinutosExpiracionPorDefecto;
                const string passwordTemporal = "00000000";
                var codigoAcceso = RecoveryCodeHelper.GenerarCodigoNumerico();
                var passwordHashAnterior = usuario.PasswordHash;
                var claveTemporalAnterior = usuario.ClaveTemporal;
                var cuentaBloqueadaAnterior = usuario.CuentaBloqueada;
                var fechaDesbloqueoAnterior = usuario.FechaDesbloqueo;
                var fechaExpiracionAnterior = usuario.FechaExpiracionToken;
                var tokenRecuperacionAnterior = usuario.TokenRecuperacion;
                var intentosAnteriores = usuario.IntentosFallidos;

                usuario.PasswordHash = HelperSecurity.HashPassword(passwordTemporal);
                usuario.ClaveTemporal = true;
                usuario.CuentaBloqueada = false;
                usuario.FechaDesbloqueo = null;
                usuario.IntentosFallidos = 0;
                usuario.FechaExpiracionToken = DateTime.Now.AddMinutes(minutosExpira);
                usuario.TokenRecuperacion = HelperSecurity.HashPassword(codigoAcceso);

                await db.SaveChangesAsync();

                try
                {
                    await _emailService.EnviarClaveTemporal(usuario.Email, codigoAcceso, minutosExpira, "Recuperacion de acceso");
                }
                catch
                {
                    usuario.PasswordHash = passwordHashAnterior;
                    usuario.ClaveTemporal = claveTemporalAnterior;
                    usuario.CuentaBloqueada = cuentaBloqueadaAnterior;
                    usuario.FechaDesbloqueo = fechaDesbloqueoAnterior;
                    usuario.FechaExpiracionToken = fechaExpiracionAnterior;
                    usuario.TokenRecuperacion = tokenRecuperacionAnterior;
                    usuario.IntentosFallidos = intentosAnteriores;
                    await db.SaveChangesAsync();
                    throw;
                }

                return Ok(new
                {
                    success = true,
                    message = "Te enviamos un codigo de acceso a tu correo.",
                    idUsuario = usuario.IdUsuario
                });
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error en recuperacion desde API: {ex.Message}");
                return StatusCode(StatusCodes.Status500InternalServerError,
                    "No se pudo enviar el codigo de recuperacion. Intenta nuevamente.");
            }
        }

        [HttpPost("change-password")]
        public async Task<IActionResult> ChangePassword([FromBody] ChangePasswordApiRequest request)
        {
            try
            {
                if (request is null ||
                    request.IdUsuario <= 0 ||
                    string.IsNullOrWhiteSpace(request.ClaveActual) ||
                    string.IsNullOrWhiteSpace(request.NuevaClave))
                {
                    return BadRequest("Los datos para cambiar la clave son obligatorios.");
                }

                if (request.NuevaClave != request.ConfirmarClave)
                {
                    return BadRequest("La confirmacion de clave no coincide.");
                }

                await using var db = await _dbFactory.CreateDbContextAsync();
                var usuario = await db.Usuarios.FindAsync(request.IdUsuario);

                if (usuario is null || usuario.ClaveTemporal != true)
                {
                    return BadRequest("La sesion de cambio de clave ha expirado.");
                }

                if (usuario.FechaExpiracionToken.HasValue && usuario.FechaExpiracionToken.Value < DateTime.Now)
                {
                    return BadRequest("El codigo de acceso expiro. Solicita uno nuevo.");
                }

                if (!EsCodigoAccesoValido(usuario, request.ClaveActual.Trim()))
                {
                    return BadRequest("El codigo de acceso no es correcto. Verifica el correo e intentalo de nuevo.");
                }

                usuario.PasswordHash = HelperSecurity.HashPassword(request.NuevaClave);
                usuario.ClaveTemporal = false;
                usuario.CuentaBloqueada = false;
                usuario.IntentosFallidos = 0;
                usuario.FechaDesbloqueo = null;
                usuario.FechaExpiracionToken = null;
                usuario.TokenRecuperacion = null;

                await db.SaveChangesAsync();

                return Ok(new { success = true, message = "Clave actualizada correctamente. Ya puedes iniciar sesion." });
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error al cambiar clave desde API: {ex.Message}");
                return StatusCode(StatusCodes.Status500InternalServerError,
                    "No se pudo actualizar la clave. Intenta nuevamente.");
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

        private static bool EsCodigoAccesoValido(Usuario usuario, string codigoAcceso)
        {
            if (string.IsNullOrWhiteSpace(codigoAcceso))
            {
                return false;
            }

            var tokenValido = !string.IsNullOrWhiteSpace(usuario.TokenRecuperacion) &&
                HelperSecurity.VerifyPassword(codigoAcceso, usuario.TokenRecuperacion);

            var claveTemporalValida = !string.IsNullOrWhiteSpace(usuario.PasswordHash) &&
                HelperSecurity.VerifyPassword(codigoAcceso, usuario.PasswordHash);

            return tokenValido || claveTemporalValida;
        }

    }

    public class LoginCookieRequest
    {
        public string Username { get; set; } = "";
        public string Password { get; set; } = "";
        public bool Recordarme { get; set; }
    }

    public class RegisterApiRequest
    {
        public string Nombres { get; set; } = "";
        public string Apellidos { get; set; } = "";
        public string RazonSocial { get; set; } = "";
        public string Email { get; set; } = "";
        public string Direccion { get; set; } = "";
        public string Celular { get; set; } = "";
        public string TipoDocumento { get; set; } = "CEDULA";
        public string Identificacion { get; set; } = "";
        public string Password { get; set; } = "";
        public string AvatarUrl { get; set; } = "";
        public int TipoCliente { get; set; } = 1;
    }

    public class RecoverPasswordApiRequest
    {
        public string Email { get; set; } = "";
    }

    public class ChangePasswordApiRequest
    {
        public int IdUsuario { get; set; }
        public string ClaveActual { get; set; } = "";
        public string NuevaClave { get; set; } = "";
        public string ConfirmarClave { get; set; } = "";
    }
}
