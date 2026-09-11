using System.Security.Claims;
using Microsoft.EntityFrameworkCore;
using Simetric.Data;

namespace Simetric.Services;

public static class BackOfficePermissionHelper
{
    private const string UsuarioERubricaEmail = "servicioalcliente@numerosasesores.com";

    public const int SuperAdministradorRoleId = 2;
    public const int BackOfficeRoleId = 7;
    public const int AdministradorBackOfficeTipoCliente = 1;
    public const int CobranzasBackOfficeTipoCliente = 3;

    public static bool PuedeAprobarTransferencias(int? idTipoUsuario, int? tipoCliente) =>
        idTipoUsuario == SuperAdministradorRoleId ||
        (idTipoUsuario == BackOfficeRoleId &&
            (tipoCliente == AdministradorBackOfficeTipoCliente ||
             tipoCliente == CobranzasBackOfficeTipoCliente));

    public static bool PuedeVerERubrica(string? email) =>
        string.Equals(email?.Trim(), UsuarioERubricaEmail, StringComparison.OrdinalIgnoreCase);

    public static bool PuedeAccederERubrica(int? idTipoUsuario, int? tipoCliente, string? email) =>
        PuedeVerERubrica(email) ||
        idTipoUsuario == SuperAdministradorRoleId ||
        (idTipoUsuario == BackOfficeRoleId && tipoCliente == AdministradorBackOfficeTipoCliente);

    public static async Task<bool> PuedeAccederERubricaAsync(
        ClaimsPrincipal user,
        IDbContextFactory<AppDbContext> dbFactory)
    {
        if (!int.TryParse(user.FindFirst("IdUsuario")?.Value, out var idUsuario))
        {
            return PuedeAccederERubrica(
                int.TryParse(user.FindFirst("IdTipoUsuario")?.Value, out var idTipoUsuario) ? idTipoUsuario : null,
                int.TryParse(user.FindFirst("TipoCliente")?.Value, out var tipoCliente) ? tipoCliente : null,
                user.FindFirst(ClaimTypes.Email)?.Value);
        }

        await using var context = await dbFactory.CreateDbContextAsync();
        var usuarioActual = await context.Usuarios
            .AsNoTracking()
            .Where(usuario => usuario.IdUsuario == idUsuario)
            .Select(usuario => new { usuario.Email, usuario.IdTipoUsuario, usuario.TipoCliente })
            .FirstOrDefaultAsync();

        return usuarioActual is not null &&
            PuedeAccederERubrica(usuarioActual.IdTipoUsuario, usuarioActual.TipoCliente, usuarioActual.Email);
    }
}
