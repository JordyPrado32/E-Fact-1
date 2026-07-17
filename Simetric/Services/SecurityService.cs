using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Primitives;
using Microsoft.JSInterop;
using Simetric.Auth;
using Simetric.Data;
using Simetric.Models;

namespace Simetric.Services
{
    public class SecurityService
    {
        private readonly IDbContextFactory<AppDbContext> _contextFactory;
        private readonly IJSRuntime _js;
        private readonly IHttpContextAccessor _http;

        public SecurityService(
            IDbContextFactory<AppDbContext> contextFactory,
            IJSRuntime js,
            IHttpContextAccessor http)
        {
            _contextFactory = contextFactory;
            _js = js;
            _http = http;
        }

        // ✅ LOGS (LOGIN / MFA / ERRORES)
        public async Task RegistrarLogInicioSesionAsync(int? idUsuario, bool exitoso, string? detalle)
        {
            try
            {
                using var ctx = await _contextFactory.CreateDbContextAsync();
                await PurgarLogsFueraDeMesActualAsync(ctx, DateTime.Now);
                var httpCtx = _http.HttpContext;

                // IP (si hay proxy toma X-Forwarded-For)
                string ip = "N/A";

                if (httpCtx != null)
                {
                    // 1. Cloudflare (si algún día lo usas)
                    if (httpCtx.Request.Headers.TryGetValue("CF-Connecting-IP", out var cfIp))
                    {
                        ip = cfIp.ToString();
                    }
                    // 2. Proxy / IIS / Nginx
                    else if (httpCtx.Request.Headers.TryGetValue("X-Forwarded-For", out var xff))
                    {
                        ip = xff.ToString().Split(',')[0].Trim();
                    }
                    // 3. Último recurso (directo)
                    else if (httpCtx.Connection.RemoteIpAddress != null)
                    {
                        ip = httpCtx.Connection.RemoteIpAddress.ToString();
                    }
                }


                // User-Agent
                string ua = httpCtx?.Request?.Headers["User-Agent"].ToString() ?? "N/A";
                if (ua.Length > 250) ua = ua[..250];

                ctx.LogIniciosSesiones.Add(new LogIniciosSesion
                {
                    IdUsuario = idUsuario,
                    FechaAcceso = DateTime.Now,
                    DireccionIp = ip,
                    Navegador = ua,
                    Exitoso = exitoso,
                    DetalleError = string.IsNullOrWhiteSpace(detalle) ? null : detalle
                });

                await ctx.SaveChangesAsync();
            }
            catch
            {
                // No romper el login por fallos de auditoría
            }
        }

        public async Task<int> AsegurarRetencionMensualLogsAsync(DateTime? fechaReferencia = null)
        {
            try
            {
                using var ctx = await _contextFactory.CreateDbContextAsync();
                return await PurgarLogsFueraDeMesActualAsync(ctx, fechaReferencia ?? DateTime.Now);
            }
            catch
            {
                return 0;
            }
        }

        private static Task<int> PurgarLogsFueraDeMesActualAsync(AppDbContext ctx, DateTime fechaReferencia)
        {
            var inicioMesActual = new DateTime(fechaReferencia.Year, fechaReferencia.Month, 1);

            return ctx.LogIniciosSesiones
                .Where(x => !x.FechaAcceso.HasValue || x.FechaAcceso.Value < inicioMesActual)
                .ExecuteDeleteAsync();
        }

        // ✅ TU LÓGICA EXISTENTE (no la toco, solo la dejo)
        public async Task<LoginResult> ValidarAcceso(LoginModel model)
        {
            using var _context = await _contextFactory.CreateDbContextAsync();

            var usuario = await _context.Usuarios
                .FirstOrDefaultAsync(u => u.Email == model.Email);

            if (usuario == null) return LoginResult.Fallido("Usuario no registrado.");

            bool esValido = Simetric.Components.Helpers.SecurityHelper.VerifyPassword(model.Password, usuario.PasswordHash);

            if (esValido && usuario.ClaveTemporal == true)
            {
                usuario.CuentaBloqueada = false;
                usuario.IntentosFallidos = 0;
                usuario.FechaDesbloqueo = null;
                await _context.SaveChangesAsync();
                return new LoginResult { RequiereCambioClave = true, Usuario = usuario, Exito = true };
            }

            if (usuario.CuentaBloqueada == true && (usuario.FechaDesbloqueo > DateTime.Now))
            {
                return LoginResult.Fallido("Cuenta bloqueada por seguridad. Use la clave enviada a su correo.");
            }

            if (!esValido)
            {
                usuario.IntentosFallidos = (usuario.IntentosFallidos ?? 0) + 1;
                if (usuario.IntentosFallidos >= 3)
                {
                    string claveTemporalRaw = GenerarClaveTemporal(8);
                    usuario.CuentaBloqueada = true;
                    usuario.ClaveTemporal = true;
                    usuario.FechaDesbloqueo = DateTime.Now.AddMinutes(30);
                    usuario.PasswordHash = Simetric.Components.Helpers.SecurityHelper.HashPassword(claveTemporalRaw);
                    await _context.SaveChangesAsync();

                    return LoginResult.Fallido("Bloqueo activado. Se envió una clave temporal a su correo.");
                }
                await _context.SaveChangesAsync();
                return LoginResult.Fallido($"Contraseña incorrecta. Intentos: {usuario.IntentosFallidos}/3");
            }

            usuario.IntentosFallidos = 0;
            usuario.CuentaBloqueada = false;
            await _context.SaveChangesAsync();

            return LoginResult.Exitoso(usuario);
        }

        private static string GenerarClaveTemporal(int longitud = 8)
        {
            const string caracteres = "ABCDEFGHJKLMNPQRSTUVWXYZabcdefghijkmnopqrstuvwxyz23456789";
            var resultado = new char[longitud];
            var bytes = new byte[longitud];
            System.Security.Cryptography.RandomNumberGenerator.Fill(bytes);
            for (int i = 0; i < longitud; i++)
                resultado[i] = caracteres[bytes[i] % caracteres.Length];
            return new string(resultado);
        }

        public async Task Logout(NavigationManager nav)
        {
            bool confirmar = await _js.InvokeAsync<bool>("confirm", "¿Deseas cerrar sesión?");
            if (confirmar)
            {
                try
                {
                    await _js.InvokeVoidAsync("numericaLogoutAndRedirect", "/login");
                }
                catch (JSDisconnectedException)
                {
                    // El navegador ya esta cerrando o recargando el circuito.
                }
                catch (JSException)
                {
                    // Evita romper el circuito si el navegador aun tenia JS anterior en cache.
                }
            }
        }
    }
}
