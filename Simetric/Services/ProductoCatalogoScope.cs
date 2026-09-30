using Microsoft.EntityFrameworkCore;
using Simetric.Data;

namespace Simetric.Services;

public static class ProductoCatalogoScope
{
    public static async Task<int?> ResolverAsync(AppDbContext context, int userId, bool backOffice)
    {
        context.CatalogoEmisorId = null;
        var usuario = await context.Usuarios.AsNoTracking()
            .Where(u => u.IdUsuario == userId)
            .Select(u => new { u.IdUsuario, u.idJefe, u.IdTipoUsuario, u.Email })
            .FirstOrDefaultAsync();

        if (usuario is null) return null;
        if (!backOffice) return usuario.idJefe ?? usuario.IdUsuario;

        if (usuario.IdTipoUsuario is not (BackOfficePermissionHelper.BackOfficeRoleId or BackOfficePermissionHelper.SuperAdministradorRoleId) &&
            !BackOfficePermissionHelper.PuedeGestionarCatalogo(usuario.Email))
            return null;

        var ownerId = await context.Emisores.AsNoTracking()
            .Where(e => e.Codigo == EmisorSistemaService.CodigoEmisorBackOffice && e.Estado && e.EsEmisorSistema)
            .Select(e => e.IdUsuario)
            .FirstOrDefaultAsync();

        context.CatalogoEmisorId = EmisorSistemaService.CodigoEmisorBackOffice;
        return ownerId is > 0 ? ownerId : null;
    }
}
