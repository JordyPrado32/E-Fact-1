using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.JSInterop;
using System.Security.Claims;
using System.Text.Json;
using Simetric.Models;
using Simetric.Services;

namespace Simetric.Auth
{
    public class CustomAuthStateProvider : AuthenticationStateProvider
    {
        private readonly IJSRuntime _jsRuntime;
        private readonly CurrentUserContext _currentUserContext;
        private ClaimsPrincipal _anonymous = new ClaimsPrincipal(new ClaimsIdentity());

        public CustomAuthStateProvider(IJSRuntime jsRuntime, CurrentUserContext currentUserContext)
        {
            _jsRuntime = jsRuntime;
            _currentUserContext = currentUserContext;
        }

        public override async Task<AuthenticationState> GetAuthenticationStateAsync()
        {
            try
            {
                var authCheck = await _jsRuntime.InvokeAsync<AuthCheckResponse>("authInterop.check");
                if (authCheck?.Authenticated != true)
                {
                    await _jsRuntime.InvokeVoidAsync("localStorage.removeItem", "userSession");
                    _currentUserContext.Clear();
                    return new AuthenticationState(_anonymous);
                }

                // Leemos la sesión del LocalStorage
                var userSessionJson = await _jsRuntime.InvokeAsync<string>("localStorage.getItem", "userSession");

                if (string.IsNullOrEmpty(userSessionJson))
                {
                    _currentUserContext.Clear();
                    return new AuthenticationState(_anonymous);
                }

                var userSession = JsonSerializer.Deserialize<Usuario>(userSessionJson);

                if (userSession == null)
                {
                    _currentUserContext.Clear();
                    return new AuthenticationState(_anonymous);
                }

                _currentUserContext.SetUserId(userSession.IdUsuario);

                return new AuthenticationState(CreateClaimsPrincipalFromUser(userSession));
            }
            catch
            {
                _currentUserContext.Clear();
                return new AuthenticationState(_anonymous);
            }
        }

        public async Task UpdateAuthenticationState(Usuario? usuario)
        {
            ClaimsPrincipal claimsPrincipal;

            if (usuario != null)
            {
                // Guardamos el objeto completo en LocalStorage (incluye IdTipoUsuario)
                var userSessionJson = JsonSerializer.Serialize(usuario);
                await _jsRuntime.InvokeVoidAsync("localStorage.setItem", "userSession", userSessionJson);
                _currentUserContext.SetUserId(usuario.IdUsuario);
                claimsPrincipal = CreateClaimsPrincipalFromUser(usuario);
            }
            else
            {
                await _jsRuntime.InvokeVoidAsync("localStorage.removeItem", "userSession");
                _currentUserContext.Clear();
                claimsPrincipal = _anonymous;
            }

            NotifyAuthenticationStateChanged(Task.FromResult(new AuthenticationState(claimsPrincipal)));
        }

        private ClaimsPrincipal CreateClaimsPrincipalFromUser(Usuario usuario)
        {
            var claims = new List<Claim>
    {
        new Claim(ClaimTypes.Name, usuario.Nombres ?? "Usuario"),
        new Claim(ClaimTypes.NameIdentifier, usuario.IdUsuario.ToString()),
        // NUEVO: Guardamos apellidos
        new Claim(ClaimTypes.Surname, usuario.Apellidos ?? ""),
        new Claim(ClaimTypes.Email, usuario.Email ?? ""),
        new Claim("IdUsuario", usuario.IdUsuario.ToString()),
        new Claim("IdTipoUsuario", usuario.IdTipoUsuario?.ToString() ?? "0"),
        new Claim("EstadoAsociado", (usuario.estadoAsociado ?? false).ToString()),
        new Claim("TipoCliente", usuario.TipoCliente?.ToString() ?? "0")
    };

            if (usuario.idJefe is > 0)
            {
                claims.Add(new Claim("IdJefe", usuario.idJefe.Value.ToString()));
            }

            if (!string.IsNullOrWhiteSpace(usuario.AvatarUrl))
            {
                claims.Add(new Claim("AvatarUrl", usuario.AvatarUrl));
            }

            if (usuario.IdTipoUsuario != null)
            {
                claims.Add(new Claim(ClaimTypes.Role, usuario.IdTipoUsuario.ToString()!));
            }

            return new ClaimsPrincipal(new ClaimsIdentity(claims, "CustomAuth"));
        }

        private sealed class AuthCheckResponse
        {
            public bool Authenticated { get; set; }
            public int IdUsuario { get; set; }
        }
    }
}
