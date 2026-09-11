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

    public static async Task<bool> PuedeVerERubricaAsync(
        ClaimsPrincipal user,
        IDbContextFactory<AppDbContext> dbFactory)
    {
        if (PuedeVerERubrica(user.FindFirst(ClaimTypes.Email)?.Value))
        {
            return true;
        }

        if (!int.TryParse(user.FindFirst("IdUsuario")?.Value, out var idUsuario))
        {
            return false;
        }

        await using var context = await dbFactory.CreateDbContextAsync();
        var emailActual = await context.Usuarios
            .AsNoTracking()
            .Where(usuario => usuario.IdUsuario == idUsuario)
            .Select(usuario => usuario.Email)
            .FirstOrDefaultAsync();

        return PuedeVerERubrica(emailActual);
    }
}
