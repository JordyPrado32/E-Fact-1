using Microsoft.AspNetCore.Components;
using Microsoft.EntityFrameworkCore;
using Microsoft.JSInterop;
using Simetric.Auth; // Donde definiremos LoginResult y LoginModel
using Simetric.Data;
using Simetric.Models;

namespace Simetric.Services
{
    public class AuthService // Lo llamamos SecurityService como en tu Program.cs
    {
        private readonly IDbContextFactory<AppDbContext> _contextFactory;
        // private readonly IEmailService _emailService; // Descomenta cuando migres el servicio de correo
        private readonly IJSRuntime _js;

        public AuthService(IDbContextFactory<AppDbContext> contextFactory, IJSRuntime js)
        {
            _contextFactory = contextFactory;
            _js = js;
        }

        public async Task<LoginResult> ValidarAcceso(LoginModel model)
        {
            using var _context = await _contextFactory.CreateDbContextAsync();

            var usuario = await _context.Usuarios
                .FirstOrDefaultAsync(u => u.Email == model.Email);

            if (usuario == null) return LoginResult.Fallido("Usuario no registrado.");

            // 1. Verificar Password (Asumimos que tienes una clase Helper para esto)
            // Si aún no tienes SecurityHelper, puedes usar BCrypt o un hash simple por ahora
            bool esValido = Simetric.Components.Helpers.SecurityHelper.VerifyPassword(model.Password, usuario.PasswordHash);

            // 2. Manejo de Clave Temporal
            if (esValido && usuario.ClaveTemporal == true)
            {
                usuario.CuentaBloqueada = false;
                usuario.IntentosFallidos = 0;
                usuario.FechaDesbloqueo = null;
                await _context.SaveChangesAsync();
                return new LoginResult { RequiereCambioClave = true, Usuario = usuario, Exito = true };
            }

            // 3. Validar bloqueo por tiempo
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

                    // Aquí iría la lógica de envío de correo
                    return LoginResult.Fallido("Bloqueo activado. Se envió una clave temporal a su correo.");
                }
                await _context.SaveChangesAsync();
                return LoginResult.Fallido($"Contraseña incorrecta. Intentos: {usuario.IntentosFallidos}/3");
            }

            // --- LOGIN EXITOSO ---
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
