using Microsoft.EntityFrameworkCore;
using Simetric.Data;

namespace Simetric.Services;

public sealed class LoginDestinationService
{
    private readonly IDbContextFactory<AppDbContext> _dbFactory;
    private readonly SelectedAppServiceStateService _selectedAppServiceState;
    private readonly EmisorOnboardingService _emisorOnboardingService;
    private readonly AppAccessService _appAccessService;

    public LoginDestinationService(
        IDbContextFactory<AppDbContext> dbFactory,
        SelectedAppServiceStateService selectedAppServiceState,
        EmisorOnboardingService emisorOnboardingService,
        AppAccessService appAccessService)
    {
        _dbFactory = dbFactory;
        _selectedAppServiceState = selectedAppServiceState;
        _emisorOnboardingService = emisorOnboardingService;
        _appAccessService = appAccessService;
    }

    public async Task<string> PrepareDestinationAsync(int userId, int? roleId = null, string? returnUrl = null)
    {
        if (userId <= 0)
        {
            return "/login";
        }

        if (!string.IsNullOrWhiteSpace(returnUrl) && IsLocalUrl(returnUrl))
        {
            return returnUrl;
        }

        await _emisorOnboardingService.RefreshRequirementAsync(userId);

        var effectiveRoleId = roleId;
        if (!effectiveRoleId.HasValue)
        {
            try
            {
                await using var context = await _dbFactory.CreateDbContextAsync();
                effectiveRoleId = await context.Usuarios
                    .AsNoTracking()
                    .Where(u => u.IdUsuario == userId)
                    .Select(u => u.IdTipoUsuario)
                    .FirstOrDefaultAsync();
            }
            catch
            {
                // Si no se puede consultar el rol, se prioriza el acceso directo al servicio funcional.
                effectiveRoleId = null;
            }
        }

        if (await EsAliadoAdministradorAsync(effectiveRoleId))
        {
            return AliadoPortalService.AdminRoute;
        }

        if (await EsAliadoAsync(userId, effectiveRoleId))
        {
            return AliadoPortalService.RootRoute;
        }

        if (string.Equals(
                effectiveRoleId?.ToString(),
                "7",
                StringComparison.OrdinalIgnoreCase))
        {
            return "/backoffice";
        }

        if (string.Equals(
                effectiveRoleId?.ToString(),
                AppAccessService.AdminRoleId,
                StringComparison.OrdinalIgnoreCase))
        {
            return "/portal-servicios";
        }

        var ultimoServicio = await _selectedAppServiceState.GetCurrentServiceKeyAsync(userId);
        if (!string.IsNullOrWhiteSpace(ultimoServicio))
        {
            var acceso = await _appAccessService.CanAccessAsync(userId, ultimoServicio, false);
            if (acceso.HasAccess && !string.IsNullOrWhiteSpace(acceso.Route))
            {
                return acceso.Route;
            }
        }

        await _selectedAppServiceState.SetCurrentServiceKeyAsync(AppAccessService.FreeServiceKey, userId);
        return "/dashboard";
    }

    private async Task<bool> EsAliadoAsync(int userId, int? roleId)
    {
        await using var context = await _dbFactory.CreateDbContextAsync();
        if (roleId is > 0 && await context.TipoUsuario
                .AsNoTracking()
                .AnyAsync(x => x.IdTipoUsuario == roleId.Value &&
                              x.Estado == true &&
                              x.NombreTipo == AliadoPortalService.RoleName))
            return true;

        return await context.AliadoPortalUsuariosRoles
            .AsNoTracking()
            .Where(x => x.IdUsuario == userId)
            .Join(context.AliadoPortalRoles.AsNoTracking().Where(x => x.Activo), x => x.IdRol, x => x.IdRol, (_, role) => role)
            .AnyAsync(x => x.Nombre == AliadoPortalService.RoleName);
    }

    private async Task<bool> EsAliadoAdministradorAsync(int? roleId)
    {
        if (roleId is not > 0)
            return false;

        await using var context = await _dbFactory.CreateDbContextAsync();
        return await context.TipoUsuario
            .AsNoTracking()
            .AnyAsync(x => x.IdTipoUsuario == roleId.Value &&
                          x.Estado == true &&
                          x.NombreTipo == AliadoPortalService.AdminRoleName);
    }

    private static bool IsLocalUrl(string url)
    {
        if (string.IsNullOrEmpty(url)) return false;
        if (url[0] == '/' && (url.Length == 1 || (url[1] != '/' && url[1] != '\\'))) return true;
        return false;
    }
}
